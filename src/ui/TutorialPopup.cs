using Godot;

namespace PredictEverything;

/// <summary>
/// First-time tutorial modal shown when the player enters a CrystalSphere
/// event for the first time. Displays a title, body text (BBCode supported),
/// and a "Got it" button that marks the tutorial as seen and saves config.
/// Only shown once — gated by Config.TutorialShown.
/// </summary>
public partial class TutorialPopup : Control
{
    // Panel palette (matches InfoPanel)
    private static readonly Color DeepSpaceBg = new(0.043f, 0.055f, 0.102f, 0.95f);
    private static readonly Color PanelBorder = new(0.118f, 0.141f, 0.200f, 1f);
    private static readonly Color StarWhite = new(0.784f, 0.816f, 0.878f);
    private static readonly Color WarmOrange = new(1f, 0.42f, 0.21f);

    private const float ModalW = 540f;
    private const float ModalH = 520f;

    // =============== Public API ===============

    /// <summary>
    /// Show the tutorial popup if it has not been shown before.
    /// Checks Config.TutorialShown, and on dismissal sets it to true.
    /// </summary>
    public static void ShowIfNeeded(Control parent)
    {
        if (PredictEverythingConfig.Instance.TutorialShown) return;
        Show(parent);
    }

    /// <summary>
    /// Always show the tutorial popup, regardless of whether it has been seen.
    /// Used by the help button for re-reading.
    /// </summary>
    public static void Show(Control parent)
    {
        var popup = new TutorialPopup();
        parent.AddChild(popup);
        popup.Build();
    }

    // =============== Build ===============

    private void Build()
    {
        Name = "PredictEverythingTutorialPopup";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 250;

        // Dark backdrop covering the full screen
        var backdrop = new ColorRect();
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.Color = new Color(0, 0, 0, 0.6f);
        backdrop.MouseFilter = MouseFilterEnum.Stop;
        AddChild(backdrop);

        // Centered modal panel
        var panel = new Panel();
        panel.MouseFilter = MouseFilterEnum.Stop;
        var viewRect = GetViewport().GetVisibleRect().Size;
        panel.Position = new Vector2(
            (viewRect.X - ModalW) / 2f,
            (viewRect.Y - ModalH) / 2f);
        panel.CustomMinimumSize = new Vector2(ModalW, ModalH);

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
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        // Content vertical container
        var content = new VBoxContainer();
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        content.AddThemeConstantOverride("separation", 8);
        content.OffsetLeft = 20;
        content.OffsetRight = -20;
        content.OffsetTop = 16;
        content.OffsetBottom = -16;
        panel.AddChild(content);

        // Title
        var title = new Label { Text = I18n.Tr("tutorial_title") };
        title.AddThemeColorOverride("font_color", WarmOrange);
        title.AddThemeFontSizeOverride("font_size", 16);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(title);

        content.AddChild(new HSeparator());

        // Body — RichTextLabel with BBCode support for formatted instructions
        var body = new RichTextLabel();
        body.BbcodeEnabled = true;
        body.FitContent = true;
        body.ScrollFollowing = false;
        body.ScrollActive = true;
        body.AddThemeFontSizeOverride("normal_font_size", 12);
        body.AddThemeColorOverride("default_color", StarWhite);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.Text = I18n.Tr("tutorial_body");
        content.AddChild(body);

        // "Got it" button — centered
        var btnRow = new HBoxContainer();
        btnRow.AddChild(new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });

        var gotItBtn = new Button { Text = I18n.Tr("tutorial_got_it") };
        gotItBtn.CustomMinimumSize = new Vector2(120, 36);
        gotItBtn.AddThemeFontSizeOverride("font_size", 13);

        // Button styling
        var btnNormal = new StyleBoxFlat
        {
            BgColor = new Color(1f, 1f, 1f, 0.12f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(1f, 1f, 1f, 0.25f),
        };
        btnNormal.SetCornerRadiusAll(6);
        var btnHover = new StyleBoxFlat
        {
            BgColor = new Color(1f, 1f, 1f, 0.20f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(1f, 1f, 1f, 0.35f),
        };
        btnHover.SetCornerRadiusAll(6);
        gotItBtn.AddThemeStyleboxOverride("normal", btnNormal);
        gotItBtn.AddThemeStyleboxOverride("hover", btnHover);
        gotItBtn.AddThemeStyleboxOverride("pressed", btnNormal);
        gotItBtn.AddThemeStyleboxOverride("focus", btnNormal);

        gotItBtn.Pressed += () =>
        {
            PredictEverythingConfig.Instance.TutorialShown = true;
            PredictEverythingConfig.Save();
            QueueFree();
        };
        btnRow.AddChild(gotItBtn);

        btnRow.AddChild(new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        content.AddChild(btnRow);
    }
}
