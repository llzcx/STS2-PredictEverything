using System.Collections.Generic;

namespace PredictEverything;

public enum ColumnType { Rare, Uncommon, Common, Relic, CommonPotion, RarePotion }

public class ColumnState
{
    public ColumnType Type { get; }
    public int LockedAt { get; set; } = -1;
    public List<int> PlannedOffsets { get; } = new();

    public bool IsLocked => LockedAt >= 0;
    public bool HasPlan => PlannedOffsets.Count > 0;
    public int? PlannedAt => PlannedOffsets.Count > 0 ? PlannedOffsets[0] : null;
    public int MaxPlans => Type == ColumnType.CommonPotion ? 2 : 1;

    public int RngCost => Type switch
    {
        ColumnType.Rare => 6,
        ColumnType.Uncommon => 6,
        ColumnType.Common => 6,
        ColumnType.Relic => 1,
        ColumnType.CommonPotion => 1,
        ColumnType.RarePotion => 1,
        _ => 1
    };

    public string Label => Type switch
    {
        ColumnType.Rare => I18n.Tr("col_rare"),
        ColumnType.Uncommon => I18n.Tr("col_uncommon"),
        ColumnType.Common => I18n.Tr("col_common"),
        ColumnType.Relic => I18n.Tr("col_relic"),
        ColumnType.CommonPotion => I18n.Tr("col_common_potion"),
        ColumnType.RarePotion => I18n.Tr("col_rare_potion"),
        _ => "?"
    };

    public ColumnState(ColumnType type) { Type = type; }
}
