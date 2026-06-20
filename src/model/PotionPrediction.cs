using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;

namespace PredictEverything;

public record PotionPrediction(string Name, PotionModel? Potion)
{
    public PotionRarity Rarity => Potion?.Rarity ?? PotionRarity.Common;
}
