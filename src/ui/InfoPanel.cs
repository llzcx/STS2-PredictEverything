using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Potions;

namespace PredictEverything;

/// <summary>
/// Main prediction panel UI for the Crystal Sphere event.
/// Displays a 4-column grid (Rare / Uncommon / Common / Relic) of
/// predicted rewards for RNG offsets 0-26, with row coloring for
/// locked/planned/current/unreachable states.
/// Supports row-click TogglePlan, plan summary label, drag-to-move
/// title bar, refresh and collapse buttons, and I18n language switching.
/// </summary>
public partial class InfoPanel : Control
{
    private CrystalSpherePredictor _predictor = null!;
    private VBoxContainer _root = null!;
    private HBoxContainer _titleBar = null!;
    private VBoxContainer _rowsContainer = null!;
    private RichTextLabel _planLabel = null!;
    private ScrollContainer _scroll = null!;
    private HBoxContainer _headerRow = null!;
    private HBoxContainer _filterRow = null!;
    private OptionButton _rareFilter = null!;
    private OptionButton _uncommonFilter = null!;
    private OptionButton _commonFilter = null!;
    private OptionButton _relicFilter = null!;
    private OptionButton _commonPotionFilter = null!;
    private OptionButton _rarePotionFilter = null!;

    private bool _collapsed;
    private DragHandler _dragHandler = null!;
    private Label _titleLabel = null!;
    private Button _collapseBtn = null!;
    private Button _helpBtn = null!;
    private Control _screenParent = null!;
    private VBoxContainer _footer = null!;
    private float _fullHeight;

    // Row highlight colors
    private static readonly Color PlannedColor = new(1f, 0.84f, 0f, 0.7f);
    private static readonly Color PlannedBorder = new(1f, 0.7f, 0f, 0.9f);
    private static readonly Color LockedColor = new(0f, 0.8f, 0f, 0.45f);
    private static readonly Color DisabledColor = new(0.25f, 0.18f, 0.18f, 0.55f);
    // Per-column reserved colors (matching column themes)
    private static readonly Color RareReservedColor = new(0.45f, 0.15f, 0.08f, 0.5f);
    private static readonly Color UncommonReservedColor = new(0.10f, 0.25f, 0.45f, 0.5f);
    private static readonly Color CommonReservedColor = new(0.20f, 0.20f, 0.25f, 0.55f);
    private static readonly Color RelicReservedColor = new(0.10f, 0.38f, 0.18f, 0.5f);
    private static readonly Color PotionReservedColor = new(0.12f, 0.30f, 0.42f, 0.5f);
    private static readonly Color MixedReservedColor = new(0.35f, 0.28f, 0.20f, 0.5f);
    private static readonly Color CurrentHighlightColor = new(1f, 1f, 1f, 0.2f);

    // Panel palette (matches RoutePlanner aesthetic)
    private static readonly Color DeepSpaceBg = new(0.043f, 0.055f, 0.102f, 0.92f);
    private static readonly Color PanelBorder = new(0.118f, 0.141f, 0.200f, 1f);
    private static readonly Color StarWhite = new(0.784f, 0.816f, 0.878f);
    private static readonly Color Gold = new(0.722f, 0.588f, 0.290f);
    private static readonly Color WarmOrange = new(1f, 0.42f, 0.21f);
    private static readonly Color IceBlue = new(0.30f, 0.65f, 1f);
    private static readonly Color LimeGreen = new(0.29f, 0.87f, 0.50f);

    private static readonly ColumnType[] Cols =
        { ColumnType.Rare, ColumnType.Uncommon, ColumnType.Common, ColumnType.Relic, ColumnType.CommonPotion, ColumnType.RarePotion };

    // I18n registry for language switching
    private readonly List<(Control control, string key)> _i18nRegistry = new();
    private readonly Dictionary<ColumnType, RichTextLabel> _headerLabels = new();



    // =============== Public API ===============

    /// <summary>
    /// Create and attach the InfoPanel to a parent Control.
    /// Wires up predictor events and I18n language switching.
    /// </summary>
    public static InfoPanel Create(Control parent)
    {
        var panel = new InfoPanel();
        panel._screenParent = parent;
        panel.Build();
        parent.AddChild(panel);

        var predictor = CrystalSpherePredictor.Instance!;
        panel._predictor = predictor;
        predictor.StateChanged += panel.Refresh;
        predictor.PlanChanged += panel.RefreshPlanLabel;
        I18n.LanguageChanged += panel.OnLanguageChanged;

        // Initial population — predictor already initialized before Screen_Ready
        panel.Refresh();
        panel.RefreshPlanLabel();
        panel._dragHandler.Start();
        return panel;
    }

    // =============== Build ===============

    private void Build()
    {
        Name = "PredictEverythingInfoPanel";
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;

        var config = PredictEverythingConfig.Instance;

        // Panel background
        var bg = new Panel();
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = DeepSpaceBg;
        bgStyle.SetCornerRadiusAll(12);
        bgStyle.BorderWidthLeft = 1;
        bgStyle.BorderWidthRight = 1;
        bgStyle.BorderWidthTop = 1;
        bgStyle.BorderWidthBottom = 1;
        bgStyle.BorderColor = PanelBorder;
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(bg);

        // Top accent line
        var accentLine = new ColorRect();
        accentLine.SetAnchorsPreset(LayoutPreset.TopWide);
        accentLine.Color = WarmOrange;
        accentLine.CustomMinimumSize = new Vector2(0, 2);
        accentLine.OffsetLeft = 8;
        accentLine.OffsetRight = -8;
        accentLine.OffsetTop = 0;
        AddChild(accentLine);

        // Root vertical container
        _root = new VBoxContainer();
        _root.SetAnchorsPreset(LayoutPreset.FullRect);
        _root.AddThemeConstantOverride("separation", 4);
        _root.OffsetLeft = 8;
        _root.OffsetRight = -8;
        _root.OffsetTop = 6;
        _root.OffsetBottom = -8;
        AddChild(_root);

        // ---- Title bar (draggable) ----
        _titleBar = new HBoxContainer();
        _titleBar.MouseFilter = MouseFilterEnum.Stop;
        _titleBar.AddThemeConstantOverride("separation", 6);

        _titleLabel = CreateLocalizedLabel("panel_title", 22, StarWhite);
        _titleLabel.AddThemeFontSizeOverride("font_size", 18);
        _titleBar.AddChild(_titleLabel);

        _titleBar.AddChild(new Control
        {
            CustomMinimumSize = new Vector2(10, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        });

        // Help button
        _helpBtn = CreateIconButton("?", 16);
        _helpBtn.Pressed += () =>
        {
            try { TutorialPopup.Show(_screenParent); }
            catch (System.Exception ex) { ModLogger.Error($"Help button failed: {ex}"); }
        };
        _titleBar.AddChild(_helpBtn);

        // Refresh button
        var refreshBtn = CreateIconButton("↻", 16);
        refreshBtn.Pressed += OnRefreshClicked;
        _titleBar.AddChild(refreshBtn);

        // Collapse button
        _collapseBtn = CreateIconButton("▼", 16);
        _collapseBtn.Pressed += ToggleCollapse;
        _titleBar.AddChild(_collapseBtn);

        _root.AddChild(_titleBar);

        // ---- Column headers ----
        _headerRow = new HBoxContainer();
        _headerRow.AddThemeConstantOverride("separation", 1);

        // Narrow offset column header
        var offsetHeader = CreateLabel("#", 9, new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.4f));
        offsetHeader.CustomMinimumSize = new Vector2(24, 0);
        offsetHeader.HorizontalAlignment = HorizontalAlignment.Center;
        _headerRow.AddChild(offsetHeader);

        foreach (var col in Cols)
        {
            string key = col switch
            {
                ColumnType.Rare => "col_rare",
                ColumnType.Uncommon => "col_uncommon",
                ColumnType.Common => "col_common",
                ColumnType.Relic => "col_relic",
                ColumnType.CommonPotion => "col_common_potion",
                ColumnType.RarePotion => "col_rare_potion",
                _ => ""
            };
            var headerLabel = new RichTextLabel();
            headerLabel.BbcodeEnabled = true;
            headerLabel.FitContent = true;
            headerLabel.ScrollActive = false;
            headerLabel.HorizontalAlignment = HorizontalAlignment.Center;
            headerLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            headerLabel.AddThemeFontSizeOverride("normal_font_size", 13);
            headerLabel.AddThemeColorOverride("default_color", StarWhite);
            headerLabel.MouseFilter = MouseFilterEnum.Pass;
            _headerLabels[col] = headerLabel;
            _i18nRegistry.Add((headerLabel, key));
            _headerRow.AddChild(headerLabel);
        }
        _root.AddChild(_headerRow);
        RefreshHeaders();

        // ---- Card/Relic filter dropdowns ---- (temporarily disabled)
        // Card/Relic filter dropdowns
        _filterRow = new HBoxContainer();
        _filterRow.AddThemeConstantOverride("separation", 1);
        // Offset spacer — matches the 24px offset column in header and data rows
        _filterRow.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0) });
        _rareFilter = BuildFilter(ColumnType.Rare, "col_rare");
        _uncommonFilter = BuildFilter(ColumnType.Uncommon, "col_uncommon");
        _commonFilter = BuildFilter(ColumnType.Common, "col_common");
        _relicFilter = BuildFilter(ColumnType.Relic, "col_relic");
        _commonPotionFilter = BuildPotionFilter(PotionRarity.Common, "col_common_potion");
        _rarePotionFilter = BuildPotionFilter(PotionRarity.Rare, "col_rare_potion");
        _filterRow.AddChild(_rareFilter);
        _filterRow.AddChild(_uncommonFilter);
        _filterRow.AddChild(_commonFilter);
        _filterRow.AddChild(_relicFilter);
        _filterRow.AddChild(_commonPotionFilter);
        _filterRow.AddChild(_rarePotionFilter);
        _root.AddChild(_filterRow);

        // Header separator

        // ---- Bottom plan summary (above scroll, always visible) ----
        _planLabel = new RichTextLabel();
        _planLabel.BbcodeEnabled = true;
        _planLabel.FitContent = true;
        _planLabel.ScrollFollowing = false;
        _planLabel.ScrollActive = false;
        _planLabel.MouseFilter = MouseFilterEnum.Pass;
        _planLabel.AddThemeFontSizeOverride("normal_font_size", 13);
        _planLabel.AddThemeColorOverride("default_color", StarWhite);
        _root.AddChild(_planLabel);

        // ---- Scrollable rows area ----
        _scroll = new ScrollContainer { Name = "Content" };
        _scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        _scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;

        // Slim scrollbar styling
        var vScroll = _scroll.GetVScrollBar();
        vScroll.CustomMinimumSize = new Vector2(4, 0);
        vScroll.AddThemeStyleboxOverride("scroll", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) });
        var grabber = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.15f) };
        grabber.SetCornerRadiusAll(2);
        vScroll.AddThemeStyleboxOverride("grabber", grabber);
        var grabberHover = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.25f) };
        grabberHover.SetCornerRadiusAll(2);
        vScroll.AddThemeStyleboxOverride("grabber_highlight", grabberHover);

        _rowsContainer = new VBoxContainer();
        _rowsContainer.AddThemeConstantOverride("separation", 1);
        _rowsContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_rowsContainer);
        _root.AddChild(_scroll);

        // ---- Footer legend ----
        _root.AddChild(new HSeparator());
        _footer = new VBoxContainer();
        _footer.AddThemeConstantOverride("separation", 2);

        var legend1 = new RichTextLabel();
        legend1.BbcodeEnabled = true;
        legend1.FitContent = true;
        legend1.ScrollActive = false;
        legend1.AutowrapMode = TextServer.AutowrapMode.Word;
        legend1.SizeFlagsHorizontal = SizeFlags.Fill;
        legend1.Text = $"[color=#66FF66][b]✦[/b][/color] = {I18n.Tr("legend_upgraded")}";
        legend1.AddThemeFontSizeOverride("normal_font_size", 11);
        legend1.AddThemeColorOverride("default_color", new Color(0.6f, 0.6f, 0.6f));
        _footer.AddChild(legend1);

        var legend2 = new RichTextLabel();
        legend2.BbcodeEnabled = true;
        legend2.FitContent = true;
        legend2.ScrollActive = false;
        legend2.AutowrapMode = TextServer.AutowrapMode.Word;
        legend2.SizeFlagsHorizontal = SizeFlags.Fill;
        legend2.Text = I18n.Tr("legend_costs");
        legend2.AddThemeFontSizeOverride("normal_font_size", 11);
        legend2.AddThemeColorOverride("default_color", new Color(0.6f, 0.6f, 0.6f));
        _footer.AddChild(legend2);

        var legend3 = new RichTextLabel();
        legend3.BbcodeEnabled = true;
        legend3.FitContent = true;
        legend3.ScrollActive = false;
        legend3.AutowrapMode = TextServer.AutowrapMode.Word;
        legend3.SizeFlagsHorizontal = SizeFlags.Fill;
        legend3.Text = I18n.Tr("legend_order");
        legend3.AddThemeFontSizeOverride("normal_font_size", 11);
        legend3.AddThemeColorOverride("default_color", new Color(0.6f, 0.6f, 0.6f));
        _footer.AddChild(legend3);

        var legend4 = new RichTextLabel();
        legend4.BbcodeEnabled = true;
        legend4.FitContent = true;
        legend4.ScrollActive = false;
        legend4.AutowrapMode = TextServer.AutowrapMode.Word;
        legend4.SizeFlagsHorizontal = SizeFlags.Fill;
        legend4.Text = I18n.Tr("legend_filter");
        legend4.AddThemeFontSizeOverride("normal_font_size", 11);
        legend4.AddThemeColorOverride("default_color", new Color(0.6f, 0.6f, 0.6f));
        _i18nRegistry.Add((legend4, "legend_filter"));
        _footer.AddChild(legend4);

        _root.AddChild(_footer);

        // ---- Size and positioning ----
        float w = config.PanelW > 0 ? config.PanelW : 560f;
        float h = config.PanelH > 0 ? config.PanelH : 680f;
        float y = config.PanelY > 0 ? config.PanelY : 200f;

        if (config.PanelX < 0)
        {
            // Auto: left side
            SetAnchorsPreset(LayoutPreset.TopLeft);
            OffsetLeft = 20;
            OffsetRight = 20 + w;
            OffsetTop = y;
            OffsetBottom = y + h;
        }
        else
        {
            AnchorLeft = 0;
            AnchorTop = 0;
            AnchorRight = 0;
            AnchorBottom = 0;
            OffsetLeft = config.PanelX;
            OffsetRight = config.PanelX + w;
            OffsetTop = y;
            OffsetBottom = y + h;
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
                var config = PredictEverythingConfig.Instance;
                config.PanelX = gpos.X; config.PanelY = gpos.Y;
                PredictEverythingConfig.Save();
            });

        // Initial data population
        Refresh();
        UpdateFilterStates();
    }

    // =============== Refresh / Data ===============

    /// <summary>
    /// Rebuild all rows from predictor data. Called on StateChanged.
    /// </summary>
    public void Refresh()
    {
        if (_rowsContainer == null) return;
        if (_predictor == null || !_predictor.IsActive) return;
        RefreshHeaders();
        UpdateFilterStates();

        // Clear existing rows
        while (_rowsContainer.GetChildCount() > 0)
        {
            var child = _rowsContainer.GetChild(0);
            _rowsContainer.RemoveChild(child);
            child.QueueFree();
        }
        int currentOffset = _predictor.CurrentOffset;

        for (int row = 0; row <= CrystalSpherePredictor.MaxOffset; row++)
        {
            var pred = _predictor.Predictions[row];
            if (pred == null) continue;

            var rowHbox = new HBoxContainer();
            rowHbox.AddThemeConstantOverride("separation", 1);
            rowHbox.MouseFilter = MouseFilterEnum.Pass;

            // Determine row background color
            Color rowColor;
            if (IsRowLocked(row))
            {
                rowColor = LockedColor;
            }
            else if (row < currentOffset)
            {
                rowColor = DisabledColor;
            }
            else if (row == currentOffset)
            {
                rowColor = CurrentHighlightColor;
            }
            else
            {
                rowColor = GetRowReservedColor(row) ?? new Color(0, 0, 0, 0);
            }

            // Offset number label
            var offsetLabel = CreateLabel(row.ToString(), 9,
                row == currentOffset ? StarWhite : new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.4f));
            offsetLabel.CustomMinimumSize = new Vector2(24, 0);
            offsetLabel.HorizontalAlignment = HorizontalAlignment.Center;
            offsetLabel.VerticalAlignment = VerticalAlignment.Center;
            rowHbox.AddChild(offsetLabel);

            // Build each column cell
            foreach (var col in Cols)
            {
                var cell = BuildCell(row, col, pred);
                rowHbox.AddChild(cell);
            }

            // Always wrap row in PanelContainer — border always present (transparent when idle)
            // to prevent layout shift when row highlight changes
            var rowWrapper = new PanelContainer();
            rowWrapper.MouseFilter = MouseFilterEnum.Pass;
            rowWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var wrapperStyle = new StyleBoxFlat
            {
                BgColor = rowColor,
                ContentMarginLeft = 2,
                ContentMarginRight = 2,
                ContentMarginTop = 1,
                ContentMarginBottom = 1
            };
            wrapperStyle.SetCornerRadiusAll(3);
            wrapperStyle.BorderWidthLeft = 2;
            wrapperStyle.BorderWidthRight = 2;
            wrapperStyle.BorderWidthTop = 2;
            wrapperStyle.BorderWidthBottom = 2;
            wrapperStyle.BorderColor = IsRowPlanned(row) ? PlannedBorder : new Color(0, 0, 0, 0);
            rowWrapper.AddThemeStyleboxOverride("panel", wrapperStyle);
            rowHbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            rowWrapper.AddChild(rowHbox);
            _rowsContainer.AddChild(rowWrapper);
        }
    }

    /// <summary>
    /// Build a single cell for a column at a given row offset.
    /// </summary>
    private static readonly Color CellPlannedBg = new(1f, 0.75f, 0.1f, 0.6f);

    private Control BuildCell(int row, ColumnType col, OffsetPrediction pred)
    {
        var inner = new VBoxContainer();
        inner.MouseFilter = MouseFilterEnum.Pass;
        inner.AddThemeConstantOverride("separation", 0);
        inner.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // Cell-level highlight: locked cell→green (always show); planned cell→gold (only for unlocked cols)
        // Always wrap in PanelContainer with 2px border — transparent when idle so layout never shifts
        bool colLocked = _predictor.IsColumnLocked(col);
        bool isLockedCell = IsColumnLockedAt(col, row);
        bool isPlannedCell = !colLocked && _predictor.IsColumnPlannedAt(col, row);

        var cellBg = new StyleBoxFlat();
        cellBg.SetCornerRadiusAll(4);
        cellBg.BorderWidthLeft = 2; cellBg.BorderWidthRight = 2;
        cellBg.BorderWidthTop = 2; cellBg.BorderWidthBottom = 2;
        cellBg.BgColor = new Color(0, 0, 0, 0);
        cellBg.BorderColor = new Color(0, 0, 0, 0);
        if (isLockedCell)
        {
            cellBg.BgColor = LockedColor;
            cellBg.BorderColor = new Color(0f, 0.7f, 0f, 0.6f);
        }
        else if (isPlannedCell)
        {
            cellBg.BgColor = CellPlannedBg;
            cellBg.BorderColor = new Color(1f, 0.6f, 0f, 0.8f);
        }

        var wrapper = new PanelContainer();
        wrapper.MouseFilter = isLockedCell || isPlannedCell ? MouseFilterEnum.Stop : MouseFilterEnum.Pass;
        wrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        wrapper.AddThemeStyleboxOverride("panel", cellBg);
        inner.MouseFilter = isLockedCell || isPlannedCell ? MouseFilterEnum.Ignore : MouseFilterEnum.Pass;
        wrapper.AddChild(inner);

        if (col == ColumnType.Relic)
        {
            var relic = pred.Relic;
            string relicName = relic?.Name ?? "?";
            var relicLabel = CreateLabel(relicName, 12, StarWhite);
            relicLabel.HorizontalAlignment = HorizontalAlignment.Center;
            relicLabel.AutowrapMode = TextServer.AutowrapMode.Word;
            relicLabel.MouseFilter = MouseFilterEnum.Pass;

            // Hover relic → show relic tooltip
            var capturedRelic = relic;
            relicLabel.MouseEntered += () => HoverTooltip.ShowRelic(capturedRelic);
            relicLabel.MouseExited += () => HoverTooltip.Hide();
            inner.AddChild(relicLabel);
        }
        else if (col == ColumnType.CommonPotion)
        {
            var seq = _predictor.CommonPotionSequence;
            if (seq != null)
            {
                string? lockedName = isLockedCell ? _predictor.GetPotionLockedNameAt(row, PotionRarity.Common) : null;
                int plannedIdx = isPlannedCell ? _predictor.GetPotionPlanIndex(row) : -1;
                for (int i = 0; i < seq.Length; i++)
                {
                    var p = seq[i];
                    string name = p?.Name ?? "?";
                    bool isActual = isLockedCell && name == lockedName;
                    bool isPlannedEntry = isPlannedCell && i == plannedIdx;
                    if (isActual)
                    {
                        var rtl = new RichTextLabel();
                        rtl.BbcodeEnabled = true;
                        rtl.Text = $"[b]{name}[/b]";
                        rtl.FitContent = true;
                        rtl.ScrollActive = false;
                        rtl.HorizontalAlignment = HorizontalAlignment.Center;
                        rtl.AutowrapMode = TextServer.AutowrapMode.Word;
                        rtl.MouseFilter = MouseFilterEnum.Pass;
                        rtl.AddThemeColorOverride("default_color", IceBlue);
                        rtl.AddThemeFontSizeOverride("normal_font_size", 12);
                        rtl.AddThemeFontSizeOverride("bold_font_size", 12);
                        inner.AddChild(rtl);
                    }
                    else if (isPlannedEntry)
                    {
                        var rtl = new RichTextLabel();
                        rtl.BbcodeEnabled = true;
                        rtl.Text = $"[b][color=#FFB830]{name}[/color][/b]";
                        rtl.FitContent = true;
                        rtl.ScrollActive = false;
                        rtl.HorizontalAlignment = HorizontalAlignment.Center;
                        rtl.AutowrapMode = TextServer.AutowrapMode.Word;
                        rtl.MouseFilter = MouseFilterEnum.Pass;
                        rtl.AddThemeFontSizeOverride("normal_font_size", 12);
                        rtl.AddThemeFontSizeOverride("bold_font_size", 12);
                        inner.AddChild(rtl);
                    }
                    else
                    {
                        var lbl = CreateLabel(name, 12, new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.85f));
                        lbl.HorizontalAlignment = HorizontalAlignment.Center;
                        lbl.AutowrapMode = TextServer.AutowrapMode.Word;
                        lbl.MouseFilter = MouseFilterEnum.Pass;
                        var captured = p;
                        lbl.MouseEntered += () => { if (captured?.Potion != null) HoverTooltip.ShowPotion(captured.Potion); };
                        lbl.MouseExited += () => HoverTooltip.Hide();
                        inner.AddChild(lbl);
                    }
                }
            }
        }
        else if (col == ColumnType.RarePotion)
        {
            var seq = _predictor.RarePotionSequence;
            if (seq != null)
            {
                string? lockedName = isLockedCell ? _predictor.GetPotionLockedNameAt(row, PotionRarity.Rare) : null;
                int plannedIdx = isPlannedCell ? _predictor.GetPotionPlanIndex(row) : -1;
                for (int i = 0; i < seq.Length; i++)
                {
                    var p = seq[i];
                    string name = p?.Name ?? "?";
                    bool isActual = isLockedCell && name == lockedName;
                    bool isPlannedEntry = isPlannedCell && i == plannedIdx;
                    if (isActual)
                    {
                        var rtl = new RichTextLabel();
                        rtl.BbcodeEnabled = true;
                        rtl.Text = $"[b]{name}[/b]";
                        rtl.FitContent = true;
                        rtl.ScrollActive = false;
                        rtl.HorizontalAlignment = HorizontalAlignment.Center;
                        rtl.AutowrapMode = TextServer.AutowrapMode.Word;
                        rtl.MouseFilter = MouseFilterEnum.Pass;
                        rtl.AddThemeColorOverride("default_color", IceBlue);
                        rtl.AddThemeFontSizeOverride("normal_font_size", 12);
                        rtl.AddThemeFontSizeOverride("bold_font_size", 12);
                        inner.AddChild(rtl);
                    }
                    else if (isPlannedEntry)
                    {
                        var rtl = new RichTextLabel();
                        rtl.BbcodeEnabled = true;
                        rtl.Text = $"[b][color=#FFB830]{name}[/color][/b]";
                        rtl.FitContent = true;
                        rtl.ScrollActive = false;
                        rtl.HorizontalAlignment = HorizontalAlignment.Center;
                        rtl.AutowrapMode = TextServer.AutowrapMode.Word;
                        rtl.MouseFilter = MouseFilterEnum.Pass;
                        rtl.AddThemeFontSizeOverride("normal_font_size", 12);
                        rtl.AddThemeFontSizeOverride("bold_font_size", 12);
                        inner.AddChild(rtl);
                    }
                    else
                    {
                        var lbl = CreateLabel(name, 12, new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.85f));
                        lbl.HorizontalAlignment = HorizontalAlignment.Center;
                        lbl.AutowrapMode = TextServer.AutowrapMode.Word;
                        lbl.MouseFilter = MouseFilterEnum.Pass;
                        var captured = p;
                        lbl.MouseEntered += () => { if (captured?.Potion != null) HoverTooltip.ShowPotion(captured.Potion); };
                        lbl.MouseExited += () => HoverTooltip.Hide();
                        inner.AddChild(lbl);
                    }
                }
            }
        }
        else
        {
            var cards = pred.GetCards(col);
            foreach (var card in cards)
            {
                string cardName = card.Name ?? "?";
                if (card.Upgraded && !cardName.EndsWith("+"))
                    cardName += "+";

                Control cardLabel;
                if (card.Upgraded)
                {
                    var rtl = new RichTextLabel();
                    rtl.BbcodeEnabled = true;
                    rtl.Text = $"[b][color=#66FF66]{cardName}[/color][/b]";
                    rtl.FitContent = true;
                    rtl.ScrollActive = false;
                    rtl.HorizontalAlignment = HorizontalAlignment.Center;
                    rtl.AutowrapMode = TextServer.AutowrapMode.Word;
                    rtl.ClipContents = true;
                    rtl.MouseFilter = MouseFilterEnum.Pass;
                    rtl.AddThemeFontSizeOverride("normal_font_size", 12);
                    rtl.AddThemeFontSizeOverride("bold_font_size", 12);
                    cardLabel = rtl;
                }
                else
                {
                    var lbl = CreateLabel(cardName, 12,
                        new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.85f));
                    lbl.HorizontalAlignment = HorizontalAlignment.Center;
                    lbl.AutowrapMode = TextServer.AutowrapMode.Word;
                    lbl.ClipContents = true;
                    lbl.MouseFilter = MouseFilterEnum.Pass;
                    cardLabel = lbl;
                }

                // Hover card → show tooltip
                var capturedCard = card;
                cardLabel.MouseEntered += () => HoverTooltip.ShowCard(capturedCard);
                cardLabel.MouseExited += () => HoverTooltip.Hide();
                inner.AddChild(cardLabel);
            }
        }

        // Click handler: left-click toggle plan, right-click show preview popup
        int capturedRow = row;
        ColumnType capturedCol = col;
        wrapper.GuiInput += (InputEvent e) =>
        {
            if (e is InputEventMouseButton mb && mb.Pressed)
            {
                if (mb.ButtonIndex == MouseButton.Left)
                {
                    if (_predictor.IsColumnLocked(capturedCol)) return;
                    if (capturedRow < _predictor.CurrentOffset) return;
                    if (PredictEverythingConfig.Instance.VerboseLogging)
                    {
                        string cellDesc = FormatCellDesc(capturedRow, capturedCol, pred);
                        ModLogger.Info($"╔══ MANUAL ROW CLICK ───────────────────────────────────────");
                        ModLogger.Info($"║ Mode: Manual Row Select");
                        ModLogger.Info($"║ Cell: offset={capturedRow} col={capturedCol} → {cellDesc}");
                        ModLogger.Info($"╚════════════════════════════════════════════════════════════");
                    }
                    _predictor.TogglePlan(capturedCol, capturedRow);
                    Refresh();
                }
                else if (mb.ButtonIndex == MouseButton.Right)
                {
                    if (col == ColumnType.Relic)
                    {
                        var rp = pred.Relic;
                        if (rp?.Relic != null)
                            PreviewPopup.ShowRelic(rp.Relic, rp.RarityLabel, rp.Icon, _screenParent);
                    }
                    else
                    {
                        var cards = pred.GetCards(col);
                        foreach (var card in cards)
                        {
                            if (card.Card != null)
                            {
                                PreviewPopup.ShowCard(card.Card, card.Upgraded, _screenParent);
                                break;
                            }
                        }
                    }
                }
            }
        };

        return wrapper;
    }

    /// <summary>
    /// Update the bottom plan summary label from ComputePlan().
    /// Called on PlanChanged event.
    /// </summary>
    public void RefreshPlanLabel()
    {
        if (_planLabel == null || _predictor == null || !_predictor.IsActive) return;

        var (feasible, sequence, error) = _predictor.ComputePlan();

        if (!feasible && error != null)
        {
            _planLabel.Text = $"[color=#FF6B35]{error}[/color]";
        }
        else if (string.IsNullOrEmpty(sequence))
        {
            _planLabel.Text = "";
        }
        else if (sequence == I18n.Tr("plan_all_resolved"))
        {
            _planLabel.Text = $"[color=#7DE2C8]{sequence}[/color]";
        }
        else
        {
            int stepCount = 1;
            for (int i = 0; i < sequence.Length; i++)
                if (sequence[i] == '→') stepCount++;
            _planLabel.Text = $"[b]{I18n.Tr("plan_prefix")}[/b]: {stepCount} {I18n.Tr("plan_clicks")} | {sequence}";
        }
    }

    /// <summary>
    /// Disable filter dropdowns for locked columns and reset their selection to default.
    /// </summary>
    private void UpdateFilterStates()
    {
        if (_predictor == null) return;

        void Apply(OptionButton? ob, bool locked)
        {
            if (ob == null) return;
            ob.Disabled = locked;
            if (locked) ob.Selected = 0;
        }

        Apply(_rareFilter, _predictor.IsColumnLocked(ColumnType.Rare));
        Apply(_uncommonFilter, _predictor.IsColumnLocked(ColumnType.Uncommon));
        Apply(_commonFilter, _predictor.IsColumnLocked(ColumnType.Common));
        Apply(_relicFilter, _predictor.IsColumnLocked(ColumnType.Relic));
        Apply(_commonPotionFilter, _predictor.IsColumnLocked(ColumnType.CommonPotion));
        Apply(_rarePotionFilter, _predictor.IsColumnLocked(ColumnType.RarePotion));
    }

    // =============== Row state helpers ===============

    private static string FormatCellDesc(int row, ColumnType col, OffsetPrediction pred)
    {
        return col switch
        {
            ColumnType.Relic => $"Relic=[{pred.Relic.Name}]",
            ColumnType.CommonPotion => $"CP=[{string.Join("|", (CrystalSpherePredictor.Instance?.CommonPotionSequence ?? System.Array.Empty<PotionPrediction>()).Select(p => p.Name))}]",
            ColumnType.RarePotion => $"RP=[{string.Join("|", (CrystalSpherePredictor.Instance?.RarePotionSequence ?? System.Array.Empty<PotionPrediction>()).Select(p => p.Name))}]",
            _ => string.Join(" | ", pred.GetCards(col).Select(c => c.Upgraded ? c.Name + "+" : c.Name))
        };
    }

    private bool IsColumnLockedAt(ColumnType col, int row) => col switch
    {
        ColumnType.Rare => _predictor.Rare.LockedAt == row,
        ColumnType.Uncommon => _predictor.Uncommon.LockedAt == row,
        ColumnType.Common => _predictor.Common.LockedAt == row,
        ColumnType.Relic => _predictor.Relic.LockedAt == row,
        ColumnType.CommonPotion => _predictor.IsCommonPotionLockedAt(row),
        ColumnType.RarePotion => _predictor.IsRarePotionLockedAt(row),
        _ => false
    };

    private bool IsRowLocked(int row)
    {
        return _predictor.Rare.LockedAt == row ||
               _predictor.Uncommon.LockedAt == row ||
               _predictor.Common.LockedAt == row ||
               _predictor.Relic.LockedAt == row ||
               _predictor.IsCommonPotionLockedAt(row) ||
               _predictor.IsRarePotionLockedAt(row);
    }

    private bool IsRowPlanned(int row)
    {
        return _predictor.Rare.PlannedAt == row ||
               _predictor.Uncommon.PlannedAt == row ||
               _predictor.Common.PlannedAt == row ||
               _predictor.Relic.PlannedAt == row ||
               _predictor.IsCommonPotionPlannedAt(row) ||
               _predictor.IsRarePotionPlannedAt(row);
    }

    private Color? GetRowReservedColor(int row)
    {
        // A planned column occupies [PlannedAt, PlannedAt + RngCost) offsets.
        ColumnType? reservingType = null;
        var cols = new[] { _predictor.Rare, _predictor.Uncommon, _predictor.Common, _predictor.Relic };
        foreach (var col in cols)
        {
            if (col.HasPlan && row >= col.PlannedAt!.Value && row < col.PlannedAt.Value + col.RngCost)
            {
                if (reservingType == null)
                    reservingType = col.Type;
                else if (reservingType != col.Type)
                    return MixedReservedColor;
            }
        }
        return reservingType switch
        {
            ColumnType.Rare => RareReservedColor,
            ColumnType.Uncommon => UncommonReservedColor,
            ColumnType.Common => CommonReservedColor,
            ColumnType.Relic => RelicReservedColor,
            _ => null
        };
    }

    // =============== Filter dropdowns ===============

    private OptionButton BuildFilter(ColumnType col, string labelKey)
    {
        var ob = new OptionButton();
        ob.AddThemeFontSizeOverride("font_size", 10);
        ob.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ob.MouseFilter = MouseFilterEnum.Pass;

        ob.AddItem($"— {I18n.Tr(labelKey)} —", 0);

        var predictor = CrystalSpherePredictor.Instance;
        if (predictor == null) return ob;

        if (col == ColumnType.Relic && predictor.RelicList != null)
        {
            foreach (var name in predictor.RelicList)
                ob.AddItem(name, ob.ItemCount);
        }
        else
        {
            var cardList = col switch
            {
                ColumnType.Rare => predictor.RareCardList,
                ColumnType.Uncommon => predictor.UncommonCardList,
                _ => predictor.CommonCardList
            };
            if (cardList != null)
            {
                foreach (var (name, upgraded) in cardList)
                {
                    string label = upgraded && !name.EndsWith("+") ? name + "+" : name;
                    ob.AddItem(label, ob.ItemCount);
                }
            }
        }
        ob.ItemSelected += _ => OnFilterChanged();
        ob.GetPopup().AddThemeFontSizeOverride("font_size", 10);
        return ob;
    }

    private OptionButton BuildPotionFilter(PotionRarity rarity, string labelKey)
    {
        var ob = new OptionButton();
        ob.AddThemeFontSizeOverride("font_size", 10);
        ob.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ob.MouseFilter = MouseFilterEnum.Pass;
        ob.AddItem($"-- {I18n.Tr(labelKey)} --", 0);
        ob.SetItemMetadata(0, -1);
        var p = CrystalSpherePredictor.Instance;
        var seq = rarity == PotionRarity.Rare ? p?.RarePotionSequence : p?.CommonPotionSequence;
        if (seq != null)
        {
            // Show each sequence entry with its global reveal index
            var idxLabels = new[] { "①", "②", "③" }; // ①, ②, ③
            for (int i = 0; i < seq.Length; i++)
            {
                var name = seq[i]?.Name;
                if (string.IsNullOrEmpty(name) || name == "?") continue;
                string label = $"{name} {idxLabels[i]}";
                ob.AddItem(label, ob.ItemCount);
                ob.SetItemMetadata(ob.ItemCount - 1, i); // store global reveal index
            }
        }
        ob.ItemSelected += _ => OnFilterChanged();
        ob.GetPopup().AddThemeFontSizeOverride("font_size", 10);
        return ob;
    }

    private void OnFilterChanged()
    {
        if (_predictor == null || !_predictor.IsActive) return;
        if (_predictor.DivinationCount == 0) return;

        (string, bool)? rareT = null, uncT = null, comT = null;
        string? relT = null;

        ParseCardFilter(_rareFilter, ColumnType.Rare, ref rareT);
        ParseCardFilter(_uncommonFilter, ColumnType.Uncommon, ref uncT);
        ParseCardFilter(_commonFilter, ColumnType.Common, ref comT);

        if (_relicFilter?.Selected > 0)
        {
            string name = _relicFilter.GetItemText(_relicFilter.Selected);
            if (CrystalSpherePredictor.Instance!.RelicMap.ContainsKey(name))
                relT = name;
        }

        int? commonPotT = null;
        int? commonPotIdx = null;
        string? commonPotName = null;
        if (_commonPotionFilter?.Selected > 0)
        {
            commonPotName = _commonPotionFilter.GetItemText((int)_commonPotionFilter.Selected);
            var cm = _commonPotionFilter.GetItemMetadata((int)_commonPotionFilter.Selected);
            commonPotIdx = (int)(long)cm;
            commonPotT = _predictor.CurrentOffset;
        }
        int? rarePotT = null;
        int? rarePotIdx = null;
        string? rarePotName = null;
        if (_rarePotionFilter?.Selected > 0)
        {
            rarePotName = _rarePotionFilter.GetItemText((int)_rarePotionFilter.Selected);
            var rm = _rarePotionFilter.GetItemMetadata((int)_rarePotionFilter.Selected);
            rarePotIdx = (int)(long)rm;
            rarePotT = commonPotT.HasValue ? commonPotT.Value + 1 : _predictor.CurrentOffset;
        }

        // Check for potion index conflict: two potions can't both be the Nth global reveal
        if (commonPotIdx.HasValue && rarePotIdx.HasValue && commonPotIdx.Value == rarePotIdx.Value)
        {
            if (PredictEverythingConfig.Instance.VerboseLogging)
                ModLogger.Info($"  Potion index conflict: CP[{commonPotIdx}] and RP[{rarePotIdx}] both want global slot {commonPotIdx} — INFEASIBLE");
            _predictor.Rare.PlannedOffsets.Clear();
            _predictor.Uncommon.PlannedOffsets.Clear();
            _predictor.Common.PlannedOffsets.Clear();
            _predictor.Relic.PlannedOffsets.Clear();
            _predictor.CommonPotionColumn.PlannedOffsets.Clear();
            _predictor.RarePotionColumn.PlannedOffsets.Clear();
            if (_planLabel != null)
                _planLabel.Text = $"[color=#FF6B35]{I18n.Tr("error_potion_index_conflict")}[/color]";
            Refresh();
            return;
        }

        if (PredictEverythingConfig.Instance.VerboseLogging)
        {
            var parts = new List<string>();
            if (rareT.HasValue) parts.Add($"Rare=[{rareT.Value.Item1}{(rareT.Value.Item2 ? "+" : "")}]");
            if (uncT.HasValue) parts.Add($"Uncommon=[{uncT.Value.Item1}{(uncT.Value.Item2 ? "+" : "")}]");
            if (comT.HasValue) parts.Add($"Common=[{comT.Value.Item1}{(comT.Value.Item2 ? "+" : "")}]");
            if (relT != null) parts.Add($"Relic=[{relT}]");
            if (commonPotName != null) parts.Add($"CommonPotion=[{commonPotName}]@{commonPotIdx}");
            if (rarePotName != null) parts.Add($"RarePotion=[{rarePotName}]@{rarePotIdx}");
            ModLogger.Info("");
            ModLogger.Info($"╔══ DROPDOWN PATH SEARCH ═══════════════════════════════════");
            ModLogger.Info($"║ Mode: Dropdown Filter");
            ModLogger.Info($"║ Current CardPredictionOffset: {_predictor.CardPredictionOffset}");
            ModLogger.Info($"║ Selections: {(parts.Count > 0 ? string.Join(", ", parts) : "(none — all default)")}");
            ModLogger.Info($"╚════════════════════════════════════════════════════════════");
        }

        var (feasible, sequence, error) = _predictor.ComputeOptimalPath(rareT, uncT, comT, relT, commonPotT, rarePotT, commonPotIdx, rarePotIdx);
        if (!feasible && error != null)
        {
            _predictor.Rare.PlannedOffsets.Clear();
            _predictor.Uncommon.PlannedOffsets.Clear();
            _predictor.Common.PlannedOffsets.Clear();
            _predictor.Relic.PlannedOffsets.Clear();
            _predictor.CommonPotionColumn.PlannedOffsets.Clear();
            _predictor.RarePotionColumn.PlannedOffsets.Clear();
        }
        Refresh();
        RefreshPlanLabel();
        // Override plan label with optimal path result
        if (_planLabel != null)
        {
            if (!feasible && error != null)
                _planLabel.Text = $"[color=#FF6B35]{error}[/color]";
            else if (!string.IsNullOrEmpty(sequence))
                _planLabel.Text = $"[b]{I18n.Tr("plan_prefix")}[/b]: {sequence}";
            else if (rareT == null && uncT == null && comT == null && relT == null && commonPotT == null && rarePotT == null)
                _planLabel.Text = "";
        }
    }

    private static void ParseCardFilter(OptionButton? ob, ColumnType col, ref (string, bool)? target)
    {
        if (ob == null || ob.Selected <= 0) return;
        string text = ob.GetItemText((int)ob.Selected);
        if (string.IsNullOrEmpty(text) || text.StartsWith("—")) return;
        bool upgraded = text.EndsWith("+");
        string cleanName = upgraded ? text[..^1] : text;
        target = (cleanName, upgraded);
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
            else if (control is RichTextLabel rtl)
                rtl.Text = $"[color=#C8D0E0]{I18n.Tr(key)}[/color]";
        }
        RefreshHeaders();
        Refresh();
        RefreshPlanLabel();
    }

    private void RefreshHeaders()
    {
        if (_predictor == null || !_predictor.IsActive) return;
        foreach (var col in Cols)
        {
            if (_headerLabels.TryGetValue(col, out var label))
            {
                string key = col switch
                {
                    ColumnType.Rare => "col_rare",
                    ColumnType.Uncommon => "col_uncommon",
                    ColumnType.Common => "col_common",
                    ColumnType.Relic => "col_relic",
                    ColumnType.CommonPotion => "col_common_potion",
                    ColumnType.RarePotion => "col_rare_potion",
                    _ => ""
                };
                string name = I18n.Tr(key);
                bool locked = _predictor.IsColumnLocked(col);
                label.Text = locked
                    ? $"[color=#33CC66]{name} [/color][color=#66FF66]🔒[/color]"
                    : $"[color=#C8D0E0]{name}[/color]";
            }
        }
    }

    // =============== Button handlers ===============

    private void OnRefreshClicked()
    {
        var predictor = CrystalSpherePredictor.Instance;
        if (predictor != null && predictor.IsActive)
        {
            // Clear all plans
            predictor.Rare.PlannedOffsets.Clear();
            predictor.Uncommon.PlannedOffsets.Clear();
            predictor.Common.PlannedOffsets.Clear();
            predictor.Relic.PlannedOffsets.Clear();
            predictor.CommonPotionColumn.PlannedOffsets.Clear();
            predictor.RarePotionColumn.PlannedOffsets.Clear();
            // Reset all dropdown filters to default
            _rareFilter.Selected = 0;
            _uncommonFilter.Selected = 0;
            _commonFilter.Selected = 0;
            _relicFilter.Selected = 0;
            _commonPotionFilter.Selected = 0;
            _rarePotionFilter.Selected = 0;
            _predictor = predictor;
        }
        Refresh();
        RefreshPlanLabel();
    }

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;

        if (_collapseBtn != null)
            _collapseBtn.Text = _collapsed ? "▲" : "▼";

        if (_collapsed)
            _fullHeight = Size.Y;

        if (_scroll != null) _scroll.Visible = !_collapsed;
        if (_headerRow != null) _headerRow.Visible = !_collapsed;
        if (_filterRow != null) _filterRow.Visible = !_collapsed;
        if (_planLabel != null) _planLabel.Visible = !_collapsed;
        if (_footer != null) _footer.Visible = !_collapsed;

        if (_collapsed)
        {
            OffsetBottom = OffsetTop + 38f;
            CustomMinimumSize = new Vector2(CustomMinimumSize.X, 0);
        }
        else
        {
            OffsetBottom = OffsetTop + _fullHeight;
            CustomMinimumSize = new Vector2(CustomMinimumSize.X, _fullHeight);
        }
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
        var normalStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
        var hoverStyle = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.1f) };
        hoverStyle.SetCornerRadiusAll(4);
        var pressedStyle = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.2f) };
        pressedStyle.SetCornerRadiusAll(4);
        btn.AddThemeStyleboxOverride("normal", normalStyle);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);
        btn.AddThemeStyleboxOverride("pressed", pressedStyle);
        btn.AddThemeStyleboxOverride("focus", normalStyle);
        return btn;
    }

    // =============== Cleanup ===============

    public override void _ExitTree()
    {
        I18n.LanguageChanged -= OnLanguageChanged;
        if (_predictor != null)
        {
            _predictor.StateChanged -= Refresh;
            _predictor.PlanChanged -= RefreshPlanLabel;
        }
        base._ExitTree();
    }
}
