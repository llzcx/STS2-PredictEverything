using System;

namespace PredictEverything;

public class OffsetPrediction
{
    public CardPrediction[] RareCards { get; init; } = null!;
    public CardPrediction[] UncommonCards { get; init; } = null!;
    public CardPrediction[] CommonCards { get; init; } = null!;
    public RelicPrediction Relic { get; init; } = null!;
    public PotionPrediction CommonPotion { get; init; } = null!;
    public PotionPrediction RarePotion { get; init; } = null!;

    public CardPrediction[] GetCards(ColumnType col) => col switch
    {
        ColumnType.Rare => RareCards,
        ColumnType.Uncommon => UncommonCards,
        ColumnType.Common => CommonCards,
        _ => Array.Empty<CardPrediction>()
    };
}
