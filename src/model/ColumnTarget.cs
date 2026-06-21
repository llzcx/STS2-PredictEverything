namespace PredictEverything;

/// <summary>
/// Algorithm target: a column at a specific offset selected by the user
/// via dropdown filter or manual row click.
/// </summary>
public record ColumnTarget(
    ColumnType Type,
    int TargetOffset,
    int RngCost,
    string DisplayName
);
