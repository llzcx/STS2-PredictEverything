using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;

namespace PredictEverything;

/// <summary>
/// Mouse-following popup shown when hovering a CrystalSphere grid cell.
/// Predicts what clicking that cell would reveal, taking into account
/// tool type (Big 3x3 vs Small 1x1), item occupancy, and RNG offset order
/// (gold before cards/relics).
/// </summary>
public partial class HoverPopup : Control
{
    private CrystalSphereMinigame _minigame = null!;
    private CrystalSpherePredictor _predictor = null!;

    // UI elements
    private Panel _bg = null!;
    private VBoxContainer _content = null!;
    private Label _titleLabel = null!;
    private VBoxContainer _itemsContainer = null!;

    // Hover state
    private bool _shouldShow;
    private int _hoverX = -1;
    private int _hoverY = -1;

    // Content rebuild cache
    private int _cachedHoverX = -1;
    private int _cachedHoverY = -1;
    private CrystalSphereMinigame.CrystalSphereToolType _cachedTool;
    private int _cachedOffset;

    // Panel palette (matches InfoPanel / LockedDashboard aesthetic)
    private static readonly Color DeepSpaceBg = new(0.043f, 0.055f, 0.102f, 0.92f);
    private static readonly Color PanelBorder = new(0.118f, 0.141f, 0.200f, 1f);
    private static readonly Color StarWhite = new(0.784f, 0.816f, 0.878f);
    private static readonly Color Gold = new(0.722f, 0.588f, 0.290f);
    private static readonly Color WarmOrange = new(1f, 0.42f, 0.21f);
    private static readonly Color IceBlue = new(0.30f, 0.65f, 1f);
    private static readonly Color LimeGreen = new(0.29f, 0.87f, 0.50f);
    private static readonly Color DangerRed = new(1f, 0.28f, 0.28f);

    // Reflection caches
    private static readonly FieldInfo? _isBigField = typeof(CrystalSphereGold)
        .GetField("_isBig", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _rarityField = typeof(CrystalSphereCardReward)
        .GetField("_rarity", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _potionRarityField = typeof(CrystalSpherePotion)
        .GetField("_rarity", BindingFlags.NonPublic | BindingFlags.Instance);


    private const float MinPopupW = 280f;

    // ============= Public API =============

    /// <summary>
    /// Create and attach a HoverPopup to the CrystalSphere screen.
    /// Hooks into each NCrystalSphereCell's MouseEntered / MouseExited
    /// signals for hover detection. Reads the minigame entity and cell
    /// container via reflection to stay compatible with private fields.
    /// </summary>
    public static HoverPopup Create(NCrystalSphereScreen screen)
    {
        var popup = new HoverPopup();
        popup.Build();
        screen.AddChild(popup);

        var predictor = CrystalSpherePredictor.Instance!;
        popup._predictor = predictor;

        var entityField = typeof(NCrystalSphereScreen).GetField("_entity",
            BindingFlags.NonPublic | BindingFlags.Instance);
        popup._minigame = (CrystalSphereMinigame)entityField!.GetValue(screen)!;

        var cellContainerField = typeof(NCrystalSphereScreen).GetField("_cellContainer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var cellContainer = (Control)cellContainerField!.GetValue(screen)!;

        foreach (var child in cellContainer.GetChildren())
        {
            if (child is NCrystalSphereCell cell)
                popup.ConnectCell(cell);
        }

        I18n.LanguageChanged += popup.OnLanguageChanged;
        return popup;
    }

    private void ConnectCell(NCrystalSphereCell cell)
    {
        cell.Connect(Control.SignalName.MouseEntered, Callable.From(() => OnCellEnter(cell)));
        cell.Connect(Control.SignalName.MouseExited, Callable.From(() => OnCellExit()));
    }

    // ============= Build UI =============

    private void Build()
    {
        Name = "PredictEverythingHoverPopup";
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
        Visible = false;
        ZIndex = 100;

        // Manual position — set in UpdateContent
        AnchorLeft = 0; AnchorTop = 0; AnchorRight = 0; AnchorBottom = 0;

        // Background panel
        _bg = new Panel();
        _bg.SetAnchorsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat
        {
            BgColor = DeepSpaceBg,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderColor = PanelBorder,
        };
        bgStyle.SetCornerRadiusAll(8);
        _bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(_bg);

        // Content root
        _content = new VBoxContainer();
        _content.SetAnchorsPreset(LayoutPreset.FullRect);
        _content.AddThemeConstantOverride("separation", 4);
        _content.OffsetLeft = 10;
        _content.OffsetRight = -10;
        _content.OffsetTop = 6;
        _content.OffsetBottom = -8;
        AddChild(_content);

        // Title
        _titleLabel = CreateLabel(I18n.Tr("hover_title"), 13, StarWhite);
        _titleLabel.AddThemeFontSizeOverride("font_size", 13);
        _content.AddChild(_titleLabel);

        _content.AddChild(new HSeparator());

        // Item rows
        _itemsContainer = new VBoxContainer();
        _itemsContainer.AddThemeConstantOverride("separation", 2);
        _content.AddChild(_itemsContainer);
    }

    // ============= Event handlers =============

    private void OnCellEnter(NCrystalSphereCell cell)
    {
        if (_minigame.IsFinished) return;
        if (!cell.Entity.IsHidden) return;
        _hoverX = cell.Entity.X;
        _hoverY = cell.Entity.Y;
        _shouldShow = true;
        UpdateContent();
    }

    private void OnCellExit()
    {
        _shouldShow = false;
        if (Visible) Visible = false;
        _hoverX = -1;
        _hoverY = -1;
    }

    public override void _Process(double delta)
    {
        if (!_shouldShow)
        {
            if (Visible) Visible = false;
            return;
        }
        UpdateContent();
    }

    // ============= Content rebuild =============

    private void UpdateContent()
    {
        if (_predictor == null || !_predictor.IsActive) return;
        if (_hoverX < 0 || _hoverY < 0) return;

        int currentOffset = _predictor.CurrentOffset;
        var tool = _minigame.CrystalSphereTool;

        if (_hoverX == _cachedHoverX
            && _hoverY == _cachedHoverY
            && tool == _cachedTool
            && currentOffset == _cachedOffset) return;

        _cachedHoverX = _hoverX;
        _cachedHoverY = _hoverY;
        _cachedTool = tool;
        _cachedOffset = currentOffset;

        while (_itemsContainer.GetChildCount() > 0)
        {
            var child = _itemsContainer.GetChild(0);
            _itemsContainer.RemoveChild(child);
            child.QueueFree();
        }

        var entries = ComputeClickPreview(_hoverX, _hoverY);

        if (entries.Count == 0)
        {
            Visible = false;
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);

            // Reveal order index
            var idxLabel = CreateLabel($"{i + 1}.", 11,
                new Color(StarWhite.R, StarWhite.G, StarWhite.B, 0.4f));
            idxLabel.CustomMinimumSize = new Vector2(18, 0);
            row.AddChild(idxLabel);

            var typeLabel = CreateLabel(entry.TypeLabel, 11, entry.TypeColor);
            typeLabel.CustomMinimumSize = new Vector2(52, 0);
            row.AddChild(typeLabel);

            if (!string.IsNullOrEmpty(entry.Content))
            {
                var nameLabel = CreateLabel(entry.Content, 11, StarWhite);
                nameLabel.AutowrapMode = TextServer.AutowrapMode.Word;
                nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                row.AddChild(nameLabel);
            }

            _itemsContainer.AddChild(row);
        }

        // Size to content
        float h = 34f + _itemsContainer.GetChildCount() * 22f;
        OffsetRight = MinPopupW;
        OffsetBottom = h;
        CustomMinimumSize = new Vector2(MinPopupW, h);

        if (!Visible) Visible = true;

        // Position at mouse + offset, clamp to viewport (same pattern as HoverTooltip)
        var mouse = GetGlobalMousePosition();
        var vs = GetViewportRect().Size;
        Position = new Vector2(
            Mathf.Min(mouse.X + 18, vs.X - MinPopupW - 30),
            Mathf.Min(mouse.Y + 12, vs.Y - OffsetBottom - 30));
    }

    // ============= Click preview algorithm =============

    /// <summary>
    /// Simulate the game's actual ClearCell loop to determine which items
    /// would be revealed and in what order. Each cell in the tool area is
    /// "cleared" in the fixed GetAdjacentCells traversal order. When a
    /// cell's item has all its occupied cells covered (already revealed in
    /// game OR cleared by this click), the item is added to the reveal list.
    /// </summary>
    private List<PreviewEntry> ComputeClickPreview(int hx, int hy)
    {
        var entries = new List<PreviewEntry>();
        if (_minigame == null || _predictor == null || !_predictor.IsActive) return entries;

        var cell = _minigame.cells[hx, hy];
        if (!cell.IsHidden) return entries;

        // 1. Build the ordered list of cells this click would clear
        List<(int x, int y)> clearOrder;
        if (_minigame.CrystalSphereTool == CrystalSphereMinigame.CrystalSphereToolType.Big)
            clearOrder = GetAdjacentCellsOrdered(hx, hy, _minigame.GridSize.X, _minigame.GridSize.Y);
        else
            clearOrder = new List<(int, int)> { (hx, hy) };

        // 2. Simulate ClearCell for each cell in traversal order.
        //    Track which cells this click clears + which items become fully revealed.
        var clearedThisClick = new HashSet<(int, int)>();
        var processedItems = new HashSet<CrystalSphereItem>();
        var orderedItems = new List<CrystalSphereItem>();

        foreach (var (cx, cy) in clearOrder)
        {
            if (cx < 0 || cx >= _minigame.GridSize.X || cy < 0 || cy >= _minigame.GridSize.Y)
                continue;

            var gc = _minigame.cells[cx, cy];
            if (!gc.IsHidden) continue; // already revealed in game — skip

            clearedThisClick.Add((cx, cy));

            if (gc.Item != null && !processedItems.Contains(gc.Item)
                && AreAllCellsCleared(gc.Item, clearedThisClick))
            {
                processedItems.Add(gc.Item);
                orderedItems.Add(gc.Item);
            }
        }

        if (orderedItems.Count == 0) return entries;

        // 3. Count total new potions — Phase 1 ToReward consumes ALL of them
        //    before any Phase 2 Populate, regardless of reveal order position.
        int newPotionTotal = orderedItems.OfType<CrystalSpherePotion>().Count();

        // 4. Build preview entries in reveal order
        int potionSeqIdx = _predictor.RevealedPotionCount; // for frozen potion sequence
        int phase2Offset = _predictor.CardPredictionOffset + newPotionTotal;
        // Phase 1 advances RNG by newPotionTotal; Phase 2 starts from there.

        foreach (var item in orderedItems)
        {
            switch (item)
            {
                case CrystalSphereGold gold:
                {
                    bool isBig = (bool)(_isBigField?.GetValue(gold) ?? false);
                    entries.Add(new PreviewEntry
                    {
                        TypeLabel = isBig ? I18n.Tr("gold_big_label") : I18n.Tr("gold_small_label"),
                        Content = "",
                        TypeColor = Gold,
                    });
                    phase2Offset += 1; // gold Populate consumes 1 RNG
                    break;
                }

                case CrystalSpherePotion potion:
                {
                    var rarity = (PotionRarity?)(_potionRarityField?.GetValue(potion)) ?? PotionRarity.Common;
                    bool isRare = rarity == PotionRarity.Rare;
                    var seq = isRare ? _predictor.RarePotionSequence : _predictor.CommonPotionSequence;
                    string potionName = seq != null && potionSeqIdx < seq.Length
                        ? (seq[potionSeqIdx].Name ?? "?") : "?";
                    entries.Add(new PreviewEntry
                    {
                        TypeLabel = isRare ? I18n.Tr("col_rare_potion") : I18n.Tr("col_common_potion"),
                        Content = potionName,
                        TypeColor = IceBlue,
                    });
                    potionSeqIdx++;
                    break;
                }

                case CrystalSphereRelic:
                {
                    var pred = _predictor.GetPrediction(phase2Offset);
                    string relicName = pred?.Relic?.Name ?? "?";
                    entries.Add(new PreviewEntry
                    {
                        TypeLabel = I18n.Tr("col_relic"),
                        Content = relicName,
                        TypeColor = LimeGreen,
                    });
                    phase2Offset += 1;
                    break;
                }

                case CrystalSphereCardReward cardReward:
                {
                    var rarity = (CardRarity?)(_rarityField?.GetValue(cardReward)) ?? CardRarity.Common;
                    var colType = rarity switch
                    {
                        CardRarity.Rare => ColumnType.Rare,
                        CardRarity.Uncommon => ColumnType.Uncommon,
                        _ => ColumnType.Common,
                    };
                    string colLabel = colType switch
                    {
                        ColumnType.Rare => I18n.Tr("col_rare"),
                        ColumnType.Uncommon => I18n.Tr("col_uncommon"),
                        _ => I18n.Tr("col_common"),
                    };
                    Color colColor = colType switch
                    {
                        ColumnType.Rare => WarmOrange,
                        ColumnType.Uncommon => IceBlue,
                        _ => StarWhite,
                    };
                    var pred = _predictor.GetPrediction(phase2Offset);
                    var cards = pred?.GetCards(colType);
                    var cardNames = cards?.Select(c =>
                    {
                        string n = c.Name ?? "?";
                        if (c.Upgraded && !n.EndsWith("+")) n += "+";
                        return n;
                    }).ToArray() ?? new[] { "?" };
                    entries.Add(new PreviewEntry
                    {
                        TypeLabel = colLabel,
                        Content = string.Join(", ", cardNames),
                        TypeColor = colColor,
                    });
                    phase2Offset += 6;
                    break;
                }

                case CrystalSphereCurse:
                {
                    entries.Add(new PreviewEntry
                    {
                        TypeLabel = I18n.Tr("curse_label"),
                        Content = "",
                        TypeColor = DangerRed,
                    });
                    break;
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Check whether every cell occupied by <paramref name="item"/> is either
    /// already revealed in the game, or covered by <paramref name="clearedThisClick"/>.
    /// Mirrors the game's AreAllOccupiedCellsClear logic.
    /// </summary>
    private bool AreAllCellsCleared(CrystalSphereItem item, HashSet<(int, int)> clearedThisClick)
    {
        for (int i = 0; i < item.Size.X; i++)
        {
            for (int j = 0; j < item.Size.Y; j++)
            {
                int px = item.Position.X + i;
                int py = item.Position.Y + j;
                if (px < 0 || px >= _minigame.GridSize.X) return false;
                if (py < 0 || py >= _minigame.GridSize.Y) return false;
                if (!_minigame.cells[px, py].IsHidden) continue; // already revealed
                if (clearedThisClick.Contains((px, py))) continue; // cleared this click
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Get 3x3 adjacent cells in the exact game order:
    /// horizontal → vertical → diagonal → center.
    /// </summary>
    private static List<(int x, int y)> GetAdjacentCellsOrdered(int x, int y, int gridW, int gridH)
    {
        var cells = new List<(int, int)>();
        // Horizontal (left, right)
        for (int dx = -1; dx <= 1; dx += 2)
        { int nx = x + dx; if (nx >= 0 && nx < gridW) cells.Add((nx, y)); }
        // Vertical (up, down)
        for (int dy = -1; dy <= 1; dy += 2)
        { int ny = y + dy; if (ny >= 0 && ny < gridH) cells.Add((x, ny)); }
        // Diagonal (top-left, bottom-left, top-right, bottom-right)
        for (int dx = -1; dx <= 1; dx += 2)
            for (int dy = -1; dy <= 1; dy += 2)
            { int nx = x + dx; int ny = y + dy; if (nx >= 0 && nx < gridW && ny >= 0 && ny < gridH) cells.Add((nx, ny)); }
        // Center
        cells.Add((x, y));
        return cells;
    }

    // ============= I18n =============

    private void OnLanguageChanged()
    {
        if (_titleLabel != null)
            _titleLabel.Text = I18n.Tr("hover_title");

        // Force rebuild on next frame
        _cachedHoverX = -1;
        _cachedHoverY = -1;
    }

    // ============= UI helpers =============

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

    // ============= Cleanup =============

    public override void _ExitTree()
    {
        I18n.LanguageChanged -= OnLanguageChanged;
        base._ExitTree();
    }

    // ============= Data types =============

    private struct PreviewEntry
    {
        public string TypeLabel;
        public string Content;
        public Color TypeColor;
    }
}
