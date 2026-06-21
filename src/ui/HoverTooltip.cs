using System;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace PredictEverything;

/// <summary>
/// Mouse-following tooltip for card/relic preview on hover.
/// </summary>
public static class HoverTooltip
{
    private static PanelContainer? _panel;
    private static Control? _parent;

    private static readonly FieldInfo? _autoSizeField = typeof(MegaRichTextLabel)
        .GetField("_isAutoSizeEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly Color CardBg = Colors.BgSecondary;
    private static readonly Color GoldBorder = new(Colors.TextPrimary.R, Colors.TextPrimary.G, Colors.TextPrimary.B, 0.20f);
    private static readonly Color Green = Colors.UpgradedColor;
    private const int PanelWidth = 420;

    public static void Init(Control screen) { _parent = screen; }

    public static void ShowCard(CardPrediction prediction)
    {
        if (_parent == null || prediction.Card == null) return;
        var card = prediction.Card;

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 5);

        // Header: cost orb + name
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);

        var costBox = new Label();
        costBox.Text = card.EnergyCost?.Canonical.ToString() ?? "?";
        costBox.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        costBox.AddThemeFontSizeOverride("font_size", 20);
        costBox.HorizontalAlignment = HorizontalAlignment.Center;
        costBox.CustomMinimumSize = new Vector2(28, 28);
        var orb = new StyleBoxFlat { BgColor = new Color(0.082f, 0.094f, 0.149f, 1f) };
        orb.SetCornerRadiusAll(14);
        costBox.AddThemeStyleboxOverride("normal", orb);
        header.AddChild(costBox);

        var name = prediction.Name ?? "?";
        if (prediction.Upgraded && !name.EndsWith("+")) name += "+";
        var nameLabel = new Label();
        nameLabel.Text = name;
        nameLabel.AddThemeColorOverride("font_color", prediction.Upgraded ? Colors.UpgradedColor : Colors.TextPrimary);
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        header.AddChild(nameLabel);
        vbox.AddChild(header);

        // Type + upgrade
        var typeText = card.Type.ToString();
        if (prediction.Upgraded) typeText += "  ⬆";
        var typeLabel = new Label();
        typeLabel.Text = typeText;
        typeLabel.AddThemeColorOverride("font_color", prediction.Upgraded ? Colors.UpgradedColor : Colors.TextSecondary);
        typeLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(typeLabel);

        // Description — scrollable if long
        try
        {
            var mutable = card.ToMutable();
            string? descText;
            if (prediction.Upgraded && mutable.IsUpgradable)
            {
                MegaCrit.Sts2.Core.Commands.CardCmd.Upgrade(mutable, MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle.None);
                descText = mutable.GetDescriptionForUpgradePreview();
            }
            else
            {
                descText = mutable.GetDescriptionForPile(PileType.None);
            }
            if (!string.IsNullOrEmpty(descText) && descText.Length > 2)
            {
                var descLabel = MakeMegaLabel(descText, 12);
                vbox.AddChild(descLabel);
            }
        }
        catch (Exception ex) { ModLogger.Info($"HoverCard err: {ex.Message}"); }

        Attach(BuildCard(vbox));
    }

    private static MegaRichTextLabel MakeMegaLabel(string text, int fontSize)
    {
        var label = new MegaRichTextLabel();
        label.BbcodeEnabled = true;
        label.Text = text;
        label.FitContent = true;
        label.ScrollActive = false;
        label.AddThemeFontSizeOverride("normal_font_size", fontSize);
        label.AddThemeFontSizeOverride("bold_font_size", fontSize);
        label.AddThemeFontSizeOverride("mono_font_size", fontSize);
        // Disable auto-size so font stays fixed
        if (_autoSizeField != null)
            _autoSizeField.SetValue(label, false);
        return label;
    }

    public static void ShowRelic(RelicPrediction? prediction)
    {
        if (_parent == null || prediction?.Relic == null) return;

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 5);

        var nameLabel = new Label();
        nameLabel.Text = $"{prediction.Name}";
        nameLabel.AddThemeColorOverride("font_color", Colors.TextPrimary);
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        vbox.AddChild(nameLabel);

        var rarityLabel = new Label();
        rarityLabel.Text = prediction.RarityLabel;
        rarityLabel.AddThemeColorOverride("font_color", Colors.RelicAccent);
        rarityLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(rarityLabel);

        var flavor = prediction.Relic.Flavor;
        var flavorText = flavor.GetFormattedText();
        if (!string.IsNullOrEmpty(flavorText) && flavorText.Length > 2)
        {
            var descLabel = MakeMegaLabel(flavorText, 12);
            vbox.AddChild(descLabel);
        }

        Attach(BuildCard(vbox));
    }

    public static void ShowPotion(PotionModel? potion)
    {
        if (_parent == null || potion == null) return;

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 5);

        // Name
        var name = potion.Title.GetFormattedText();
        var nameLabel = new Label();
        nameLabel.Text = name ?? "?";
        nameLabel.AddThemeColorOverride("font_color", Colors.TextPrimary);
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        vbox.AddChild(nameLabel);

        // Description
        var desc = potion.DynamicDescription;
        if (desc != null)
        {
            var descText = desc.GetFormattedText();
            if (!string.IsNullOrEmpty(descText) && descText.Length > 2)
            {
                var descLabel = MakeMegaLabel(descText, 12);
                vbox.AddChild(descLabel);
            }
        }

        Attach(BuildCard(vbox));
    }

    public static void Hide()
    {
        _panel?.QueueFree();
        _panel = null;
    }

    private static PanelContainer BuildCard(Control content)
    {
        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(PanelWidth, 0);
        var bg = new StyleBoxFlat();
        bg.BgColor = CardBg;
        bg.SetCornerRadiusAll(8);
        bg.BorderWidthLeft = 2;
        bg.BorderWidthRight = 2;
        bg.BorderWidthTop = 2;
        bg.BorderWidthBottom = 2;
        bg.BorderColor = GoldBorder;
        bg.ContentMarginLeft = 12;
        bg.ContentMarginRight = 12;
        bg.ContentMarginTop = 10;
        bg.ContentMarginBottom = 10;
        panel.AddThemeStyleboxOverride("panel", bg);
        panel.AddChild(content);
        return panel;
    }

    private static void Attach(PanelContainer panel)
    {
        Hide();
        _panel = panel;
        panel.ZIndex = 200;
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        Position(panel);
        _parent!.AddChild(panel);
    }

    private static void Position(Control popup)
    {
        if (_parent == null) return;
        var mouse = _parent.GetGlobalMousePosition();
        var vs = _parent.GetViewportRect().Size;
        popup.Position = new Vector2(
            Mathf.Min(mouse.X + 18, vs.X - PanelWidth - 30),
            Mathf.Min(mouse.Y + 6, vs.Y - 300));
    }
}
