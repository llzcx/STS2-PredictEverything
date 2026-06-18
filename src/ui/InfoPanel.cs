using System.Collections.Generic;
using System.Linq;
using Godot;

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

    private bool _collapsed;
    private bool _dragging;
    private Vector2 _dragStart;

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
        { ColumnType.Rare, ColumnType.Uncommon, ColumnType.Common, ColumnType.Relic };

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
        return panel;
    }

    // =============== Build ===============

    private void Build()
    {
        Name = "PredictEverythingInfoPanel";
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);

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

        var title = CreateLocalizedLabel("panel_title", 22, StarWhite);
        title.AddThemeFontSizeOverride("font_size", 22);
        _titleBar.AddChild(title);

        _titleBar.AddChild(new Control
        {
            CustomMinimumSize = new Vector2(10, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        });

        // Refresh button
        var refreshBtn = CreateIconButton("↻", 16);
        refreshBtn.Pressed += OnRefreshClicked;
        _titleBar.AddChild(refreshBtn);

        // Collapse button
        var collapseBtn = CreateIconButton("✕", 16);
        collapseBtn.Pressed += ToggleCollapse;
        _titleBar.AddChild(collapseBtn);

        _titleBar.GuiInput += OnTitleBarGuiInput;
        _root.AddChild(_titleBar);

        // ---- Column headers ----
        _headerRow = new HBoxContainer();
        _headerRow.AddThemeConstantOverride("separation", 4);

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
                _ => ""
            };
            var headerLabel = new RichTextLabel();
            headerLabel.BbcodeEnabled = true;
            headerLabel.FitContent = true;
            headerLabel.ScrollActive = false;
            headerLabel.HorizontalAlignment = HorizontalAlignment.Center;
            headerLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            headerLabel.AddThemeFontSizeOverride("normal_font_size", 16);
            headerLabel.AddThemeColorOverride("default_color", StarWhite);
            headerLabel.MouseFilter = MouseFilterEnum.Pass;
            _headerLabels[col] = headerLabel;
            _i18nRegistry.Add((headerLabel, key));
            _headerRow.AddChild(headerLabel);
        }
        _root.AddChild(_headerRow);
        RefreshHeaders();

        // Header separator
        _root.AddChild(new HSeparator());

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

        // ---- Bottom plan summary ----
        _root.AddChild(new HSeparator());

        _planLabel = new RichTextLabel();
        _planLabel.BbcodeEnabled = true;
        _planLabel.FitContent = true;
        _planLabel.ScrollFollowing = false;
        _planLabel.ScrollActive = false;
        _planLabel.MouseFilter = MouseFilterEnum.Pass;
        _planLabel.AddThemeFontSizeOverride("normal_font_size", 16);
        _planLabel.AddThemeColorOverride("default_color", StarWhite);
        _root.AddChild(_planLabel);

        // ---- Footer legend ----
        _root.AddChild(new HSeparator());
        var footer = new VBoxContainer();
        footer.AddThemeConstantOverride("separation", 2);

        var legend1 = new RichTextLabel();
        legend1.BbcodeEnabled = true;
        legend1.FitContent = true;
        legend1.ScrollActive = false;
        legend1.Text = $"[color=#66FF66][b]✦[/b][/color] = {I18n.Tr("legend_upgraded")}";
        legend1.AddThemeFontSizeOverride("normal_font_size", 11);
        legend1.AddThemeColorOverride("default_color", new Color(0.6f, 0.6f, 0.6f));
        footer.AddChild(legend1);

        var legend2 = new RichTextLabel();
        legend2.BbcodeEnabled = true;
        legend2.FitContent = true;
        legend2.ScrollActive = false;
        legend2.Text = I18n.Tr("legend_costs");
        legend2.AddThemeFontSizeOverride("normal_font_size", 11);
        legend2.AddThemeColorOverride("default_color", new Color(0.6f, 0.6f, 0.6f));
        footer.AddChild(legend2);

        var legend3 = new RichTextLabel();
        legend3.BbcodeEnabled = true;
        legend3.FitContent = true;
        legend3.ScrollActive = false;
        legend3.Text = I18n.Tr("legend_order");
        legend3.AddThemeFontSizeOverride("normal_font_size", 11);
        legend3.AddThemeColorOverride("default_color", new Color(0.6f, 0.6f, 0.6f));
        footer.AddChild(legend3);

        _root.AddChild(footer);

        // ---- Size and positioning ----
        float w = config.PanelW > 0 ? config.PanelW : 500f;
        float h = config.PanelH > 0 ? config.PanelH : 600f;
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

        // Initial data population
        Refresh();
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

        // Clear existing rows
        while (_rowsContainer.GetChildCount() > 0)
        {
            var child = _rowsContainer.GetChild(0);
            _rowsContainer.RemoveChild(child);
            child.QueueFree();
        }
        int currentOffset = _predictor.CurrentOffset;

        for (int row = 0; row <= 26; row++)
        {
            var pred = _predictor.Predictions[row];
            if (pred == null) continue;

            var rowHbox = new HBoxContainer();
            rowHbox.AddThemeConstantOverride("separation", 2);
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

            // Apply row background via PanelContainer
            if (rowColor.A > 0f)
            {
                var wrapper = new PanelContainer();
                wrapper.MouseFilter = MouseFilterEnum.Pass;
                wrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                var wrapperStyle = new StyleBoxFlat
                {
                    BgColor = rowColor,
                    ContentMarginLeft = 2,
                    ContentMarginRight = 2,
                    ContentMarginTop = 1,
                    ContentMarginBottom = 1
                };
                wrapperStyle.SetCornerRadiusAll(3);
                // Add border for planned rows
                if (IsRowPlanned(row))
                {
                    wrapperStyle.BorderWidthLeft = 2;
                    wrapperStyle.BorderWidthRight = 2;
                    wrapperStyle.BorderWidthTop = 2;
                    wrapperStyle.BorderWidthBottom = 2;
                    wrapperStyle.BorderColor = PlannedBorder;
                }
                wrapper.AddThemeStyleboxOverride("panel", wrapperStyle);
                rowHbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                wrapper.AddChild(rowHbox);
                _rowsContainer.AddChild(wrapper);
            }
            else
            {
                rowHbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                _rowsContainer.AddChild(rowHbox);
            }
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
        bool colLocked = _predictor.IsColumnLocked(col);
        bool isLockedCell = IsColumnLockedAt(col, row);
        bool isPlannedCell = !colLocked && _predictor.IsColumnPlannedAt(col, row);
        Control cell;
        if (isLockedCell || isPlannedCell)
        {
            var wrapper = new PanelContainer();
            wrapper.MouseFilter = MouseFilterEnum.Stop;
            wrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            Color bgColor, borderColor;
            if (isLockedCell)
            {
                bgColor = LockedColor;
                borderColor = new Color(0f, 0.7f, 0f, 0.6f);
            }
            else
            {
                bgColor = CellPlannedBg;
                borderColor = new Color(1f, 0.6f, 0f, 0.8f);
            }
            var cellBg = new StyleBoxFlat { BgColor = bgColor };
            cellBg.SetCornerRadiusAll(4);
            cellBg.BorderWidthLeft = 2;
            cellBg.BorderWidthRight = 2;
            cellBg.BorderWidthTop = 2;
            cellBg.BorderWidthBottom = 2;
            cellBg.BorderColor = borderColor;
            wrapper.AddThemeStyleboxOverride("panel", cellBg);
            inner.MouseFilter = MouseFilterEnum.Ignore; // let click pass to wrapper
            wrapper.AddChild(inner);
            cell = wrapper;
        }
        else
        {
            cell = inner;
        }

        if (col == ColumnType.Relic)
        {
            var relic = pred.Relic;
            string relicName = relic?.Name ?? "?";
            var relicLabel = CreateLabel(relicName, 15, StarWhite);
            relicLabel.HorizontalAlignment = HorizontalAlignment.Center;
            relicLabel.AutowrapMode = TextServer.AutowrapMode.Word;
            relicLabel.MouseFilter = MouseFilterEnum.Pass;

            // Hover relic → show relic tooltip
            var capturedRelic = relic;
            relicLabel.MouseEntered += () => HoverTooltip.ShowRelic(capturedRelic);
            relicLabel.MouseExited += () => HoverTooltip.Hide();
            inner.AddChild(relicLabel);
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
                    rtl.AddThemeFontSizeOverride("normal_font_size", 14);
                    rtl.AddThemeFontSizeOverride("bold_font_size", 14);
                    cardLabel = rtl;
                }
                else
                {
                    var lbl = CreateLabel(cardName, 14,
                        new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.85f));
                    lbl.HorizontalAlignment = HorizontalAlignment.Center;
                    lbl.AutowrapMode = TextServer.AutowrapMode.Word;
                    lbl.ClipContents = true;
                    lbl.MouseFilter = MouseFilterEnum.Pass;
                    cardLabel = lbl;
                }

                // Hover card → show card tooltip near mouse
                var capturedCard = card;
                cardLabel.MouseEntered += () => HoverTooltip.ShowCard(capturedCard);
                cardLabel.MouseExited += () => HoverTooltip.Hide();
                inner.AddChild(cardLabel);
            }
        }

        // Click handler: toggle plan for this column at this row
        int capturedRow = row;
        ColumnType capturedCol = col;
        cell.GuiInput += (InputEvent e) =>
        {
            if (e is InputEventMouseButton mb &&
                mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                if (_predictor.IsColumnLocked(capturedCol)) return;
                if (capturedRow < _predictor.CurrentOffset) return;
                _predictor.TogglePlan(capturedCol, capturedRow);
                Refresh();  // Rebuild rows to update colors
            }
        };

        return cell;
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
        else if (sequence == I18n.Tr("plan_all_resolved"))
        {
            _planLabel.Text = $"[color=#7DE2C8]{sequence}[/color]";
        }
        else
        {
            int stepCount = 1;
            for (int i = 0; i < sequence.Length; i++)
                if (sequence[i] == '→') stepCount++; // count arrow separators
            _planLabel.Text = $"[b]{I18n.Tr("plan_prefix")}[/b]: {stepCount} {I18n.Tr("plan_clicks")} | {sequence}";
        }
    }

    // =============== Row state helpers ===============

    private bool IsColumnLockedAt(ColumnType col, int row) => col switch
    {
        ColumnType.Rare => _predictor.Rare.LockedAt == row,
        ColumnType.Uncommon => _predictor.Uncommon.LockedAt == row,
        ColumnType.Common => _predictor.Common.LockedAt == row,
        ColumnType.Relic => _predictor.Relic.LockedAt == row,
        _ => false
    };

    private bool IsRowLocked(int row)
    {
        return _predictor.Rare.LockedAt == row ||
               _predictor.Uncommon.LockedAt == row ||
               _predictor.Common.LockedAt == row ||
               _predictor.Relic.LockedAt == row;
    }

    private bool IsRowPlanned(int row)
    {
        return _predictor.Rare.PlannedAt == row ||
               _predictor.Uncommon.PlannedAt == row ||
               _predictor.Common.PlannedAt == row ||
               _predictor.Relic.PlannedAt == row;
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

    // =============== Drag handling ===============

    private void OnTitleBarGuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb &&
            mb.ButtonIndex == MouseButton.Left &&
            mb.Pressed)
        {
            _dragging = true;
            _dragStart = GetGlobalMousePosition() - GlobalPosition;
        }
    }

    public override void _Process(double delta)
    {
        if (!_dragging) return;

        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            GlobalPosition = GetGlobalMousePosition() - _dragStart;
        }
        else
        {
            // Mouse released -- end drag and persist position
            _dragging = false;

            // Convert to manual (top-left) anchoring
            var gpos = GlobalPosition;
            float w = OffsetRight - OffsetLeft;
            float h = OffsetBottom - OffsetTop;
            AnchorLeft = 0;
            AnchorTop = 0;
            AnchorRight = 0;
            AnchorBottom = 0;
            OffsetLeft = gpos.X;
            OffsetTop = gpos.Y;
            OffsetRight = gpos.X + w;
            OffsetBottom = gpos.Y + h;

            var config = PredictEverythingConfig.Instance;
            config.PanelX = gpos.X;
            config.PanelY = gpos.Y;
            PredictEverythingConfig.Save();
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
            predictor.Rare.PlannedAt = null;
            predictor.Uncommon.PlannedAt = null;
            predictor.Common.PlannedAt = null;
            predictor.Relic.PlannedAt = null;
            _predictor = predictor;
        }
        Refresh();
        RefreshPlanLabel();
    }

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;

        // Hide/show content area
        if (_scroll != null)
        {
            _scroll.Visible = !_collapsed;
        }
        if (_headerRow != null)
        {
            _headerRow.Visible = !_collapsed;
        }
        if (_planLabel != null)
        {
            _planLabel.Visible = !_collapsed;
        }

        // Adjust panel height
        float h = _collapsed ? 38f : (PredictEverythingConfig.Instance.PanelH > 0
            ? PredictEverythingConfig.Instance.PanelH : 520f);
        OffsetBottom = OffsetTop + h;
        CustomMinimumSize = new Vector2(CustomMinimumSize.X, _collapsed ? 0 : 520);
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
        {
            _predictor.StateChanged -= Refresh;
            _predictor.PlanChanged -= RefreshPlanLabel;
        }
        base._ExitTree();
    }
}
