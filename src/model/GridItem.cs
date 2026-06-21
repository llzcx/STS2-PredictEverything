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
    public string DisplayLabel = "";

    public bool IsGold => Source is CrystalSphereGold;
    public bool IsPotion => Source is CrystalSpherePotion;
    public bool IsCard => Source is CrystalSphereCardReward;
    public bool IsRelic => Source is CrystalSphereRelic;
    public bool IsCurse => Source is CrystalSphereCurse;

    private static string MakeLabel(CrystalSphereItem item, ColumnType colType)
    {
        return item switch
        {
            CrystalSphereGold g =>
                ((bool?)(typeof(CrystalSphereGold).GetField("_isBig",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(g)) == true)
                    ? "大金币" : "小金币",
            CrystalSpherePotion => colType == ColumnType.RarePotion ? "金药水" : "白药水",
            CrystalSphereCardReward => "任意卡牌",
            CrystalSphereRelic => "遗物",
            CrystalSphereCurse => "诅咒",
            _ => ""
        };
    }

    public static GridItem FromItem(CrystalSphereItem item, ColumnType colType, int benefit, bool revealed)
    {
        return new GridItem
        {
            Source = item,
            ColumnType = colType,
            Position = item.Position,
            GridCost = item.Size.X * item.Size.Y,
            RngBenefit = benefit,
            IsRevealed = revealed,
            DisplayLabel = MakeLabel(item, colType)
        };
    }
}
