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
/// Floating popup shown near the mouse cursor when hovering a CrystalSphere
/// grid cell. Predicts what clicking that cell would reveal, taking into
/// account tool type (Big vs Small), adjacent-cell coverage, item overlap,
/// and the current RNG offset.
/// Follows the mouse with lerp damping for smooth movement.
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

    // Mouse tracking
    private Vector2 _targetPos;
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
    private static readonly FieldInfo? _potionField = typeof(CrystalSpherePotion)
        .GetField("_potion", BindingFlags.NonPublic | BindingFlags.Instance);

    // Mouse-follow config
    private static readonly Vector2 FollowOffset = new(16, 16);
    private const float Damping = 0.15f;
    private const float MinPopupW = 200f;

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

        // Access private fields on the screen
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
        ZIndex = 100; // render above game elements

        // No anchors — manual position + sizing
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
        // Never show popup for already-revealed cells or finished minigame
        if (!cell.Entity.IsHidden || _minigame.IsFinished) return;

        _hoverX = cell.Entity.X;
        _hoverY = cell.Entity.Y;
        _shouldShow = true;

        // Snap to mouse on first frame so lerp starts from a nearby position
        _targetPos = GetGlobalMousePosition() + FollowOffset;
        if (!Visible)
        {
            Visible = true;
            GlobalPosition = _targetPos;
        }
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

        // Follow mouse with lerp damping
        _targetPos = GetGlobalMousePosition() + FollowOffset;
        GlobalPosition = GlobalPosition.Lerp(_targetPos, Damping);

        // Refresh content when state changes
        UpdateContent();
    }

    // ============= Content rebuild =============

    private void UpdateContent()
    {
        if (_predictor == null || !_predictor.IsActive) return;
        if (_hoverX < 0 || _hoverY < 0) return;

        int currentOffset = _predictor.CurrentOffset;
        var tool = _minigame.CrystalSphereTool;

        // Avoid rebuilding every frame when nothing changed
        if (_hoverX == _cachedHoverX
            && _hoverY == _cachedHoverY
            && tool == _cachedTool
            && currentOffset == _cachedOffset) return;

        _cachedHoverX = _hoverX;
        _cachedHoverY = _hoverY;
        _cachedTool = tool;
        _cachedOffset = currentOffset;

        // Clear previous items
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

        foreach (var entry in entries)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);

            // Type tag
            var typeLabel = CreateLabel(entry.TypeLabel, 10, entry.TypeColor);
            typeLabel.CustomMinimumSize = new Vector2(48, 0);
            row.AddChild(typeLabel);

            // Item name / value
            if (!string.IsNullOrEmpty(entry.Content))
            {
                var nameLabel = CreateLabel(entry.Content, 10, StarWhite);
                nameLabel.AutowrapMode = TextServer.AutowrapMode.Word;
                nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                row.AddChild(nameLabel);
            }

            _itemsContainer.AddChild(row);
        }

        // Size popup to content
        float h = 36f + _itemsContainer.GetChildCount() * 22f;
        OffsetRight = MinPopupW;
        OffsetBottom = Mathf.Max(h, 56f);
        CustomMinimumSize = new Vector2(MinPopupW, h);
    }

    // ============= Click preview algorithm =============

    /// <summary>
    /// Compute what items would be revealed by clicking cell (hx, hy).
    /// Accounts for tool type (Big=3x3 area, Small=1 cell), multi-cell
    /// item occupancy, already-revealed cells, and current RNG offset.
    /// </summary>
    private List<PreviewEntry> ComputeClickPreview(int hx, int hy)
    {
        var entries = new List<PreviewEntry>();
        if (_minigame == null) return entries;

        // 1. Build list of covered cells in clearing order
        List<(int x, int y)> clearOrder;
        if (_minigame.CrystalSphereTool == CrystalSphereMinigame.CrystalSphereToolType.Big)
        {
            clearOrder = GetAdjacentCellsOrdered(hx, hy,
                _minigame.GridSize.X, _minigame.GridSize.Y);
        }
        else
        {
            clearOrder = new List<(int, int)> { (hx, hy) };
        }

        // 2. Determine which items will be revealed, in reveal order
        //    A multi-cell item is revealed when its LAST occupied cell is cleared.
        var clearedThisClick = new HashSet<(int, int)>();
        var processedItems = new HashSet<CrystalSphereItem>();
        var orderedItems = new List<CrystalSphereItem>();

        foreach (var (cx, cy) in clearOrder)
        {
            var cell = _minigame.cells[cx, cy];
            if (cell.IsHidden)
                clearedThisClick.Add((cx, cy));

            if (cell.Item != null && !processedItems.Contains(cell.Item)
                && AreAllCellsClearOrWillBe(cell.Item, _minigame, clearedThisClick))
            {
                processedItems.Add(cell.Item);
                orderedItems.Add(cell.Item);
            }
        }

        if (orderedItems.Count == 0) return entries;

        // 3. Build preview entries, tracking RNG offset advancement
        int goldCost = 0;


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
                    goldCost += 1; // gold consumes 1 RNG slot
                    break;
                }

                case CrystalSpherePotion:
                {
                    // Read the pre-determined potion directly from the item
                    var potionModel = (PotionModel?)_potionField?.GetValue(item);
                    string rarityLabel = potionModel?.Rarity == PotionRarity.Rare
                        ? I18n.Tr("potion_rare") : I18n.Tr("potion_common");
                    string potionName = potionModel?.Title.GetFormattedText() ?? "?";
                    entries.Add(new PreviewEntry
                    {
                        TypeLabel = rarityLabel,
                        Content = potionName,
                        TypeColor = IceBlue,
                    });
                    // Potion does NOT consume goldCost per spec
                    break;
                }

                case CrystalSphereRelic:
                {
                    int offset = _predictor.CurrentOffset + goldCost;
                    var pred = _predictor.GetPrediction(offset);
                    string relicName = pred?.Relic?.Name ?? "?";
                    entries.Add(new PreviewEntry
                    {
                        TypeLabel = I18n.Tr("col_relic"),
                        Content = relicName,
                        TypeColor = LimeGreen,
                    });
                    goldCost += 1; // relic consumes 1 RNG slot
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

                    int offset = _predictor.CurrentOffset + goldCost;
                    var pred = _predictor.GetPrediction(offset);
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
                    goldCost += 6; // card column consumes 6 RNG slots
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
                    // Curse does not consume goldCost
                    break;
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Check whether every cell occupied by the item is either already
    /// revealed in the game, or will be cleared by this click (present in
    /// <paramref name="clearedThisClick"/>).
    /// </summary>
    private static bool AreAllCellsClearOrWillBe(
        CrystalSphereItem item,
        CrystalSphereMinigame minigame,
        HashSet<(int, int)> clearedThisClick)
    {
        for (int i = 0; i < item.Size.X; i++)
        {
            for (int j = 0; j < item.Size.Y; j++)
            {
                int px = item.Position.X + i;
                int py = item.Position.Y + j;

                if (px < 0 || px >= minigame.GridSize.X) return false;
                if (py < 0 || py >= minigame.GridSize.Y) return false;

                if (!minigame.cells[px, py].IsHidden) continue; // already revealed
                if (clearedThisClick.Contains((px, py))) continue; // will be cleared

                return false; // hidden AND not covered by this click
            }
        }
        return true;
    }

    /// <summary>
    /// Get 3x3 adjacent cells in the same order as the game's
    /// GetAdjacentCells: horizontal, vertical, diagonal, center.
    /// </summary>
    private static List<(int x, int y)> GetAdjacentCellsOrdered(
        int x, int y, int gridW, int gridH)
    {
        var cells = new List<(int, int)>();

        // Horizontal neighbours (x-1, y) then (x+1, y)
        for (int dx = -1; dx <= 1; dx += 2)
        {
            int nx = x + dx;
            if (nx >= 0 && nx < gridW)
                cells.Add((nx, y));
        }

        // Vertical neighbours (x, y-1) then (x, y+1)
        for (int dy = -1; dy <= 1; dy += 2)
        {
            int ny = y + dy;
            if (ny >= 0 && ny < gridH)
                cells.Add((x, ny));
        }

        // Diagonal neighbours (x-1, y-1), (x+1, y-1), (x-1, y+1), (x+1, y+1)
        for (int dx = -1; dx <= 1; dx += 2)
        {
            for (int dy = -1; dy <= 1; dy += 2)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && nx < gridW && ny >= 0 && ny < gridH)
                    cells.Add((nx, ny));
            }
        }

        // Center cell
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
