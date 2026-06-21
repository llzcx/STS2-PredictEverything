using Godot;

namespace PredictEverything;

/// <summary>
/// Compact grid inventory entry for LockedDashboard display.
/// </summary>
public class GridInventoryEntry
{
    public string Label = "";
    public int Total;
    public int Remaining;
    public string CellSize = "";
    public int RngBenefit;
    public Color LabelColor = new(1, 1, 1);
    public string BenefitLabel => RngBenefit == 0 ? "0" : $"+{RngBenefit}";
}
