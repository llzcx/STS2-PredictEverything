using System;
using System.Collections.Generic;
using System.Linq;

namespace PredictEverything;

/// <summary>
/// Unified pathfinding engine for the CrystalSphere planning system.
/// Given a set of required targets (each at a specific offset with a known RNG cost)
/// and a pool of optional stepping stones, computes the optimal click sequence
/// that minimizes gold expenditure (capped at 7).
///
/// Algorithm: Sorts targets by offset ascending, then walks forward from
/// startOffset. Between each pair of consecutive targets, the gap is filled
/// by greedily selecting the largest stepping stone that fits (preferring
/// cards over relics/potions), then using gold clicks for the remainder.
/// If total gold exceeds maxGold (7), the path is infeasible.
/// </summary>
public static class UnifiedPathFinder
{
    /// <summary>
    /// Compute the optimal click order for a set of targets at specific offsets.
    /// </summary>
    /// <param name="targets">
    /// Required targets, each at a specific offset with known RNG cost.
    /// Must be pre-resolved — each target represents "reveal column T at offset O".
    /// </param>
    /// <param name="stonePool">
    /// Available stepping stones. Each entry is (type, cost) and can be used
    /// at most once. Stones can be revealed at any offset; the algorithm places
    /// them in gaps between targets to reduce gold expenditure.
    /// </param>
    /// <param name="startOffset">Current CardPredictionOffset.</param>
    /// <param name="maxGold">Maximum gold clicks allowed (default 7).</param>
    /// <returns>
    /// Tuple of (path, goldUsed). path is null if infeasible.
    /// Each path entry is (columnType, offsetWhereRevealed).
    /// </returns>
    public static (List<(ColumnType type, int offset)>? path, int goldUsed) FindPath(
        List<(ColumnType type, int offset, int cost)> targets,
        List<(ColumnType type, int cost)> stonePool,
        int startOffset,
        int maxGold = 7)
    {
        // Validate: no target should be behind startOffset
        foreach (var t in targets)
        {
            if (t.offset < startOffset)
                return (null, 0);
        }

        // Sort targets by offset ascending
        targets = targets.OrderBy(t => t.offset).ThenByDescending(t => t.cost).ToList();

        var path = new List<(ColumnType type, int offset)>();
        int cur = startOffset;
        int gold = 0;

        // Track which stone pool entries are consumed (by index)
        var consumedStones = new bool[stonePool.Count];
        // Track which column types are consumed as targets (can't also be stones)
        var targetTypes = new HashSet<ColumnType>(targets.Select(t => t.type));

        foreach (var target in targets)
        {
            int targetOff = target.offset;

            // Check if we overshot (stones from a previous gap may have pushed past)
            if (cur > targetOff)
                return (null, gold);

            // Fill the gap between cur and targetOff with stones, then gold
            while (cur < targetOff)
            {
                int gap = targetOff - cur;
                bool foundStone = false;

                // Greedy: try largest fitting stone first (cards=6, then relics/potions=1)
                for (int si = 0; si < stonePool.Count; si++)
                {
                    if (consumedStones[si]) continue;
                    var stone = stonePool[si];
                    if (targetTypes.Contains(stone.type)) continue; // stone is itself a target
                    if (stone.cost <= gap)
                    {
                        consumedStones[si] = true;
                        path.Add((stone.type, cur));
                        cur += stone.cost;
                        foundStone = true;
                        break;
                    }
                }

                if (!foundStone)
                {
                    // No stone fits — use a gold click
                    cur += 1;
                    gold += 1;
                    if (gold > maxGold)
                        return (null, gold);
                }
            }

            // cur == targetOff: reveal the target
            path.Add((target.type, targetOff));
            cur = targetOff + target.cost;
        }

        return (path, gold);
    }
}
