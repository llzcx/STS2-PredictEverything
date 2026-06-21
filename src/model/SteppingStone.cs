namespace PredictEverything;

/// <summary>
/// Unified stepping stone abstraction.
/// Every unrevealed item is modeled as a stepping stone with a cost (estimated clicks)
/// and a benefit (RNG offset advancement). The algorithm picks the cheapest stone
/// with the same benefit to fill gaps between targets.
/// </summary>
public class SteppingStone
{
    public int Cost { get; }
    public int Benefit { get; }
    public string GenericLabel { get; }
    public bool IsTarget { get; }

    public SteppingStone(int cost, int benefit, string genericLabel, bool isTarget)
    {
        Cost = cost;
        Benefit = benefit;
        GenericLabel = genericLabel;
        IsTarget = isTarget;
    }
}
