using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;

namespace PredictEverything;

public enum TargetKind
{
    /// <summary>Card target: must be at a specific RNG offset.</summary>
    ExactCard,
    /// <summary>Relic target: must be at a specific RNG offset.</summary>
    ExactRelic,
    /// <summary>Flexible potion prerequisite: any unrevealed potion works, cheapest first.</summary>
    FlexiblePotion,
    /// <summary>Exact potion: Nth reveal must match a specific frozen-sequence name.</summary>
    ExactPotion
}

/// <summary>
/// A planning target — either user-selected (card/relic/exact potion) or
/// auto-generated prerequisite (flexible potion).
/// </summary>
public class GridTarget
{
    public TargetKind Kind;
    public ColumnType ColumnType;
    public CrystalSphereItem? GridItem;
    public int Cost;
    public int Benefit;
    /// <summary>For potions: global reveal index (0-based across all potion reveals).</summary>
    public int RevealIndex;
    /// <summary>For cards/relics: the RNG offset where this item must be revealed.</summary>
    public int? TargetOffset;
    /// <summary>For ExactPotion: the required potion rarity.</summary>
    public PotionRarity? PotionRarity;
    /// <summary>For ExactPotion: the frozen-sequence name that must match.</summary>
    public string? MustMatchName;
    /// <summary>Display name for path output.</summary>
    public string Label = "";
}
