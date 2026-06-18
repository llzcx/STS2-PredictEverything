namespace PredictEverything;

public enum ColumnType { Rare, Uncommon, Common, Relic }

public class ColumnState
{
    public ColumnType Type { get; }
    public int LockedAt { get; set; } = -1;
    public int? PlannedAt { get; set; } = null;

    public bool IsLocked => LockedAt >= 0;
    public bool HasPlan => PlannedAt.HasValue;
    public int RngCost => Type switch
    {
        ColumnType.Rare => 6,
        ColumnType.Uncommon => 6,
        ColumnType.Common => 6,
        ColumnType.Relic => 1,
        _ => 1
    };

    public string Label => Type switch
    {
        ColumnType.Rare => I18n.Tr("col_rare"),
        ColumnType.Uncommon => I18n.Tr("col_uncommon"),
        ColumnType.Common => I18n.Tr("col_common"),
        ColumnType.Relic => I18n.Tr("col_relic"),
        _ => "?"
    };

    public ColumnState(ColumnType type) { Type = type; }
}
