using System.Linq;
using MegaCrit.Sts2.Core.Entities.Potions;

namespace PredictEverything;

public class PotionColumnData
{
    public int TotalCount;
    public int RevealedCount;
    public bool[] Revealed = null!;
    public string[] Names = null!;
    public PotionPrediction?[] Predictions = null!;
    public int[] LockedAt = null!;
    public PotionRarity[] Rarities = null!;

    public int UnrevealedCommonCount
    {
        get
        {
            int c = 0;
            for (int i = 0; i < TotalCount; i++)
                if (!Revealed[i] && Rarities[i] == PotionRarity.Common)
                    c++;
            return c;
        }
    }

    public int UnrevealedRareCount
    {
        get
        {
            int c = 0;
            for (int i = 0; i < TotalCount; i++)
                if (!Revealed[i] && Rarities[i] == PotionRarity.Rare)
                    c++;
            return c;
        }
    }

    public bool IsLockedAt(int row) => LockedAt.Contains(row);

    public bool IsRevealed(int index) => index >= 0 && index < TotalCount && Revealed[index];
}
