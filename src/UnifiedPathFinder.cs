using System;
using System.Collections.Generic;
using System.Linq;

namespace PredictEverything;

// Legacy tuple-based overload (kept for backward compat during migration)
public static partial class UnifiedPathFinder
{
    /// <summary>
    /// Legacy interface — delegates to grid-aware FindPath with auto-converted cost model.
    /// </summary>
    public static (List<(ColumnType type, int offset)>? path, int goldUsed) FindPath(
        List<(ColumnType type, int offset, int cost)> targets,
        List<(ColumnType type, int cost)> stonePool,
        int startOffset,
        int maxGold = 7)
    {
        var gridTargets = targets.Select(t => new GridTarget
        {
            Kind = TargetKind.ExactCard,
            ColumnType = t.type,
            TargetOffset = t.offset,
            Benefit = t.cost,
            Cost = t.cost >= 6 ? 4 : 1, // rough grid cost: cards=4 cells, others=1
            Label = t.type.ToString(),
        }).ToList();

        var gridStones = stonePool.Select(s => new GridItem
        {
            ColumnType = s.type,
            GridCost = s.cost >= 6 ? 4 : 1,
            RngBenefit = s.cost,
        }).ToList();

        return FindPath(gridTargets, gridStones, startOffset, maxGold);
    }
}

/// <summary>
/// Grid-aware pathfinding engine for the CrystalSphere planning system.
///
/// Models each grid item as having a real click cost (grid cells = Size.X * Size.Y)
/// and an RNG benefit (offset advancement). Uses 0-1 knapsack DP to find the
/// minimum-cell-cost set of filler stones to bridge gaps between targets.
/// Falls back to gold clicks (1 cell per offset) when stones are exhausted.
///
/// The returned path lists target/stone reveals at their offsets. Gaps between
/// consecutive entries are filled with gold clicks during display.
/// </summary>
public static partial class UnifiedPathFinder
{
    /// <summary>
    /// Compute the optimal click sequence for a set of resolved targets.
    /// </summary>
    /// <param name="targets">Pre-resolved targets, each at a specific offset with grid cost/benefit.</param>
    /// <param name="stonePool">Available non-target grid items usable as fillers.</param>
    /// <param name="startOffset">Current CardPredictionOffset.</param>
    /// <param name="maxGoldCells">Maximum gold grid cells available (default 9).</param>
    /// <returns>Path and gold cells used. path is null if infeasible.</returns>
    public static (List<(ColumnType type, int offset)>? path, int goldCellsUsed) FindPath(
        List<GridTarget> targets,
        List<GridItem> stonePool,
        int startOffset,
        int maxGoldCells = 9)
    {
        foreach (var t in targets)
        {
            if (t.TargetOffset.HasValue && t.TargetOffset.Value < startOffset)
            {
                if (PredictEverythingConfig.Instance.VerboseLogging)
                    ModLogger.Info($"        Target {t.ColumnType}@{t.TargetOffset} is behind startOffset={startOffset} — INFEASIBLE");
                return (null, 0);
            }
        }

        var sorted = targets
            .OrderBy(t => t.TargetOffset ?? int.MaxValue)
            .ToList();

        var path = new List<(ColumnType type, int offset)>();
        int cur = startOffset;
        int goldCells = 0;

        var consumedStones = new bool[stonePool.Count];
        var targetTypes = new HashSet<ColumnType>(sorted.Select(t => t.ColumnType));

        if (PredictEverythingConfig.Instance.VerboseLogging)
        {
            var tc = string.Join(", ", sorted.Select(t =>
                t.TargetOffset.HasValue ? $"{t.ColumnType}@{t.TargetOffset}" : $"{t.ColumnType}@idx{t.RevealIndex}"));
            ModLogger.Info($"        FindPath: startOffset={startOffset}, targets=[{tc}]");
        }

        foreach (var target in sorted)
        {
            int targetOff = target.TargetOffset ?? cur;

            if (cur > targetOff)
            {
                bool isPotion = target.Kind == TargetKind.FlexiblePotion || target.Kind == TargetKind.ExactPotion;
                if (isPotion)
                {
                    targetOff = cur;
                }
                else
                {
                    if (PredictEverythingConfig.Instance.VerboseLogging)
                        ModLogger.Info($"        cur={cur} > targetOff={targetOff} — INFEASIBLE");
                    return (null, goldCells);
                }
            }

            if (cur < targetOff)
            {
                int gap = targetOff - cur;
                var (fillers, stoneIndices, fillGoldCells) = FillGap(gap, stonePool, consumedStones,
                    targetTypes, maxGoldCells - goldCells);

                if (fillers == null)
                {
                    if (PredictEverythingConfig.Instance.VerboseLogging)
                        ModLogger.Info($"        FillGap FAILED (gap={gap}, maxGoldRemaining={maxGoldCells - goldCells})");
                    return (null, goldCells);
                }

                goldCells += fillGoldCells;
                foreach (int si in stoneIndices)
                    consumedStones[si] = true;

                int fillCur = cur;
                foreach (var (fType, fBenefit) in fillers)
                {
                    if (fType != null)
                        path.Add((fType.Value, fillCur));
                    fillCur += fBenefit;
                }
                cur = fillCur;
            }

            path.Add((target.ColumnType, targetOff));
            cur = targetOff + target.Benefit;
        }

        if (PredictEverythingConfig.Instance.VerboseLogging)
            ModLogger.Info($"        → DONE goldCells={goldCells} pathLen={path.Count} endOffset={cur}");

        return (path, goldCells);
    }

    /// <summary>
    /// Fill an RNG gap using the cheapest combination of stones + gold clicks.
    /// 0-1 knapsack DP: dp[i] = min grid cells to reach offset i via stones.
    /// Then find i + goldFill(i→gap) with minimum total cell cost.
    /// </summary>
    private static (List<(ColumnType? type, int benefit)>? fillers,
        List<int> stoneIndices, int goldCells) FillGap(
        int gap, List<GridItem> pool, bool[] consumed,
        HashSet<ColumnType> targetTypes, int maxGold)
    {
        const int inf = int.MaxValue / 2;
        var dp = new int[gap + 1];
        var dpGold = new int[gap + 1];
        var dpTrace = new (int prevOff, int stoneIdx)[gap + 1];
        for (int i = 0; i <= gap; i++) dp[i] = inf;

        dp[0] = 0;
        dpGold[0] = 0;
        dpTrace[0] = (-1, -1);

        // Stones (0-1 knapsack for exact offset)
        for (int si = 0; si < pool.Count; si++)
        {
            if (consumed[si]) continue;
            var stone = pool[si];
            if (targetTypes.Contains(stone.ColumnType)) continue;
            int b = stone.RngBenefit;
            int c = stone.GridCost;
            if (b <= 0 || b > gap) continue;

            for (int i = gap - b; i >= 0; i--)
            {
                if (dp[i] == inf) continue;
                int nc = dp[i] + c;
                if (nc < dp[i + b])
                {
                    dp[i + b] = nc;
                    dpGold[i + b] = dpGold[i];
                    dpTrace[i + b] = (i, si);
                }
            }
        }

        // Gold fill: for each reachable offset i, fill remaining with gold
        int bestCost = inf;
        int bestGold = 0;
        int bestEnd = -1;
        for (int i = 0; i <= gap; i++)
        {
            if (dp[i] == inf) continue;
            int remaining = gap - i;
            if (remaining > maxGold) continue;
            int totalGold = dpGold[i] + remaining;
            int totalCost = dp[i] + remaining; // gold costs 1 cell per offset
            if (totalCost < bestCost)
            {
                bestCost = totalCost;
                bestGold = totalGold;
                bestEnd = i;
            }
        }

        if (bestEnd < 0)
            return (null, new List<int>(), 0);

        // Backtrack stone indices
        var indices = new List<int>();
        int off = bestEnd;
        while (off > 0 && dpTrace[off].stoneIdx >= 0)
        {
            indices.Add(dpTrace[off].stoneIdx);
            off = dpTrace[off].prevOff;
        }
        indices.Reverse();

        // Build filler list: stones first (in DP order), then gold fills.
        // Gold grid items are treated as virtual gold (null type) — they
        // don't appear as named columns in the path, just advance offset.
        var fillers = new List<(ColumnType? type, int benefit)>();
        foreach (int si in indices)
        {
            var stone = pool[si];
            fillers.Add((stone.IsGold ? null : stone.ColumnType, stone.RngBenefit));
        }

        // Gold fills (null type = gold, used only for offset tracking in caller)
        int stoneOffset = indices.Sum(si => pool[si].RngBenefit);
        int goldRemaining = gap - stoneOffset;
        for (int i = 0; i < goldRemaining; i++)
            fillers.Add((null, 1));

        return (fillers, indices, bestGold);
    }
}
