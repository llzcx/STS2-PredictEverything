using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace PredictEverything;

/// <summary>
/// Right-side dashboard showing locked-in CrystalSphere rewards.
/// Displays Rare/Uncommon/Common/Relic locked status with reward names,
/// potion reveal progress, and remaining gold clicks.
/// Subscribes to Predictor.StateChanged for auto-refresh and supports
/// collapse toggle + I18n language switching.
/// </summary>
public partial class LockedDashboard : Control
{
    private CrystalSpherePredictor _predictor = null!;
    private VBoxContainer _root = null!;
    private VBoxContainer _rowsContainer = null!;
    private VBoxContainer _content = null!;
    private Label _potionLabel = null!;
    private Label _goldLabel = null!;
    private bool _collapsed;

    // Panel palette (matches InfoPanel)
    private static readonly Color DeepSpaceBg = new(0.043f, 0.055f, 0.102f, 0.92f);
    private static readonly Color PanelBorder = new(0.118f, 0.141f, 0.200f, 1f);
    private static readonly Color StarWhite = new(0.784f, 0.816f, 0.878f);
    private static readonly Color Gold = new(0.722f, 0.588f, 0.290f);
    private static readonly Color IceBlue = new(0.30f, 0.65f, 1f);

    // Column dot / accent colors
    private static readonly Color RareColor = new(1f, 0.42f, 0.21f);
    private static readonly Color UncommonColor = new(0.30f, 0.65f, 1f);
    private static readonly Color CommonColor = new(0.784f, 0.816f, 0.878f);
    private static readonly Color RelicColor = new(0.29f, 0.87f, 0.50f);

    // Column definitions: type, i18n key, accent color
    private static readonly (ColumnType type, string i18nKey, Color color)[] Columns =
    {
        (ColumnType.Rare, "locked_rare", RareColor),
        (ColumnType.Uncommon, "locked_uncommon", UncommonColor),
        (ColumnType.Common, "locked_common", CommonColor),
        (ColumnType.Relic, "locked_relic", RelicColor),
    };

    // I18n registry for persistent labels updated on language switch
    private readonly List<(Control control, string key)> _i18nRegistry = new();

    // =============== Public API ===============

    /// <summary>
    /// Create and attach the LockedDashboard to a parent Control.
    /// Wires up predictor events and I18n language switching.
    /// </summary>
    public static LockedDashboard Create(Control parent)
    {
        var dashboard = new LockedDashboard();
        dashboard.Build();
        parent.AddChild(dashboard);

        var predictor = CrystalSpherePredictor.Instance!;
        dashboard._predictor = predictor;
        predictor.StateChanged += dashboard.Refresh;
        I18n.LanguageChanged += dashboard.OnLanguageChanged;

        dashboard.Refresh();
        return dashboard;
    }

    // =============== Build ===============

    private void Build()
    {
        Name = "PredictEverythingLockedDashboard";
        MouseFilter = MouseFilterEnum.Stop;

        // Panel background
        var bg = new Panel();
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat
        {
            BgColor = DeepSpaceBg,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderColor = PanelBorder,
        };
        bgStyle.SetCornerRadiusAll(12);
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(bg);

        // Top accent line (ice-blue to differentiate from InfoPanel)
        var accentLine = new ColorRect();
        accentLine.SetAnchorsPreset(LayoutPreset.TopWide);
        accentLine.Color = IceBlue;
        accentLine.CustomMinimumSize = new Vector2(0, 2);
        accentLine.OffsetLeft = 8;
        accentLine.OffsetRight = -8;
        accentLine.OffsetTop = 0;
        AddChild(accentLine);

        // Root vertical container
        _root = new VBoxContainer();
        _root.SetAnchorsPreset(LayoutPreset.FullRect);
        _root.AddThemeConstantOverride("separation", 4);
        _root.OffsetLeft = 10;
        _root.OffsetRight = -10;
        _root.OffsetTop = 6;
        _root.OffsetBottom = -8;
        AddChild(_root);

        // ---- Title bar ----
        var titleBar = new HBoxContainer();
        titleBar.MouseFilter = MouseFilterEnum.Stop;
        titleBar.AddThemeConstantOverride("separation", 6);

        var title = CreateLocalizedLabel("locked_title", 13, StarWhite);
        title.AddThemeFontSizeOverride("font_size", 20);
        titleBar.AddChild(title);

        titleBar.AddChild(new Control
        {
            CustomMinimumSize = new Vector2(10, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });

        var collapseBtn = CreateIconButton("✕", 14);
        collapseBtn.Pressed += ToggleCollapse;
        titleBar.AddChild(collapseBtn);

        _root.AddChild(titleBar);

        // ---- Collapsible content ----
        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 4);
        _content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _root.AddChild(_content);

        // Column rows container (rebuilt on Refresh)
        _rowsContainer = new VBoxContainer();
        _rowsContainer.AddThemeConstantOverride("separation", 3);
        _rowsContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _content.AddChild(_rowsContainer);

        // Separator before potion / gold summary
        _content.AddChild(new HSeparator());

        // Potion row
        _potionLabel = CreateLabel("", 16, StarWhite);
        _content.AddChild(_potionLabel);

        // Gold row
        _goldLabel = CreateLabel("", 16, Gold);
        _content.AddChild(_goldLabel);

        // ---- Size and positioning (right side) ----
        float w = 320f;
        float h = 380f;
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -w - 20;
        OffsetRight = -20;
        OffsetTop = 200;
        OffsetBottom = 100 + h;
        CustomMinimumSize = new Vector2(w, h);

        // Initial data population
        Refresh();
    }

    // =============== Refresh ===============

    /// <summary>
    /// Rebuild all column rows from predictor state.
    /// Called on StateChanged event.
    /// </summary>
    public void Refresh()
    {
        if (_rowsContainer == null) return;
        if (_predictor == null || !_predictor.IsActive) return;

        // Clear existing column rows (separator + potion + gold remain in _content)
        while (_rowsContainer.GetChildCount() > 0)
        {
            var child = _rowsContainer.GetChild(0);
            _rowsContainer.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var (type, i18nKey, color) in Columns)
        {
            var state = GetColumnState(type);
            bool isLocked = state.IsLocked;
            string colName = I18n.Tr(i18nKey);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            row.MouseFilter = MouseFilterEnum.Pass;

            // Dot indicator: filled when locked, hollow when not
            string dot = isLocked ? "●" : "○";
            float dotAlpha = isLocked ? 1f : 0.3f;
            var dotLabel = CreateLabel(dot, 12, new Color(color.R, color.G, color.B, dotAlpha));
            dotLabel.CustomMinimumSize = new Vector2(16, 0);
            row.AddChild(dotLabel);

            // Column name
            var nameLabel = CreateLabel(colName, 15,
                new Color(StarWhite.R, StarWhite.G, StarWhite.B, isLocked ? 1f : 0.35f));
            nameLabel.CustomMinimumSize = new Vector2(50, 0);
            row.AddChild(nameLabel);

            // Reward name or "(not yet)" placeholder
            string rewardText;
            Color rewardColor;
            if (isLocked)
            {
                int offset = state.LockedAt;
                var pred = _predictor.Predictions[offset];
                rewardText = type switch
                {
                    ColumnType.Relic => pred.Relic?.Name ?? "?",
                    _ => string.Join(" / ", pred.GetCards(type).Select(c => c.Upgraded ? c.Name + "+" : c.Name)),
                };
                rewardColor = StarWhite;
            }
            else
            {
                rewardText = I18n.Tr("locked_not_yet");
                rewardColor = new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.3f);
            }

            var rewardLabel = CreateLabel(rewardText, 15, rewardColor);
            rewardLabel.AutowrapMode = TextServer.AutowrapMode.Word;
            rewardLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            rewardLabel.ClipContents = true;
            row.AddChild(rewardLabel);

            _rowsContainer.AddChild(row);
        }

        // Refresh potion summary
        int revealed = _predictor.RevealedPotionCount;
        int total = _predictor.TotalPotionCount;
        _potionLabel.Text = $"● {I18n.Tr("locked_potion")}: {revealed} / {total}";

        // Refresh gold summary
        int goldLeft = Math.Max(0, 7 - _predictor.CurrentOffset);
        _goldLabel.Text = $"{I18n.Tr("gold_left")}: {goldLeft}";
    }

    private ColumnState GetColumnState(ColumnType col) => col switch
    {
        ColumnType.Rare => _predictor.Rare,
        ColumnType.Uncommon => _predictor.Uncommon,
        ColumnType.Common => _predictor.Common,
        ColumnType.Relic => _predictor.Relic,
        _ => throw new ArgumentOutOfRangeException(nameof(col)),
    };

    // =============== Collapse ===============

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;

        if (_content != null)
            _content.Visible = !_collapsed;

        if (_collapsed)
        {
            OffsetBottom = OffsetTop + 36f;
            CustomMinimumSize = new Vector2(CustomMinimumSize.X, 0);
        }
        else
        {
            float h = 260f;
            OffsetBottom = OffsetTop + h;
            CustomMinimumSize = new Vector2(CustomMinimumSize.X, h);
        }
    }

    // =============== I18n ===============

    private void OnLanguageChanged()
    {
        foreach (var (control, key) in _i18nRegistry)
        {
            if (control is Label label)
                label.Text = I18n.Tr(key);
            else if (control is Button button)
                button.Text = I18n.Tr(key);
        }
        Refresh();
    }

    // =============== UI Helpers ===============

    private static Label CreateLabel(string text, int fontSize, Color color)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private Label CreateLocalizedLabel(string key, int fontSize, Color color)
    {
        var label = CreateLabel(I18n.Tr(key), fontSize, color);
        _i18nRegistry.Add((label, key));
        return label;
    }

    private static Button CreateIconButton(string text, int fontSize)
    {
        var btn = new Button { Text = text };
        btn.CustomMinimumSize = new Vector2(22, 22);
        btn.AddThemeFontSizeOverride("font_size", fontSize);
        var flatStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
        btn.AddThemeStyleboxOverride("normal", flatStyle);
        btn.AddThemeStyleboxOverride("hover", flatStyle);
        btn.AddThemeStyleboxOverride("pressed", flatStyle);
        btn.AddThemeStyleboxOverride("focus", flatStyle);
        return btn;
    }

    // =============== Cleanup ===============

    public override void _ExitTree()
    {
        I18n.LanguageChanged -= OnLanguageChanged;
        if (_predictor != null)
            _predictor.StateChanged -= Refresh;
        base._ExitTree();
    }
}
