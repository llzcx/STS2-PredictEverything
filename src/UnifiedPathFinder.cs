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
    public static (List<(ColumnType? type, int offset, string? label, int benefit)>? path, int goldUsed) FindPath(
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
    public static (List<(ColumnType? type, int offset, string? label, int benefit)>? path, int goldCellsUsed) FindPath(
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

        var path = new List<(ColumnType? type, int offset, string? label, int benefit)>();
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
                foreach (var (fType, fBenefit, fLabel) in fillers)
                {
                    if (fType.HasValue || fLabel != null)
                        path.Add((fType, fillCur, fLabel, fBenefit));
                    fillCur += fBenefit;
                }
                cur = fillCur;
            }

            path.Add((target.ColumnType, targetOff, null, target.Benefit));
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
    private static (List<(ColumnType? type, int benefit, string? label)>? fillers,
        List<int> stoneIndices, int goldCells) FillGap(
        int gap, List<GridItem> pool, bool[] consumed,
        HashSet<ColumnType> targetTypes, int maxGold)
    {
        const int inf = int.MaxValue / 2;
        var dp = new int[gap + 1];
        for (int i = 0; i <= gap; i++) dp[i] = inf;

        dp[0] = 0;

        // Stones (0-1 knapsack for exact offset) — only real board items
        for (int si = 0; si < pool.Count; si++)
        {
            if (consumed[si]) continue;
            var stone = pool[si];
            if (!stone.IsGold && !stone.IsCurse && targetTypes.Contains(stone.ColumnType)) continue;
            int b = stone.RngBenefit;
            int c = stone.GridCost;
            if (b <= 0 || b > gap) continue;

            for (int i = gap - b; i >= 0; i--)
            {
                if (dp[i] == inf) continue;
                int nc = dp[i] + c;
                if (nc < dp[i + b])
                    dp[i + b] = nc;
            }
        }

        // Must fill the entire gap with real stones — no virtual gold
        if (dp[gap] == inf || dp[gap] > maxGold)
            return (null, new List<int>(), 0);

        // Reconstruct stone set: find unused stones that satisfy dp[off - b] + c == dp[off].
        // Avoids dpTrace aliasing where same-cost stones can appear multiple times.
        var used = new bool[pool.Count];
        var indices = new List<int>();
        int off = gap;
        while (off > 0)
        {
            bool found = false;
            for (int si = 0; si < pool.Count; si++)
            {
                if (consumed[si] || used[si]) continue;
                int b = pool[si].RngBenefit;
                int c = pool[si].GridCost;
                if (b > 0 && off >= b && dp[off - b] + c == dp[off])
                {
                    used[si] = true;
                    indices.Add(si);
                    off -= b;
                    found = true;
                    break;
                }
            }
            if (!found) break; // should not happen if dp[gap] is valid
        }
        indices.Reverse();

        // Build filler list with DisplayLabels
        var fillers = new List<(ColumnType? type, int benefit, string? label)>();
        foreach (int si in indices)
        {
            var stone = pool[si];
            string? label = string.IsNullOrEmpty(stone.DisplayLabel) ? null : stone.DisplayLabel;
            fillers.Add((null, stone.RngBenefit, label));
        }

        return (fillers, indices, dp[gap]); // goldCells = actual cell cost of stones used
    }
}
