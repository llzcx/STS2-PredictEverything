using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Potions;

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
    private Label _goldLabel = null!;
    private Button _collapseBtn = null!;
    private Label _progressLabel = null!;
    private Label _inventoryTitle = null!;
    private VBoxContainer _inventoryContainer = null!;
    private HBoxContainer _titleBar = null!;
    private Label _titleLabel = null!;
    private DragHandler _dragHandler = null!;
    private bool _collapsed;
    private float _fullHeight;

    // Panel palette (matches InfoPanel via shared tokens)
    private static readonly Color DeepSpaceBg = Colors.BgPrimary;
    private static readonly Color PanelBorder = Colors.BorderPrimary;
    private static readonly Color StarWhite = Colors.TextPrimary;
    private static readonly Color Gold = Colors.PlannedColor;
    private static readonly Color IceBlue = Colors.UncommonAccent;
    private static readonly Color LimeGreen = Colors.RelicAccent;

    // Column dot / accent colors (from shared tokens)
    private static readonly Color RareColor = Colors.RareAccent;
    private static readonly Color UncommonColor = Colors.UncommonAccent;
    private static readonly Color CommonColor = Colors.CommonAccent;
    private static readonly Color RelicColor = Colors.RelicAccent;
    private static readonly Color PotionColor = Colors.PotionAccent;

    // Column definitions: type, i18n key, accent color
    private static readonly (ColumnType type, string i18nKey, Color color)[] Columns =
    {
        (ColumnType.Rare, "locked_rare", RareColor),
        (ColumnType.Uncommon, "locked_uncommon", UncommonColor),
        (ColumnType.Common, "locked_common", CommonColor),
        (ColumnType.Relic, "locked_relic", RelicColor),
        (ColumnType.CommonPotion, "locked_common_potion_col", PotionColor),
        (ColumnType.RarePotion, "locked_rare_potion_col", PotionColor),
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
        dashboard._dragHandler.Start();
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
        accentLine.Color = Colors.LockedColor;
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
        _titleBar = new HBoxContainer();
        _titleBar.MouseFilter = MouseFilterEnum.Stop;
        _titleBar.AddThemeConstantOverride("separation", 6);

        _titleLabel = CreateLocalizedLabel("locked_title", 13, StarWhite);
        _titleLabel.AddThemeFontSizeOverride("font_size", 20);
        _titleBar.AddChild(_titleLabel);

        _titleBar.AddChild(new Control
        {
            CustomMinimumSize = new Vector2(10, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });

        var collapseBtn = CreateIconButton("▼", 14);
        collapseBtn.Pressed += ToggleCollapse;
        _collapseBtn = collapseBtn;
        _titleBar.AddChild(collapseBtn);

        _root.AddChild(_titleBar);

        // ---- Progress summary (always visible) ----
        _progressLabel = CreateLabel("", 12, Colors.TextSecondary);
        _progressLabel.AddThemeFontSizeOverride("font_size", 12);
        _root.AddChild(_progressLabel);

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

        // Gold row — hidden, replaced by inventory
        _goldLabel = CreateLabel("", 16, Gold);
        _goldLabel.Visible = false;
        _content.AddChild(_goldLabel);

        // Grid inventory section (shows below column rows)
        _content.AddChild(new HSeparator());
        _inventoryTitle = CreateLabel(I18n.Tr("inventory_title"), 12, Colors.TextSecondary);
        _inventoryTitle.AddThemeFontSizeOverride("font_size", 14);
        _content.AddChild(_inventoryTitle);

        // Inventory header row
        var invHeader = new HBoxContainer();
        invHeader.AddThemeConstantOverride("separation", 6);
        var nameHeaderLabel = CreateLabel(I18n.Tr("inventory_col_item"), 10, Colors.TextDim);
        nameHeaderLabel.CustomMinimumSize = new Vector2(80, 0);
        invHeader.AddChild(nameHeaderLabel);
        var hRemain = CreateLabel("剩余", 10, Colors.TextDim);
        hRemain.CustomMinimumSize = new Vector2(36, 0);
        hRemain.HorizontalAlignment = HorizontalAlignment.Center;
        invHeader.AddChild(hRemain);
        var hSize = CreateLabel("格子", 10, Colors.TextDim);
        hSize.CustomMinimumSize = new Vector2(32, 0);
        hSize.HorizontalAlignment = HorizontalAlignment.Center;
        invHeader.AddChild(hSize);
        var hOff = CreateLabel("偏移", 10, Colors.TextDim);
        hOff.CustomMinimumSize = new Vector2(28, 0);
        hOff.HorizontalAlignment = HorizontalAlignment.Center;
        invHeader.AddChild(hOff);
        _content.AddChild(invHeader);

        _inventoryContainer = new VBoxContainer();
        _inventoryContainer.AddThemeConstantOverride("separation", 2);
        _inventoryContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _content.AddChild(_inventoryContainer);

        // ---- Size and positioning (right side) ----
        float w = 320f;
        float h = 550f;
        var config = PredictEverythingConfig.Instance;
        if (config.DashboardX >= 0 && config.DashboardY >= 0)
        {
            // Manual position from drag
            AnchorLeft = 0; AnchorTop = 0; AnchorRight = 0; AnchorBottom = 0;
            OffsetLeft = config.DashboardX; OffsetTop = config.DashboardY;
            OffsetRight = config.DashboardX + w; OffsetBottom = config.DashboardY + h;
        }
        else
        {
            SetAnchorsPreset(LayoutPreset.TopRight);
            OffsetLeft = -w - 20;
            OffsetRight = -20;
            OffsetTop = 200;
            OffsetBottom = 100 + h;
        }
        CustomMinimumSize = new Vector2(w, h);
        _fullHeight = OffsetBottom - OffsetTop;

        // Drag support
        _dragHandler = new DragHandler(this, _titleBar,
            onDragStart: () => _titleLabel.AddThemeColorOverride("font_color", Gold),
            onDragEnd: () =>
            {
                _titleLabel.AddThemeColorOverride("font_color", StarWhite);
                var gpos = GlobalPosition;
                float pw = OffsetRight - OffsetLeft;
                float ph = OffsetBottom - OffsetTop;
                AnchorLeft = 0; AnchorTop = 0; AnchorRight = 0; AnchorBottom = 0;
                OffsetLeft = gpos.X; OffsetTop = gpos.Y;
                OffsetRight = gpos.X + pw; OffsetBottom = gpos.Y + ph;
                config.DashboardX = gpos.X; config.DashboardY = gpos.Y;
                PredictEverythingConfig.Save();
            });

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
            bool isPotionCol = type == ColumnType.CommonPotion || type == ColumnType.RarePotion;
            bool effectiveLocked = isPotionCol ? _predictor.IsColumnLocked(type) : state.IsLocked;
            string colName = I18n.Tr(i18nKey);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            row.MouseFilter = MouseFilterEnum.Pass;

            // Dot indicator: filled when locked, hollow when not
            string dot = effectiveLocked ? "●" : "○";
            float dotAlpha = effectiveLocked ? 1f : 0.3f;
            var dotLabel = CreateLabel(dot, 12, new Color(color.R, color.G, color.B, dotAlpha));
            dotLabel.CustomMinimumSize = new Vector2(16, 0);
            row.AddChild(dotLabel);

            // Column name
            var nameLabel = CreateLabel(colName, 15,
                new Color(StarWhite.R, StarWhite.G, StarWhite.B, effectiveLocked ? 1f : 0.35f));
            nameLabel.CustomMinimumSize = new Vector2(50, 0);
            row.AddChild(nameLabel);

            // Reward name
            if (isPotionCol)
            {
                PotionRarity filterRarity = type == ColumnType.RarePotion ? PotionRarity.Rare : PotionRarity.Common;
                var names = _predictor.GetPotionRevealedNames(filterRarity);
                var pc = new Color(StarWhite.R, StarWhite.G, StarWhite.B, names.Count > 0 ? 0.85f : 0.3f);
                var lbl = CreateLabel(names.Count > 0 ? string.Join(", ", names) : I18n.Tr("locked_not_yet"), 15, pc);
                lbl.AutowrapMode = TextServer.AutowrapMode.Word;
                lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                lbl.ClipContents = true;
                row.AddChild(lbl);
            }
            else if (effectiveLocked)
            {
                int offset = state.LockedAt;
                var pred = _predictor.Predictions[offset];
                if (type == ColumnType.Relic)
                {
                    var lbl = CreateLabel(pred.Relic?.Name ?? "?", 15, LimeGreen);
                    lbl.AutowrapMode = TextServer.AutowrapMode.Word;
                    lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    lbl.ClipContents = true;
                    row.AddChild(lbl);
                }
                else
                {
                    var cards = pred.GetCards(type);
                    var parts = cards.Select(c =>
                    {
                        string name = c.Name ?? "?";
                        if (c.Upgraded && !name.EndsWith("+")) name += "+";
                        return c.Upgraded
                            ? $"[b][color=#A0D636]{name}[/color][/b]"
                            : $"[color=#CCC8B7]{name}[/color]";
                    });
                    var rtl = new RichTextLabel();
                    rtl.BbcodeEnabled = true;
                    rtl.Text = string.Join("  /  ", parts);
                    rtl.FitContent = true;
                    rtl.ScrollActive = false;
                    rtl.AutowrapMode = TextServer.AutowrapMode.Word;
                    rtl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    rtl.ClipContents = true;
                    rtl.AddThemeFontSizeOverride("normal_font_size", 15);
                    rtl.AddThemeFontSizeOverride("bold_font_size", 15);
                    row.AddChild(rtl);
                }
            }
            else
            {
                var lbl = CreateLabel(I18n.Tr("locked_not_yet"), 15,
                    new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.3f));
                lbl.AutowrapMode = TextServer.AutowrapMode.Word;
                lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                lbl.ClipContents = true;
                row.AddChild(lbl);
            }

            _rowsContainer.AddChild(row);
        }

        // Refresh progress summary (potion columns never count toward "locked" since infinite supply)
        int lockedCols = 0;
        foreach (var (type, _, _) in Columns)
        {
            if (type == ColumnType.CommonPotion || type == ColumnType.RarePotion) continue;
            if (GetColumnState(type).IsLocked) lockedCols++;
        }
        int revealedPotions = 0;
        for (int i = 0; i < _predictor.TotalPotionCount; i++)
            if (_predictor.IsPotionRevealed(i)) revealedPotions++;
        _progressLabel.Text = $"{I18n.Tr("progress_columns")}: {lockedCols}/4  |  {I18n.Tr("progress_potions")}: {revealedPotions}/{_predictor.TotalPotionCount}";

        // Refresh grid inventory
        RefreshInventory();
    }

    private void RefreshInventory()
    {
        if (_inventoryContainer == null || _predictor == null) return;
        while (_inventoryContainer.GetChildCount() > 0)
        {
            var child = _inventoryContainer.GetChild(0);
            _inventoryContainer.RemoveChild(child);
            child.QueueFree();
        }

        var inv = _predictor.GetGridInventory();
        foreach (var entry in inv)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);

            // Name with dot indicator (filled = unrevealed items remain)
            bool hasRemaining = entry.Remaining > 0;
            string dot = hasRemaining ? "●" : "○";
            float alpha = hasRemaining ? 1f : 0.25f;
            var labelColor = new Color(entry.LabelColor.R, entry.LabelColor.G, entry.LabelColor.B, alpha);
            var dotLabel = CreateLabel($"{dot} {entry.Label}", 13, labelColor);
            dotLabel.CustomMinimumSize = new Vector2(80, 0);
            row.AddChild(dotLabel);

            // Remaining / Total
            var remainColor = entry.Remaining > 0
                ? new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.9f)
                : new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.25f);
            var remainLabel = CreateLabel($"{entry.Remaining}/{entry.Total}", 12, remainColor);
            remainLabel.HorizontalAlignment = HorizontalAlignment.Center;
            remainLabel.CustomMinimumSize = new Vector2(36, 0);
            row.AddChild(remainLabel);

            // Cell size
            var sizeLabel = CreateLabel(entry.CellSize, 12,
                new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.55f));
            sizeLabel.HorizontalAlignment = HorizontalAlignment.Center;
            sizeLabel.CustomMinimumSize = new Vector2(32, 0);
            row.AddChild(sizeLabel);

            // RNG benefit
            Color benefitColor = entry.RngBenefit >= 6 ? Colors.RelicAccent
                : entry.RngBenefit > 0 ? Colors.UncommonAccent
                : Colors.CurseColor;
            var benefitLabel = CreateLabel(entry.BenefitLabel, 12, benefitColor);
            benefitLabel.HorizontalAlignment = HorizontalAlignment.Center;
            benefitLabel.CustomMinimumSize = new Vector2(28, 0);
            row.AddChild(benefitLabel);

            _inventoryContainer.AddChild(row);
        }

        // Summary rows: Σ (remaining × RngBenefit), by benefit group
        int totalBenefit = inv.Sum(e => e.Remaining * e.RngBenefit);
        int benefit1 = inv.Where(e => e.RngBenefit == 1).Sum(e => e.Remaining * e.RngBenefit);
        int benefit6 = inv.Where(e => e.RngBenefit == 6).Sum(e => e.Remaining * e.RngBenefit);
        _inventoryContainer.AddChild(new HSeparator());

        void AddSumRow(string labelKey, int value, Color valueColor)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            var lbl = CreateLabel(I18n.Tr(labelKey), 12,
                new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.7f));
            lbl.CustomMinimumSize = new Vector2(130, 0);
            row.AddChild(lbl);
            row.AddChild(new Control { CustomMinimumSize = new Vector2(36, 0) });
            row.AddChild(new Control { CustomMinimumSize = new Vector2(32, 0) });
            var val = CreateLabel($"+{value}", 13, valueColor);
            val.HorizontalAlignment = HorizontalAlignment.Center;
            val.CustomMinimumSize = new Vector2(28, 0);
            row.AddChild(val);
            _inventoryContainer.AddChild(row);
        }

        AddSumRow("inventory_total_benefit", totalBenefit, Colors.RelicAccent);
        AddSumRow("inventory_benefit_1", benefit1, IceBlue);
        AddSumRow("inventory_benefit_6", benefit6, LimeGreen);
    }

    private ColumnState GetColumnState(ColumnType col) => col switch
    {
        ColumnType.Rare => _predictor.Rare,
        ColumnType.Uncommon => _predictor.Uncommon,
        ColumnType.Common => _predictor.Common,
        ColumnType.Relic => _predictor.Relic,
        ColumnType.CommonPotion => _predictor.CommonPotionColumn,
        ColumnType.RarePotion => _predictor.RarePotionColumn,
        _ => throw new ArgumentOutOfRangeException(nameof(col)),
    };

    // =============== Collapse ===============

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;

        if (_collapseBtn != null)
            _collapseBtn.Text = _collapsed ? "▲" : "▼";

        if (_collapsed)
            _fullHeight = Size.Y;

        if (_content != null)
            _content.Visible = !_collapsed;
        if (_progressLabel != null)
            _progressLabel.Visible = !_collapsed;

        if (_collapsed)
        {
            OffsetBottom = OffsetTop + 36f;
            CustomMinimumSize = new Vector2(CustomMinimumSize.X, 0);
        }
        else
        {
            OffsetBottom = OffsetTop + _fullHeight;
            CustomMinimumSize = new Vector2(CustomMinimumSize.X, _fullHeight);
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
