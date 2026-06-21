using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace PredictEverything;

/// <summary>
/// Floating detail popup shown when right-clicking a card or relic name
/// in the InfoPanel. Displays the item's name, upgraded status (for cards),
/// icon + rarity (for relics), and full formatted description text.
/// Features draggable panel, close button, and backdrop-click dismissal.
/// </summary>
public partial class PreviewPopup : Control
{
    private Panel _panel = null!;
    private VBoxContainer _content = null!;
    private bool _dragging;
    private Vector2 _dragStart;

    // Panel palette (matches InfoPanel / LockedDashboard)
    private static readonly Color DeepSpaceBg = Colors.BgPrimary;
    private static readonly Color PanelBorder = Colors.BorderPrimary;
    private static readonly Color StarWhite = Colors.TextPrimary;
    private static readonly Color LimeGreen = Colors.RelicAccent;
    private static readonly Color WarmOrange = Colors.RareAccent;
    private static readonly Color Gold = Colors.PlannedColor;
    private static readonly Color IceBlue = Colors.UncommonAccent;

    private const float PanelW = 380f;
    private const float PanelMinH = 180f;

    // =============== Public static API ===============

    /// <summary>
    /// Show a card detail popup. Attaches to the given parent (typically the
    /// screen-level Control) so it overlays the entire UI.
    /// </summary>
    public static PreviewPopup ShowCard(CardModel card, bool upgraded, Control parent)
    {
        var popup = new PreviewPopup();
        popup.BuildBase(parent);
        popup.FillCard(card, upgraded);
        return popup;
    }

    /// <summary>
    /// Show a relic detail popup. Attaches to the given parent (typically the
    /// screen-level Control) so it overlays the entire UI.
    /// </summary>
    public static PreviewPopup ShowRelic(RelicModel relic, string rarityLabel, Texture2D? icon, Control parent)
    {
        var popup = new PreviewPopup();
        popup.BuildBase(parent);
        popup.FillRelic(relic, rarityLabel, icon);
        return popup;
    }

    // =============== Build ===============

    private void BuildBase(Control parent)
    {
        Name = "PredictEverythingPreviewPopup";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 200;

        // Dark backdrop — click anywhere on backdrop closes popup
        var backdrop = new ColorRect();
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.Color = new Color(0, 0, 0, 0.45f);
        backdrop.MouseFilter = MouseFilterEnum.Stop;
        backdrop.GuiInput += (InputEvent e) =>
        {
            if (e is InputEventMouseButton mb &&
                mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                QueueFree();
            }
        };
        AddChild(backdrop);

        // Centered draggable panel
        _panel = new Panel();
        _panel.MouseFilter = MouseFilterEnum.Stop;
        var viewRect = GetViewport().GetVisibleRect().Size;
        _panel.Position = new Vector2(
            (viewRect.X - PanelW) / 2f,
            (viewRect.Y - PanelMinH) / 2f);
        _panel.CustomMinimumSize = new Vector2(PanelW, PanelMinH);

        var panelStyle = new StyleBoxFlat
        {
            BgColor = DeepSpaceBg,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderColor = PanelBorder,
        };
        panelStyle.SetCornerRadiusAll(12);
        _panel.AddThemeStyleboxOverride("panel", panelStyle);

        // Drag handling: start drag on left-press over panel
        _panel.GuiInput += (InputEvent e) =>
        {
            if (e is InputEventMouseButton mb &&
                mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                _dragging = true;
                _dragStart = GetGlobalMousePosition() - _panel.GlobalPosition;
            }
        };
        AddChild(_panel);

        // Content vertical container filling the panel
        _content = new VBoxContainer();
        _content.SetAnchorsPreset(LayoutPreset.FullRect);
        _content.AddThemeConstantOverride("separation", 4);
        _content.OffsetLeft = 12;
        _content.OffsetRight = -12;
        _content.OffsetTop = 8;
        _content.OffsetBottom = -8;
        _panel.AddChild(_content);

        parent.AddChild(this);
    }

    private void FillCard(CardModel card, bool upgraded)
    {
        // Title bar
        _content.AddChild(BuildTitleBar(I18n.Tr("preview_card_title")));
        _content.AddChild(new HSeparator());

        // Card name + upgrade badge
        string displayName = card.Title;
        if (upgraded && !displayName.EndsWith("+"))
            displayName += "+";

        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 6);

        var nameLabel = CreateLabel(displayName, 14, upgraded ? LimeGreen : StarWhite, bold: true);
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nameLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        nameRow.AddChild(nameLabel);

        // Rarity badge
        var rarityBadge = CreateLabel(GetRarityString(card), 10, Gold);
        nameRow.AddChild(rarityBadge);

        _content.AddChild(nameRow);

        // Description (BBCode enabled for card keyword formatting)
        var descLabel = new RichTextLabel();
        descLabel.BbcodeEnabled = true;
        descLabel.FitContent = true;
        descLabel.ScrollFollowing = false;
        descLabel.ScrollActive = false;
        descLabel.MouseFilter = MouseFilterEnum.Pass;
        descLabel.AddThemeFontSizeOverride("normal_font_size", 11);
        descLabel.AddThemeColorOverride("default_color", StarWhite);
        descLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
        descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        try { descLabel.Text = card.Description.GetFormattedText(); }
        catch { descLabel.Text = "(no description)"; }
        _content.AddChild(descLabel);

        // Deferred size-to-content
        CallDeferred(nameof(DeferredSize));
    }

    private void FillRelic(RelicModel relic, string rarityLabel, Texture2D? icon)
    {
        // Title bar
        _content.AddChild(BuildTitleBar(I18n.Tr("preview_relic_title")));
        _content.AddChild(new HSeparator());

        // Icon + name + rarity row
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 8);

        if (icon != null)
        {
            var iconRect = new TextureRect();
            iconRect.Texture = icon;
            iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            iconRect.CustomMinimumSize = new Vector2(48, 48);
            iconRect.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            headerRow.AddChild(iconRect);
        }

        var nameVBox = new VBoxContainer();
        nameVBox.AddThemeConstantOverride("separation", 2);
        nameVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var nameLabel = CreateLabel(relic.Title.GetFormattedText(), 14, StarWhite, bold: true);
        nameLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        nameVBox.AddChild(nameLabel);

        if (!string.IsNullOrEmpty(rarityLabel))
        {
            var rarityBadge = CreateLabel(rarityLabel, 10, Gold);
            nameVBox.AddChild(rarityBadge);
        }

        headerRow.AddChild(nameVBox);
        _content.AddChild(headerRow);

        // Description
        var descLabel = new RichTextLabel();
        descLabel.BbcodeEnabled = true;
        descLabel.FitContent = true;
        descLabel.ScrollFollowing = false;
        descLabel.ScrollActive = false;
        descLabel.MouseFilter = MouseFilterEnum.Pass;
        descLabel.AddThemeFontSizeOverride("normal_font_size", 11);
        descLabel.AddThemeColorOverride("default_color", StarWhite);
        descLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
        descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        try { descLabel.Text = relic.DynamicDescription.GetFormattedText(); }
        catch { descLabel.Text = "(no description)"; }
        _content.AddChild(descLabel);

        CallDeferred(nameof(DeferredSize));
    }

    // =============== Size helpers ===============

    private void DeferredSize()
    {
        // Sum visible content children height with spacing
        float contentH = 0f;
        int visibleCount = 0;
        foreach (var child in _content.GetChildren())
        {
            if (child is Control c && c.Visible)
            {
                contentH += c.Size.Y;
                visibleCount++;
            }
        }
        if (visibleCount > 0)
            contentH += (visibleCount - 1) * 4f; // separation

        float h = Mathf.Clamp(contentH + 24f, PanelMinH, 500f);
        _panel.Size = new Vector2(PanelW, h);
    }

    // =============== Title bar ===============

    private HBoxContainer BuildTitleBar(string titleText)
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 6);

        var title = CreateLabel(titleText, 13, WarmOrange, bold: true);
        title.AddThemeFontSizeOverride("font_size", 13);
        bar.AddChild(title);

        bar.AddChild(new Control
        {
            CustomMinimumSize = new Vector2(10, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });

        var closeBtn = CreateIconButton("✕", 16);
        closeBtn.Pressed += () => QueueFree();
        bar.AddChild(closeBtn);

        return bar;
    }

    // =============== Drag handling ===============

    public override void _Process(double delta)
    {
        if (!_dragging) return;

        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _panel.Position = GetGlobalMousePosition() - _dragStart;
        }
        else
        {
            _dragging = false;
        }
    }

    // =============== UI helpers ===============

    private static Label CreateLabel(string text, int fontSize, Color color, bool bold = false)
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

    /// <summary>Render card rarity as a concise label string.</summary>
    private static string GetRarityString(CardModel card)
    {
        try
        {
            // CardModel.Rarity is CardRarity enum; use ToString for display
            var r = card.Rarity;
            return r.ToString();
        }
        catch
        {
            return "?";
        }
    }
}
