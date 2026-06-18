using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;

namespace PredictEverything;

/// <summary>
/// Fixed preview panel below the InfoPanel. Shows card names/costs/descriptions
/// or relic icon+flavor. Uses only simple Godot controls — no game rendering nodes
/// that require combat/run context.
/// </summary>
public static class HoverTooltip
{
    private static PanelContainer? _panel;
    private static HBoxContainer? _content;
    private static Control? _parent;
    private static InfoPanel? _infoPanel;

    public static void Init(Control screen, InfoPanel infoPanel)
    {
        _parent = screen;
        _infoPanel = infoPanel;
        BuildPanel();
    }

    private static void BuildPanel()
    {
        _panel = new PanelContainer();
        _panel.Visible = false;
        _panel.ZIndex = 190;
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.04f, 0.05f, 0.10f, 0.94f);
        bg.SetCornerRadiusAll(8);
        bg.BorderWidthLeft = 1;
        bg.BorderWidthRight = 1;
        bg.BorderWidthTop = 1;
        bg.BorderWidthBottom = 1;
        bg.BorderColor = new Color(0.722f, 0.588f, 0.290f, 0.4f);
        bg.ContentMarginLeft = 12;
        bg.ContentMarginRight = 12;
        bg.ContentMarginTop = 8;
        bg.ContentMarginBottom = 8;
        _panel.AddThemeStyleboxOverride("panel", bg);

        _content = new HBoxContainer();
        _content.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(_content);

        _parent!.AddChild(_panel);
    }

    public static void ShowCards(CardPrediction[] cards)
    {
        ClearContent();
        if (_panel == null || _content == null || _infoPanel == null || cards == null) return;

        foreach (var card in cards.Where(c => c.Card != null))
        {
            var label = new RichTextLabel();
            label.BbcodeEnabled = true;
            var name = card.Name ?? "?";
            if (card.Upgraded && !name.EndsWith("+")) name += "+";
            var color = card.Upgraded ? "#66FF66" : "#C8D0E0";
            label.Text = $"[b][color={color}]{name}[/color][/b]";
            label.FitContent = true;
            label.ScrollActive = false;
            label.AddThemeFontSizeOverride("normal_font_size", 14);
            _content.AddChild(label);
        }

        _panel.CustomMinimumSize = new Vector2(350, 0);
        PositionAndShow();
    }

    public static void ShowRelic(RelicPrediction? prediction)
    {
        ClearContent();
        if (_panel == null || _content == null || _infoPanel == null || prediction?.Relic == null) return;

        _content.AddChild(BuildRelicSlot(prediction));
        _panel.CustomMinimumSize = new Vector2(280, 0);
        PositionAndShow();
    }

    public static void Hide()
    {
        if (_panel != null) _panel.Visible = false;
    }

    // ---- Relic slot ----

    private static Control BuildRelicSlot(RelicPrediction prediction)
    {
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);

        // Icon
        var iconRect = new TextureRect();
        iconRect.Texture = prediction.Icon ?? prediction.Relic.BigIcon;
        iconRect.ExpandMode = TextureRect.ExpandModeEnum.KeepSize;
        iconRect.StretchMode = TextureRect.StretchModeEnum.KeepCentered;
        iconRect.CustomMinimumSize = new Vector2(80, 80);
        iconRect.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        vbox.AddChild(iconRect);

        // Name
        var nameLabel = new RichTextLabel();
        nameLabel.BbcodeEnabled = true;
        nameLabel.Text = $"[b]{prediction.Name}[/b] ([color=#B8902F]{prediction.RarityLabel}[/color])";
        nameLabel.FitContent = true;
        nameLabel.ScrollActive = false;
        nameLabel.AddThemeFontSizeOverride("normal_font_size", 15);
        vbox.AddChild(nameLabel);

        // Flavor
        var flavor = prediction.Relic.Flavor;
        var flavorText = flavor.GetFormattedText();
        if (!string.IsNullOrEmpty(flavorText))
        {
            var desc = new RichTextLabel();
            desc.BbcodeEnabled = true;
            desc.Text = flavorText;
            desc.FitContent = true;
            desc.ScrollActive = false;
            desc.CustomMinimumSize = new Vector2(240, 0);
            desc.AddThemeFontSizeOverride("normal_font_size", 12);
            vbox.AddChild(desc);
        }

        return vbox;
    }

    // ----

    private static void ClearContent()
    {
        if (_content == null) return;
        while (_content.GetChildCount() > 0)
            _content.GetChild(0).QueueFree();
    }

    private static void PositionAndShow()
    {
        if (_panel == null || _infoPanel == null) return;
        var infoPos = _infoPanel.GlobalPosition;
        var infoSize = _infoPanel.Size;
        _panel.Position = new Vector2(infoPos.X, infoPos.Y + infoSize.Y + 6);
        _panel.Visible = true;
    }
}
