using Godot;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;

namespace PredictEverything;

/// <summary>
/// Represents a physical item on the CrystalSphere grid.
/// Cost = grid cells (Size.X * Size.Y), Benefit = RNG offset advancement.
/// </summary>
public class GridItem
{
    public CrystalSphereItem Source = null!;
    public ColumnType ColumnType;
    public Vector2I Position;
    public int GridCost;
    public int RngBenefit;
    public bool IsRevealed;

    public bool IsGold => Source is CrystalSphereGold;
    public bool IsPotion => Source is CrystalSpherePotion;
    public bool IsCard => Source is CrystalSphereCardReward;
    public bool IsRelic => Source is CrystalSphereRelic;
    public bool IsCurse => Source is CrystalSphereCurse;

    public static GridItem FromItem(CrystalSphereItem item, ColumnType colType, int benefit, bool revealed)
    {
        return new GridItem
        {
            Source = item,
            ColumnType = colType,
            Position = item.Position,
            GridCost = item.Size.X * item.Size.Y,
            RngBenefit = benefit,
            IsRevealed = revealed
        };
    }
}
