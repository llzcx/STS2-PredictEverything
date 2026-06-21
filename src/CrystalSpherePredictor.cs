using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace PredictEverything;

public class CrystalSpherePredictor
{
    public static CrystalSpherePredictor? Instance { get; private set; }

    // RNG state
    private uint _rngSeed;
    private int _rngBaseCounter;
    public int BaseCounter => _rngBaseCounter;

    // Player info
    private Player _player = null!;
    private int _actIndex;
    private decimal _upgradeScaling;

    // Egg relics
    private bool _hasEggAttack;
    private bool _hasEggSkill;
    private bool _hasEggPower;

    // Card pools
    private List<CardModel> _rarePool = null!;
    private List<CardModel> _uncommonPool = null!;
    private List<CardModel> _commonPool = null!;

    // Potion predictions — updated dynamically on reveal
    private CrystalSpherePotion[] _potionItems = null!;
    private PotionModel[] _cachedCommonOptions = null!;
    private PotionModel[] _cachedRareOptions = null!;
    /// <summary>Centralized potion tracking (replaces scattered _potionNames, _potionPredictions, PotionRevealed).</summary>
    public PotionColumnData PotionData { get; private set; } = null!;

    // Frozen potion sequences: counter baseCounter+0, +1, +2 for common; +0, +1 for rare
    public PotionPrediction[] CommonPotionSequence { get; private set; } = null!;
    public PotionPrediction[] RarePotionSequence { get; private set; } = null!;

    // Precomputed predictions
    public OffsetPrediction[] Predictions { get; private set; } = null!;
    public const int MaxOffset = 30; // max CardPredictionOffset: 3P+7G+1R+18(3cards)=29, rounded up
    public bool IsActive => Predictions != null;

    // State tracking — counts items revealed so far (for Populate RNG tracking)
    private int _goldsRevealed;
    private int _cardColumnsRevealed;
    private int _relicsRevealed;
    public int CurrentOffset { get; private set; } = 0;
    public ColumnState Rare { get; } = new(ColumnType.Rare);
    public ColumnState Uncommon { get; } = new(ColumnType.Uncommon);
    public ColumnState Common { get; } = new(ColumnType.Common);
    public ColumnState Relic { get; } = new(ColumnType.Relic);
    public ColumnState CommonPotionColumn { get; } = new(ColumnType.CommonPotion);
    public ColumnState RarePotionColumn { get; } = new(ColumnType.RarePotion);
    public int TotalPotionCount => PotionData?.TotalCount ?? 0;
    public int RevealedPotionCount => PotionData?.RevealedCount ?? 0;
    public int UnrevealedCommonCount => PotionData?.UnrevealedCommonCount ?? 0;
    public int UnrevealedRareCount => PotionData?.UnrevealedRareCount ?? 0;
    public bool[] PotionRevealed => PotionData?.Revealed ?? System.Array.Empty<bool>();
    public Dictionary<(string n, bool u), int> RareCardMap { get; private set; } = null!;
    public Dictionary<(string n, bool u), int> UncommonCardMap { get; private set; } = null!;
    public Dictionary<(string n, bool u), int> CommonCardMap { get; private set; } = null!;
    public Dictionary<string, int> RelicMap { get; private set; } = null!;
    public Dictionary<string, int> CommonPotionMap { get; private set; } = null!;
    public Dictionary<string, int> RarePotionMap { get; private set; } = null!;
    public List<(string n, bool u)> RareCardList { get; private set; } = null!;
    public List<(string n, bool u)> UncommonCardList { get; private set; } = null!;
    public List<(string n, bool u)> CommonCardList { get; private set; } = null!;
    public List<string> RelicList { get; private set; } = null!;

    // Events
    public event Action? StateChanged;
    public event Action? PlanChanged;

    // Reflection cache
    private static readonly FieldInfo? _dequesField = typeof(RelicGrabBag)
        .GetField("_deques", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly PropertyInfo? _upgradeScalingProp = typeof(CardFactory)
        .GetProperty("UpgradedCardOddScaling", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo? _rarityField = typeof(CrystalSphereCardReward)
        .GetField("_rarity", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _potionRarityField = typeof(CrystalSpherePotion)
        .GetField("_rarity", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _isBigField = typeof(CrystalSphereGold)
        .GetField("_isBig", BindingFlags.NonPublic | BindingFlags.Instance);

    // Minigame reference for grid-aware pathfinding
    private CrystalSphereMinigame _minigame = null!;

    // =============== Public API ===============

    public void Initialize(Rng rng, Player player, CrystalSphereMinigame minigame)
    {
        Instance = this;
        _rngSeed = rng.Seed;
        _rngBaseCounter = rng.Counter;
        _player = player;
        _minigame = minigame;
        _actIndex = player.RunState.CurrentActIndex;
        _upgradeScaling = (decimal)(_upgradeScalingProp?.GetValue(null) ?? 0.125m);

        // Check egg relics
        _hasEggAttack = player.Relics.Any(r => r is MoltenEgg);
        _hasEggSkill = player.Relics.Any(r => r is ToxicEgg);
        _hasEggPower = player.Relics.Any(r => r is FrozenEgg);

        // Build card pools
        _rarePool = BuildCardPool(player, CardRarity.Rare);
        _uncommonPool = BuildCardPool(player, CardRarity.Uncommon);
        _commonPool = BuildCardPool(player, CardRarity.Common);

        if (_rarePool == null || _uncommonPool == null || _commonPool == null)
        {
            Predictions = null!;
            ModLogger.Warn("Card pool build failed — predictions disabled");
            return;
        }

        // v0.107.1: Potions selected at reveal time via ToReward → rng.NextItem().
        // Cache rarity-filtered pools at init so predictions are deterministic.
        _potionItems = minigame.Items.OfType<CrystalSpherePotion>().ToArray();
        var totalPotionCount = _potionItems.Length;
        _cachedCommonOptions = PotionFactory.GetPotionOptions(_player, System.Array.Empty<PotionModel>())
            .Where(p => p.Rarity == PotionRarity.Common).ToArray();
        _cachedRareOptions = PotionFactory.GetPotionOptions(_player, System.Array.Empty<PotionModel>())
            .Where(p => p.Rarity == PotionRarity.Rare).ToArray();
        ModLogger.Info($"Potion options cached: common={_cachedCommonOptions.Length}, rare={_cachedRareOptions.Length}");
        var potionNames = new string[totalPotionCount];
        var potionPredictions = new PotionPrediction?[totalPotionCount];
        var potionRarities = new PotionRarity[totalPotionCount];
        for (int i = 0; i < totalPotionCount; i++)
        {
            var item = _potionItems[i];
            var rarity = (PotionRarity?)_potionRarityField?.GetValue(item) ?? PotionRarity.Common;
            potionRarities[i] = rarity;
            var prng = new Rng(_rngSeed, _rngBaseCounter + i);
            var options = rarity == PotionRarity.Rare ? _cachedRareOptions : _cachedCommonOptions;
            var potion = prng.NextItem(options);
            string name = potion?.Title?.GetFormattedText() ?? "?";
            potionNames[i] = name;
            potionPredictions[i] = new PotionPrediction(name, potion);
        }
        PotionData = new PotionColumnData
        {
            TotalCount = totalPotionCount,
            RevealedCount = 0,
            Revealed = new bool[totalPotionCount],
            Names = potionNames,
            Predictions = potionPredictions,
            Rarities = potionRarities,
            LockedAt = Enumerable.Repeat(-1, totalPotionCount).ToArray()
        };
        ModLogger.Info($"Potion init: common=[{string.Join(", ", potionNames.Where((_, i) => potionRarities[i] == PotionRarity.Common))}] rare=[{string.Join(", ", potionNames.Where((_, i) => potionRarities[i] == PotionRarity.Rare))}]");

        // Frozen potion sequences — same in every row, computed once at init.
        // Both cover counters baseCounter+0, +1, +2 because any of the 3 potion reveals
        // could be common or rare depending on which physical item the player clicks.
        CommonPotionSequence = new PotionPrediction[3];
        RarePotionSequence = new PotionPrediction[3];
        for (int i = 0; i < 3; i++)
        {
            CommonPotionSequence[i] = PredictPotionByRarityAndCounter(PotionRarity.Common, _rngBaseCounter + i);
            RarePotionSequence[i] = PredictPotionByRarityAndCounter(PotionRarity.Rare, _rngBaseCounter + i);
        }
        ModLogger.Info($"Potion seq: CP=[{string.Join(", ", CommonPotionSequence.Select(p => p.Name))}] RP=[{string.Join(", ", RarePotionSequence.Select(p => p.Name))}]");

        // Precompute all offset predictions
        Predictions = new OffsetPrediction[MaxOffset + 1];
        for (int i = 0; i <= MaxOffset; i++)
            Predictions[i] = PredictAtOffset(i);

        CurrentOffset = 0;
        _cardColumnsRevealed = 0;
        _goldsRevealed = 0;
        _relicsRevealed = 0;
        foreach (var col in new[] { Rare, Uncommon, Common, Relic })
        { col.LockedAt = -1; col.PlannedOffsets.Clear(); }
        BuildCardMaps();

        ModLogger.Info($"Predictor initialized — seed={_rngSeed}, baseCounter={_rngBaseCounter}, " +
            $"rarePool={_rarePool.Count}, uncommonPool={_uncommonPool.Count}, commonPool={_commonPool.Count}, " +
            $"potions={TotalPotionCount}, hasEggs=[A:{_hasEggAttack},S:{_hasEggSkill},P:{_hasEggPower}]");

        // Dump all predictions (verbose only)
        if (PredictEverythingConfig.Instance.VerboseLogging)
        {
            for (int i = 0; i <= MaxOffset; i++)
            {
                var p = Predictions[i];
                ModLogger.Info($"  Offset {i,2}: " +
                    $"R=[{string.Join("|", p.RareCards.Select(c => c.Upgraded ? c.Name + "+" : c.Name))}] " +
                    $"U=[{string.Join("|", p.UncommonCards.Select(c => c.Upgraded ? c.Name + "+" : c.Name))}] " +
                    $"C=[{string.Join("|", p.CommonCards.Select(c => c.Upgraded ? c.Name + "+" : c.Name))}] " +
                    $"Relic=[{p.Relic.Name}] " +
                    $"CP=[{p.CommonPotion?.Name ?? "?"}] RP=[{p.RarePotion?.Name ?? "?"}]");
            }
        }
    }

    public OffsetPrediction GetPrediction(int offset)
    {
        if (offset < 0 || offset > MaxOffset) return null!;
        return Predictions[offset];
    }

    // Phase 1 (ToReward): ALL revealed potions consume 1 RNG each, before ANY Populate.
    // Phase 2 (Populate): Golds(1 RNG, NextInt), Relics(~1 RNG, PullNextRelicFromFront),
    //                      Cards(6 RNG, 3×NextItem+3×NextFloat), all in reveal order.
    // RevealedPotionCount tracks actual potions revealed (NOT TotalPotionCount).
    // Potion ToReward advances shared RNG before Populate, so ALL cards/relics see it.
    public int CardPredictionOffset => RevealedPotionCount + _goldsRevealed + _relicsRevealed + _cardColumnsRevealed * 6;

    public void OnGoldRevealed()
    {
        _goldsRevealed++;
        CurrentOffset++;
        StateChanged?.Invoke();
        PlanChanged?.Invoke();
    }

    public void OnColumnRevealed(ColumnType col, int rngCost)
    {
        int offset = CardPredictionOffset;
        GetColumnState(col).LockedAt = offset;
        GetColumnState(col).PlannedOffsets.Add(offset);
        if (col == ColumnType.Relic)
            _relicsRevealed++;
        else
            _cardColumnsRevealed++;
        CurrentOffset += rngCost;
        StateChanged?.Invoke();
        PlanChanged?.Invoke();
        if (PredictEverythingConfig.Instance.VerboseLogging)
            ModLogger.Info($"{col} locked at offset {offset} (rngCost={rngCost}) cardPredictionOffset={CardPredictionOffset}");
    }

    public void OnPotionRevealed(CrystalSpherePotion item)
    {
        var rarity = (PotionRarity?)_potionRarityField?.GetValue(item) ?? PotionRarity.Common;
        int counter = _rngBaseCounter + PotionData.RevealedCount;
        for (int i = 0; i < _potionItems.Length; i++)
        {
            if (_potionItems[i] == item)
            {
                PotionData.Revealed[i] = true;
                PotionData.LockedAt[i] = CurrentOffset;
                break;
            }
        }
        PotionData.RevealedCount++;

        // Potion ToReward consumes 1 RNG on the SHARED RNG object before any Populate.
        // All previously-locked columns will see this incremented counter during Populate.
        foreach (var col in new[] { Rare, Uncommon, Common, Relic })
        {
            if (col.IsLocked) col.LockedAt++;
        }
        CurrentOffset++;
        StateChanged?.Invoke();
        PlanChanged?.Invoke();
        if (PredictEverythingConfig.Instance.VerboseLogging)
            ModLogger.Info($"Potion revealed (#{PotionData.RevealedCount}/{PotionData.TotalCount} rarity={rarity} counter={counter}) at CurrentOffset={CurrentOffset}");
    }

    /// <summary>Notify that the RevealedPotionCount has changed (for external consumers tracking the property).</summary>
    private void RevealedPotionCountPropertyUpdated() { }

    public bool IsPotionRevealed(int index)
    {
        return PotionData?.IsRevealed(index) ?? false;
    }

    public void TogglePlan(ColumnType col, int row)
    {
        var state = GetColumnState(col);
        if (state.PlannedOffsets.Contains(row))
        {
            state.PlannedOffsets.Remove(row);
            if (PredictEverythingConfig.Instance.VerboseLogging)
                ModLogger.Info($"  Plan: UNPLAN {col}[{row}] → planned=[{string.Join(",", state.PlannedOffsets)}]");
        }
        else if (state.PlannedOffsets.Count < state.MaxPlans)
        {
            state.PlannedOffsets.Add(row);
            if (PredictEverythingConfig.Instance.VerboseLogging)
            {
                string extra = col == ColumnType.CommonPotion || col == ColumnType.RarePotion
                    ? $" potIdx={GetPotionPlanIndex(row)}" : "";
                ModLogger.Info($"  Plan: ADD {col}[{row}]{extra} → planned=[{string.Join(",", state.PlannedOffsets)}]");
            }
        }
        else if (state.MaxPlans == 1)
        {
            // Single-plan columns: replace existing
            state.PlannedOffsets.Clear();
            state.PlannedOffsets.Add(row);
            if (PredictEverythingConfig.Instance.VerboseLogging)
                ModLogger.Info($"  Plan: REPLACE {col}[{row}] (max={state.MaxPlans}) → planned=[{string.Join(",", state.PlannedOffsets)}]");
        }
        PlanChanged?.Invoke();
    }

    public (bool feasible, string sequence, string? error) ComputePlan()
    {
        var allColumns = new (ColumnType type, ColumnState state)[] {
            (ColumnType.Rare, Rare), (ColumnType.Uncommon, Uncommon),
            (ColumnType.Common, Common), (ColumnType.Relic, Relic),
            (ColumnType.CommonPotion, CommonPotionColumn),
            (ColumnType.RarePotion, RarePotionColumn)
        };
        var pending = allColumns
            .Where(x => x.state.HasPlan)
            .SelectMany(x => x.state.PlannedOffsets.Select(o => (col: x.type, offset: o,
                           cost: x.state.RngCost, resolved: x.state.IsLocked)))
            .ToList();

        if (pending.Count == 0)
        {
            if (PredictEverythingConfig.Instance.VerboseLogging)
                ModLogger.Info($"╔══ MANUAL PLAN ────────────────────────────────────────────");
            return (true, "", null);
        }

        var unresolved = pending.Where(x => !x.resolved)
            .OrderBy(x => x.offset).ToList();

        if (unresolved.Count == 0) return (true, I18n.Tr("plan_all_resolved"), null);

        var swPlan = Stopwatch.StartNew();
        if (PredictEverythingConfig.Instance.VerboseLogging)
        {
            var planDesc = string.Join(", ", pending.Select(x =>
                $"{(x.resolved ? "✓" : "?")}{x.col}@{x.offset}(cost={x.cost})"));
            ModLogger.Info($"╔══ MANUAL PLAN ────────────────────────────────────────────");
            ModLogger.Info($"║ Mode: Manual Row Select");
            ModLogger.Info($"║ Planned: {planDesc}");
            ModLogger.Info($"║ Current CardPredictionOffset: {CardPredictionOffset}");
            ModLogger.Info($"║ RevealedPotionCount: {RevealedPotionCount}  Golds: {_goldsRevealed}  Cards: {_cardColumnsRevealed}  Relics: {_relicsRevealed}");
            ModLogger.Info($"╚════════════════════════════════════════════════════════════");
        }

        int cur = CardPredictionOffset;
        int goldUsed = 0;
        var steps = new List<string>();

        foreach (var item in unresolved)
        {
            var col = item.col;
            int targetOffset = item.offset;
            int cost = item.cost;

            int delta = targetOffset - cur;
            if (delta < 0)
            {
                // Potions can share the same planned offset — auto-bump to cur
                if (col == ColumnType.CommonPotion || col == ColumnType.RarePotion)
                {
                    delta = 0;
                    if (PredictEverythingConfig.Instance.VerboseLogging)
                        ModLogger.Info($"  Plan: auto-bump {col} from offset {targetOffset} to {cur} (potion shared slot)");
                }
                else
                {
                    var conflicts = pending.Where(x =>
                        x.offset + x.cost > targetOffset &&
                        x.offset <= targetOffset &&
                        x.col != col).ToList();
                    if (conflicts.Count > 0)
                    {
                        var conflictCol = conflicts[0].col;
                        return (false, "", string.Format(I18n.Tr("error_conflict"), targetOffset, GetColumnState(conflictCol).Label));
                    }
                    return (false, "", string.Format(I18n.Tr("error_passed"), cur, targetOffset));
                }
            }
            if (delta > 0)
            {
                goldUsed += delta;
                if (goldUsed > 7)
                    return (false, "", string.Format(I18n.Tr("error_gold_limit"), goldUsed));
                steps.Add($"{I18n.Tr("gold_step")}{delta}");
            }
            steps.Add(GetColumnState(col).Label);
            cur = targetOffset + cost;
        }
        var sequence = string.Join(" → ", steps);
        ModLogger.Info($"  Plan ({swPlan.Elapsed.TotalMilliseconds:F1}ms): {sequence} (gold={goldUsed} offset: {CardPredictionOffset}→{cur})");
        return (true, sequence, null);
    }

    public void Reset()
    {
        Instance = null;
        StateChanged = null;
        PlanChanged = null;
    }

    public bool IsColumnLocked(ColumnType col)
    {
        return col switch
        {
            ColumnType.Rare => Rare.IsLocked,
            ColumnType.Uncommon => Uncommon.IsLocked,
            ColumnType.Common => Common.IsLocked,
            ColumnType.Relic => Relic.IsLocked,
            ColumnType.CommonPotion => PotionData.UnrevealedCommonCount == 0,
            ColumnType.RarePotion => PotionData.UnrevealedRareCount == 0,
            _ => false
        };
    }

    public bool IsColumnPlannedAt(ColumnType col, int row)
    {
        var state = GetColumnState(col);
        return state.PlannedOffsets.Contains(row);
    }

    private ColumnState GetColumnState(ColumnType col) => col switch
    {
        ColumnType.Rare => Rare,
        ColumnType.Uncommon => Uncommon,
        ColumnType.Common => Common,
        ColumnType.Relic => Relic,
        ColumnType.CommonPotion => CommonPotionColumn,
        ColumnType.RarePotion => RarePotionColumn,
        _ => throw new ArgumentOutOfRangeException(nameof(col))
    };

    public bool IsPotionLockedAt(int row) => PotionData?.IsLockedAt(row) ?? false;

    public bool IsCommonPotionLockedAt(int row)
    {
        if (PotionData == null) return false;
        for (int i = 0; i < PotionData.TotalCount; i++)
            if (PotionData.LockedAt[i] == row && PotionData.Rarities[i] == PotionRarity.Common)
                return true;
        return false;
    }

    public bool IsRarePotionLockedAt(int row)
    {
        if (PotionData == null) return false;
        for (int i = 0; i < PotionData.TotalCount; i++)
            if (PotionData.LockedAt[i] == row && PotionData.Rarities[i] == PotionRarity.Rare)
                return true;
        return false;
    }

    public bool IsCommonPotionPlannedAt(int row) => CommonPotionColumn.PlannedOffsets.Contains(row);
    public bool IsRarePotionPlannedAt(int row) => RarePotionColumn.PlannedOffsets.Contains(row);

    /// <summary>
    /// Get the frozen-sequence index for a planned potion at a given row.
    /// Global index = RevealedPotionCount + position among all potion plans (sorted by offset).
    /// Already-revealed potions consumed counters 0..N-1; future plans start from counter N.
    /// </summary>
    public int GetPotionPlanIndex(int row)
    {
        var offsets = new List<int>();
        offsets.AddRange(CommonPotionColumn.PlannedOffsets);
        offsets.AddRange(RarePotionColumn.PlannedOffsets);
        offsets.Sort();
        int pos = offsets.IndexOf(row);
        return pos >= 0 ? PotionData.RevealedCount + pos : -1;
    }

    // =============== Initialization helpers ===============

    private List<CardModel> BuildCardPool(Player player, CardRarity rarity)
    {
        try
        {
            var options = new CardCreationOptions(
                new[] { player.Character.CardPool },
                CardCreationSource.Other,
                CardRarityOddsType.Uniform,
                c => c.Rarity == rarity);
            options = Hook.ModifyCardRewardCreationOptions(player.RunState, player, options);
            var cards = options.GetPossibleCards(player);
            // Filter out Basic and Curse
            cards = cards.Where(c => c.Rarity != CardRarity.Basic && c.Rarity != CardRarity.Curse);
            // Replicate CardFactory.FilterForPlayerCount (private method)
            if (player.RunState.Players.Count > 1)
                cards = cards.Where(c => c.MultiplayerConstraint != CardMultiplayerConstraint.SingleplayerOnly);
            else
                cards = cards.Where(c => c.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly);
            return cards.ToList();
        }
        catch (Exception ex) { ModLogger.Error($"BuildCardPool({rarity}) failed: {ex.Message}"); return null!; }
    }

    // =============== Prediction methods ===============

    private OffsetPrediction PredictAtOffset(int offset)
    {
        // Potion predictions frozen at init: counter = baseCounter + offset.
        // Each row shows "if next reveal is a potion of this rarity, what would you get."
        // Treated as infinite supply — predictions never change after init.
        PotionPrediction? commonP = null;
        if (_cachedCommonOptions.Length > 0)
            commonP = PredictPotionByRarityAndCounter(PotionRarity.Common, _rngBaseCounter + offset);
        PotionPrediction? rareP = null;
        if (_cachedRareOptions.Length > 0)
            rareP = PredictPotionByRarityAndCounter(PotionRarity.Rare, _rngBaseCounter + offset);
        return new OffsetPrediction
        {
            RareCards = PredictCards(_rarePool, offset, CardRarity.Rare),
            UncommonCards = PredictCards(_uncommonPool, offset, CardRarity.Uncommon),
            CommonCards = PredictCards(_commonPool, offset, CardRarity.Common),
            Relic = PredictRelic(offset),
            CommonPotion = commonP,
            RarePotion = rareP
        };
    }

    private CardPrediction[] PredictCards(List<CardModel> pool, int offset, CardRarity rarity)
    {
        var rng = new Rng(_rngSeed, _rngBaseCounter + offset);
        var result = new CardPrediction[3];
        var chosen = new HashSet<CardModel>();

        for (int i = 0; i < 3; i++)
        {
            var available = pool.Where(c => !chosen.Contains(c)).ToArray();
            if (available.Length == 0)
            {
                result[i] = new CardPrediction("?", false, null);
                continue;
            }
            var card = rng.NextItem(available)!;
            chosen.Add(card);
            bool upgraded = false;

            if (card.IsUpgradable)
            {
                float roll = rng.NextFloat();
                decimal num = (decimal)roll;
                decimal baseChance = 0m;
                if (rarity != CardRarity.Rare)
                    baseChance += _actIndex * _upgradeScaling;
                baseChance = Hook.ModifyCardRewardUpgradeOdds(_player.RunState, _player, card, baseChance);
                upgraded = num <= baseChance;
            }
            result[i] = new CardPrediction(card.Title, upgraded, card);
        }

        // Egg post-processing — STS2 upgrades ALL matching-type cards unconditionally
        // via TryModifyCardRewardOptionsLate → EggRelicHelper.UpgradeValidCards()
        if (_hasEggAttack || _hasEggSkill || _hasEggPower)
        {
            for (int i = 0; i < 3; i++)
            {
                var cp = result[i];
                if (cp.Card != null && cp.Card.IsUpgradable && !cp.Upgraded)
                {
                    if ((_hasEggAttack && cp.Card.Type == CardType.Attack)
                     || (_hasEggSkill && cp.Card.Type == CardType.Skill)
                     || (_hasEggPower && cp.Card.Type == CardType.Power))
                    {
                        result[i] = cp with { Upgraded = true };
                    }
                }
            }
        }
        return result;
    }

    private RelicPrediction PredictRelic(int offset)
    {
        try
        {
            var rng = new Rng(_rngSeed, _rngBaseCounter + offset);
            float roll = rng.NextFloat();
            RelicRarity rarity = roll < 0.50f ? RelicRarity.Common
                               : roll < 0.83f ? RelicRarity.Uncommon
                               : RelicRarity.Rare;

            // Read _deques from RelicGrabBag via reflection (NEVER call PullFromFront — it removes items)
            var deques = (Dictionary<RelicRarity, List<RelicModel>>?) _dequesField?.GetValue(_player.RelicGrabBag);
            if (deques == null) return new RelicPrediction("?", "?", null, null);

            // Fallback rarity chain: Shop→Common, Common→Uncommon, Uncommon→Rare, else→None
            while ((int)rarity > 0)
            {
                if (deques.TryGetValue(rarity, out var queue))
                {
                    var relic = queue.FirstOrDefault(r => r.IsAllowed(_player.RunState));
                    if (relic != null)
                        return new RelicPrediction(relic.Title.GetFormattedText(), rarity.ToString(), relic.BigIcon, relic);
                }
                rarity = rarity switch
                {
                    RelicRarity.Shop => RelicRarity.Common,
                    RelicRarity.Common => RelicRarity.Uncommon,
                    RelicRarity.Uncommon => RelicRarity.Rare,
                    _ => RelicRarity.None
                };
            }
            return new RelicPrediction("?", "?", null, null);
        }
        catch (Exception ex)
        {
            if (PredictEverythingConfig.Instance.VerboseLogging)
                ModLogger.Warn($"PredictRelic({offset}) failed: {ex.Message}");
            return new RelicPrediction("?", "?", null, null);
        }
    }

    // =============== Potion prediction helpers (v0.107.1) ===============

    /// <summary>
    /// Predict which potion ToReward will select at a specific RNG counter for a given rarity.
    /// Counter should be baseCounter + RevealedPotionCount (before increment).
    /// </summary>
    private PotionPrediction PredictPotionByRarityAndCounter(PotionRarity rarity, int counter)
    {
        try
        {
            var options = rarity == PotionRarity.Rare ? _cachedRareOptions : _cachedCommonOptions;
            if (options == null || options.Length == 0) return new PotionPrediction("?", null);
            var rng = new Rng(_rngSeed, counter);
            var potion = rng.NextItem(options);
            return new PotionPrediction(potion?.Title?.GetFormattedText() ?? "?", potion);
        }
        catch { return new PotionPrediction("?", null); }
    }

    /// <summary>Get the Nth potion's display name (updated on reveal).</summary>
    public string? GetPotionName(int index)
    {
        return PotionData != null && index >= 0 && index < PotionData.TotalCount
            ? PotionData.Names[index] : null;
    }

    /// <summary>Get the Nth potion model for hover tooltip.</summary>
    public PotionModel? GetPotionModel(int index)
    {
        return PotionData != null && index >= 0 && index < PotionData.TotalCount
            ? PotionData.Predictions[index]?.Potion : null;
    }

    /// <summary>
    /// Count how many potion items of a given rarity exist.
    /// </summary>
    public int GetPotionCount(bool isRare)
    {
        if (_potionItems == null) return 0;
        return _potionItems.Count(p => (PotionRarity?)_potionRarityField?.GetValue(p)
            == (isRare ? PotionRarity.Rare : PotionRarity.Common));
    }

    /// <summary>Called from Potion_ToReward_Postfix to record the actual revealed name.</summary>
    public void UpdateActualPotionName(CrystalSpherePotion item, string actualName)
    {
        for (int i = 0; i < _potionItems.Length; i++)
        {
            if (_potionItems[i] == item)
            {
                ModLogger.Info($"UpdateActualPotionName: matched i={i} old=[{PotionData.Names[i]}] new=[{actualName}]");
                PotionData.Names[i] = actualName;
                return;
            }
        }
        ModLogger.Warn($"UpdateActualPotionName: NO MATCH for item, _potionItems.Length={_potionItems.Length} actual=[{actualName}]");
    }

    /// <summary>Get the frozen-sequence name for a potion locked at a given row.</summary>
    public string? GetPotionLockedNameAt(int row, PotionRarity rarity)
    {
        if (PotionData == null) return null;
        var allLocked = new List<int>();
        for (int i = 0; i < PotionData.TotalCount; i++)
            if (PotionData.LockedAt[i] >= 0)
                allLocked.Add(PotionData.LockedAt[i]);
        allLocked.Sort();
        int globalIdx = allLocked.IndexOf(row);
        if (globalIdx < 0) return null;
        var seq = rarity == PotionRarity.Rare ? RarePotionSequence : CommonPotionSequence;
        return seq != null && globalIdx < seq.Length ? seq[globalIdx].Name : null;
    }

    /// <summary>Get revealed potion names for a rarity, computed from frozen sequences.</summary>
    public List<string> GetPotionRevealedNames(PotionRarity rarity)
    {
        var result = new List<string>();
        if (PotionData == null) return result;
        var allLocked = new List<int>();
        for (int i = 0; i < PotionData.TotalCount; i++)
            if (PotionData.Revealed[i])
                allLocked.Add(PotionData.LockedAt[i]);
        allLocked.Sort();
        var seq = rarity == PotionRarity.Rare ? RarePotionSequence : CommonPotionSequence;
        for (int gi = 0; gi < allLocked.Count; gi++)
        {
            int row = allLocked[gi];
            bool matchesRarity = false;
            for (int i = 0; i < PotionData.TotalCount; i++)
                if (PotionData.LockedAt[i] == row && PotionData.Rarities[i] == rarity)
                    { matchesRarity = true; break; }
            if (matchesRarity && seq != null && gi < seq.Length)
                result.Add(seq[gi].Name ?? "?");
        }
        return result;
    }

    private void BuildCardMaps()
    {
        RareCardMap = new(); UncommonCardMap = new(); CommonCardMap = new(); RelicMap = new();
        CommonPotionMap = new(); RarePotionMap = new();
        for (int off = 0; off <= MaxOffset; off++)
        {
            var p = Predictions[off];
            foreach (var c in p.RareCards.Where(c => c.Card != null))
            { var k = (c.Name, c.Upgraded); if (!RareCardMap.ContainsKey(k)) RareCardMap[k] = off; }
            foreach (var c in p.UncommonCards.Where(c => c.Card != null))
            { var k = (c.Name, c.Upgraded); if (!UncommonCardMap.ContainsKey(k)) UncommonCardMap[k] = off; }
            foreach (var c in p.CommonCards.Where(c => c.Card != null))
            { var k = (c.Name, c.Upgraded); if (!CommonCardMap.ContainsKey(k)) CommonCardMap[k] = off; }
            if (p.Relic.Name != "?" && !RelicMap.ContainsKey(p.Relic.Name)) RelicMap[p.Relic.Name] = off;
            if (p.CommonPotion?.Name is string cn && cn != "?" && !CommonPotionMap.ContainsKey(cn)) CommonPotionMap[cn] = off;
            if (p.RarePotion?.Name is string rn && rn != "?" && !RarePotionMap.ContainsKey(rn)) RarePotionMap[rn] = off;
        }
        RareCardList = RareCardMap.Keys.OrderBy(k => k.u).ThenBy(k => k.n).ToList();
        UncommonCardList = UncommonCardMap.Keys.OrderBy(k => k.u).ThenBy(k => k.n).ToList();
        CommonCardList = CommonCardMap.Keys.OrderBy(k => k.u).ThenBy(k => k.n).ToList();
        RelicList = RelicMap.Keys.OrderBy(n => n).ToList();
    }

    public (bool f, string s, string? e) ComputeOptimalPath(
        (string n, bool u)? rt, (string n, bool u)? ut, (string n, bool u)? ct, string? relT, int? commonPotT, int? rarePotT,
        int? commonPotIdx = null, int? rarePotIdx = null)
    {
        Rare.PlannedOffsets.Clear(); Uncommon.PlannedOffsets.Clear(); Common.PlannedOffsets.Clear();
        Relic.PlannedOffsets.Clear(); CommonPotionColumn.PlannedOffsets.Clear(); RarePotionColumn.PlannedOffsets.Clear();
        var sw = Stopwatch.StartNew();

        // Card/relic target resolution
        int[]? rareOff = rt.HasValue ? FindCardOffsets(RareCardMap, rt.Value) : null;
        int[]? uncOff = ut.HasValue ? FindCardOffsets(UncommonCardMap, ut.Value) : null;
        int[]? comOff = ct.HasValue ? FindCardOffsets(CommonCardMap, ct.Value) : null;
        int[]? relOff = relT != null && RelicMap.TryGetValue(relT, out var ro) ? new[] { ro } : null;

        // === Potion prerequisite expansion ===
        var potionExact = new List<(ColumnType type, int globalIdx, PotionRarity rarity)>();
        if (commonPotIdx.HasValue) potionExact.Add((ColumnType.CommonPotion, commonPotIdx.Value, PotionRarity.Common));
        if (rarePotIdx.HasValue) potionExact.Add((ColumnType.RarePotion, rarePotIdx.Value, PotionRarity.Rare));

        int maxPotionIdx = potionExact.Count > 0 ? potionExact.Max(p => p.globalIdx) : -1;
        int availPotions = TotalPotionCount - RevealedPotionCount;

        // Build sorted potion target list: fill all indices from RevealedPotionCount to maxIdx
        var potionTargets = new List<(ColumnType type, int globalIdx, bool isExact, PotionRarity? rarity)>();
        var exactSet = new HashSet<int>(potionExact.Select(p => p.globalIdx));
        for (int idx = RevealedPotionCount; idx <= maxPotionIdx; idx++)
        {
            var exact = potionExact.FirstOrDefault(p => p.globalIdx == idx);
            if (exact.type != default)
                potionTargets.Add((exact.type, idx, true, exact.rarity));
            else
                potionTargets.Add((ColumnType.CommonPotion, idx, false, null)); // flexible — cheapest available
        }

        if (potionTargets.Count > availPotions)
        {
            ModLogger.Info($"  Potion overflow: need {potionTargets.Count} reveals but only {availPotions} potion items left ({sw.Elapsed.TotalMilliseconds:F1}ms)");
            return (false, "", I18n.Tr("error_gold_limit"));
        }

        // Build potion GridTargets (no fixed offset — auto-place at cur)
        var potionGridTargets = potionTargets.Select(p => new GridTarget
        {
            Kind = p.isExact ? TargetKind.ExactPotion : TargetKind.FlexiblePotion,
            ColumnType = p.type,
            TargetOffset = null,
            RevealIndex = p.globalIdx,
            Benefit = 1,
            Cost = p.type == ColumnType.CommonPotion ? 3 : 4,
            PotionRarity = p.rarity,
            Label = GetColumnState(p.type).Label,
        }).ToList();

        bool hasTarget = rareOff != null || uncOff != null || comOff != null || relOff != null || potionTargets.Count > 0;
        if (!hasTarget) { ModLogger.Info($"  OptPath ({sw.Elapsed.TotalMilliseconds:F1}ms): no targets"); return (true, "", null); }

        // Card/relic target groups (potion is handled separately)
        var targetGroups = new List<(ColumnType t, int[] offs, int benefit)>();
        if (rareOff != null) targetGroups.Add((ColumnType.Rare, rareOff, 6));
        if (uncOff != null) targetGroups.Add((ColumnType.Uncommon, uncOff, 6));
        if (comOff != null) targetGroups.Add((ColumnType.Common, comOff, 6));
        if (relOff != null) targetGroups.Add((ColumnType.Relic, relOff, 1));

        // Exclude potion column types from stone pool
        var targetTypes = new HashSet<ColumnType>(targetGroups.Select(g => g.t));
        foreach (var pt in potionTargets) targetTypes.Add(pt.type);
        var t0 = sw.Elapsed;
        var gridStones = BuildGridStonePool(targetTypes);
        var t1 = sw.Elapsed;
        if (PredictEverythingConfig.Instance.VerboseLogging)
            ModLogger.Info($"  [{(t1 - t0).TotalMilliseconds:F1}ms] Stone pool built: {gridStones.Count} items");

        var best = FindBestPathGrid(targetGroups, potionGridTargets, gridStones);
        var t2 = sw.Elapsed;
        if (PredictEverythingConfig.Instance.VerboseLogging)
            ModLogger.Info($"  [{(t2 - t1).TotalMilliseconds:F1}ms] FindBestPathGrid done");
        // Legacy fallback only when no potion targets (legacy can't handle index-based ordering)
        if (best == null && potionGridTargets.Count == 0)
        {
            var stonePoolFull = BuildStonePool();
            var stonePoolNoRelic = stonePoolFull.Where(s => s.type != ColumnType.Relic).ToList();
            best = FindBestPath(targetGroups, stonePoolNoRelic);
            if (best == null && relT == null && !Relic.IsLocked && hasTarget)
                best = FindBestPath(targetGroups, stonePoolFull);
        }

        if (best == null)
        {
            ModLogger.Info($"  OptPath result ({sw.Elapsed.TotalMilliseconds:F1}ms): INFEASIBLE (no path from offset {CardPredictionOffset})");
            return (false, "", I18n.Tr("error_gold_limit"));
        }

        var targetedTypes = new HashSet<ColumnType>(targetGroups.Select(g => g.t));
        foreach (var pt in potionTargets.Where(p => p.isExact))
            targetedTypes.Add(pt.type);
        foreach (var (type, offset) in best)
        {
            if (targetedTypes.Contains(type))
                GetColumnState(type).PlannedOffsets.Add(offset);
        }
        PlanChanged?.Invoke();

        var steps = new List<string>();
        int pos = CardPredictionOffset;
        int totalG = 0;
        foreach (var (type, offset) in best)
        {
            int d = offset - pos;
            totalG += d;
            if (d > 0) steps.Add(I18n.Tr("gold_step") + d);
            steps.Add(GetColumnState(type).Label);
            pos = offset + GetColumnState(type).RngCost;
        }
        ModLogger.Info($"  OptPath result ({sw.Elapsed.TotalMilliseconds:F1}ms): [{string.Join(" -> ", steps)}] gold={totalG} end={pos}");
        return (true, string.Join(" -> ", steps), null);
    }

    // =============== Grid inventory ===============

    /// <summary>
    /// Scan all items on the CrystalSphere grid and return inventory summary:
    /// item type label, total count, remaining (unrevealed), grid size, RNG benefit.
    /// </summary>
    public List<GridInventoryEntry> GetGridInventory()
    {
        var result = new List<GridInventoryEntry>();
        if (_minigame == null) return result;

        // Aggregate: (label, size, benefit) → count
        var totals = new Dictionary<(string label, string size, int benefit), int>();
        var remainings = new Dictionary<(string label, string size, int benefit), int>();
        // Ordered by benefit desc then size asc
        var order = new List<(string label, string size, int benefit)>();

        foreach (var item in _minigame.Items)
        {
            string label;
            int benefit;
            switch (item)
            {
                case CrystalSphereGold gold:
                    bool isBig = (bool?)_isBigField?.GetValue(gold) == true;
                    label = isBig ? I18n.Tr("gold_big_label") : I18n.Tr("gold_small_label");
                    benefit = 1;
                    break;
                case CrystalSpherePotion potion:
                    var r = (PotionRarity?)_potionRarityField?.GetValue(potion) ?? PotionRarity.Common;
                    label = r == PotionRarity.Rare ? I18n.Tr("col_rare_potion") : I18n.Tr("col_common_potion");
                    benefit = 1;
                    break;
                case CrystalSphereCardReward card:
                    var rarity = (CardRarity?)_rarityField?.GetValue(card) ?? CardRarity.Common;
                    label = rarity switch
                    {
                        CardRarity.Rare => I18n.Tr("col_rare"),
                        CardRarity.Uncommon => I18n.Tr("col_uncommon"),
                        _ => I18n.Tr("col_common")
                    };
                    benefit = 6;
                    break;
                case CrystalSphereRelic:
                    label = I18n.Tr("col_relic");
                    benefit = 1;
                    break;
                case CrystalSphereCurse:
                    label = I18n.Tr("curse_label");
                    benefit = 0;
                    break;
                default: continue;
            }
            string size = $"{item.Size.X}×{item.Size.Y}";
            var key = (label, size, benefit);

            if (!totals.ContainsKey(key))
            {
                totals[key] = 0;
                remainings[key] = 0;
                order.Add(key);
            }
            totals[key]++;

            // Check revealed
            bool revealed = false;
            for (int x = 0; x < item.Size.X && !revealed; x++)
                for (int y = 0; y < item.Size.Y && !revealed; y++)
                {
                    int px = item.Position.X + x;
                    int py = item.Position.Y + y;
                    if (px >= 0 && px < _minigame.GridSize.X
                        && py >= 0 && py < _minigame.GridSize.Y
                        && !_minigame.cells[px, py].IsHidden)
                        revealed = true;
                }
            if (!revealed) remainings[key]++;
        }

        foreach (var key in order)
        {
            var label = key.label;
            Color labelColor = label switch
            {
                _ when label == I18n.Tr("col_rare") => new Color(1f, 0.42f, 0.21f),
                _ when label == I18n.Tr("col_uncommon") => new Color(0.30f, 0.65f, 1f),
                _ when label == I18n.Tr("col_common") => new Color(0.784f, 0.816f, 0.878f),
                _ when label == I18n.Tr("col_relic") => new Color(0.29f, 0.87f, 0.50f),
                _ when label == I18n.Tr("col_common_potion") => new Color(0.30f, 0.65f, 1f),
                _ when label == I18n.Tr("col_rare_potion") => new Color(0.30f, 0.65f, 1f),
                _ when label == I18n.Tr("curse_label") => new Color(1f, 0.28f, 0.28f),
                _ when label == I18n.Tr("gold_small_label") => new Color(0.722f, 0.588f, 0.290f),
                _ when label == I18n.Tr("gold_big_label") => new Color(0.722f, 0.588f, 0.290f),
                _ => new Color(0.784f, 0.816f, 0.878f),
            };
            result.Add(new GridInventoryEntry
            {
                Label = label,
                Total = totals[key],
                Remaining = remainings[key],
                CellSize = key.size,
                RngBenefit = key.benefit,
                LabelColor = labelColor,
            });
        }
        return result;
    }

    // =============== Stone pool ===============

    /// <summary>
    /// Build the pool of available stepping stones from real grid items.
    /// Each unrevealed, non-target item contributes its grid cell cost and RNG benefit.
    /// Small golds (1 cell, +1 offset) are the cheapest stepping stones.
    /// </summary>
    private List<GridItem> BuildGridStonePool(HashSet<ColumnType>? excludeColumns = null)
    {
        var pool = new List<GridItem>();
        if (_minigame == null) return pool;

        foreach (var item in _minigame.Items)
        {
            // Check if any cell of this item is already revealed
            bool revealed = false;
            for (int x = 0; x < item.Size.X && !revealed; x++)
                for (int y = 0; y < item.Size.Y && !revealed; y++)
                {
                    int px = item.Position.X + x;
                    int py = item.Position.Y + y;
                    if (px >= 0 && px < _minigame.GridSize.X
                        && py >= 0 && py < _minigame.GridSize.Y
                        && !_minigame.cells[px, py].IsHidden)
                        revealed = true;
                }
            if (revealed) continue;

            ColumnType colType;
            int benefit;
            switch (item)
            {
                case CrystalSphereGold:
                    colType = ColumnType.Rare; // sentinel — gold isn't a column
                    benefit = 1;
                    break;
                case CrystalSpherePotion potion:
                    var r = (PotionRarity?)_potionRarityField?.GetValue(potion) ?? PotionRarity.Common;
                    colType = r == PotionRarity.Rare ? ColumnType.RarePotion : ColumnType.CommonPotion;
                    benefit = 1;
                    break;
                case CrystalSphereCardReward card:
                    var rarity = (CardRarity?)_rarityField?.GetValue(card) ?? CardRarity.Common;
                    colType = rarity switch
                    {
                        CardRarity.Rare => ColumnType.Rare,
                        CardRarity.Uncommon => ColumnType.Uncommon,
                        _ => ColumnType.Common
                    };
                    benefit = 6;
                    break;
                case CrystalSphereRelic:
                    colType = ColumnType.Relic;
                    benefit = 1;
                    break;
                case CrystalSphereCurse:
                    colType = ColumnType.Rare; // sentinel — curse isn't a column
                    benefit = 0;
                    break;
                default: continue;
            }

            if (excludeColumns?.Contains(colType) == true) continue;

            pool.Add(GridItem.FromItem(item, colType, benefit, false));
        }

        return pool;
    }

    // Legacy wrapper — called by existing code, delegates to grid pool
    private List<(ColumnType type, int cost)> BuildStonePool()
    {
        // Fall back to old RNG-cost model when minigame not available
        if (_minigame == null)
            return BuildStonePoolLegacy();

        var pool = new List<(ColumnType type, int cost)>();
        foreach (var gi in BuildGridStonePool())
        {
            if (gi.ColumnType != ColumnType.Rare || gi.Source is CrystalSphereCardReward)
                pool.Add((gi.ColumnType, gi.RngBenefit));
        }
        // Add gold slots (max 7x 1-offset advances)
        for (int i = 0; i < 7; i++)
            pool.Add((ColumnType.Rare, 1)); // gold placeholder
        return pool;
    }

    private List<(ColumnType type, int cost)> BuildStonePoolLegacy()
    {
        var pool = new List<(ColumnType type, int cost)>();
        if (!Rare.IsLocked) pool.Add((ColumnType.Rare, 6));
        if (!Uncommon.IsLocked) pool.Add((ColumnType.Uncommon, 6));
        if (!Common.IsLocked) pool.Add((ColumnType.Common, 6));
        if (!Relic.IsLocked) pool.Add((ColumnType.Relic, 1));
        for (int i = 0; i < TotalPotionCount; i++)
        {
            if (!PotionData.IsRevealed(i))
                pool.Add((PotionData.Rarities[i] == PotionRarity.Rare
                    ? ColumnType.RarePotion : ColumnType.CommonPotion, 1));
        }
        return pool;
    }

    // =============== Pathfinding helpers ===============

    /// <summary>
    /// Find the first offset where a specific card appears in the prediction map.
    /// </summary>
    private static int[] FindCardOffsets(
        Dictionary<(string n, bool u), int> map, (string n, bool u) target)
    {
        if (map.TryGetValue(target, out int offset))
            return new[] { offset };
        return Array.Empty<int>();
    }

    /// <summary>
    /// Try all combinations of (one offset per target group) and return the best path.
    /// Best = fewest gold used, then earliest end offset.
    /// </summary>
    private List<(ColumnType, int)>? FindBestPath(
        List<(ColumnType t, int[] offs, int cost)> targetGroups,
        List<(ColumnType type, int cost)> stonePool)
    {
        var combos = CartesianProduct(targetGroups.Select(g => g.offs).ToArray());
        if (combos.Count == 0) return null;

        // Sort combos by total distance from CardPredictionOffset (closest first)
        combos = combos.OrderBy(c => c.Sum(o => Math.Abs(o - CardPredictionOffset))).ToList();

        List<(ColumnType, int)>? best = null;
        int bestGold = int.MaxValue;
        int bestEnd = int.MaxValue;

        foreach (var combo in combos)
        {
            var targets = new List<(ColumnType type, int offset, int cost)>();
            for (int i = 0; i < targetGroups.Count; i++)
                targets.Add((targetGroups[i].t, combo[i], targetGroups[i].cost));

            var (path, gold) = UnifiedPathFinder.FindPath(targets, stonePool, CardPredictionOffset, 7);
            if (path == null || gold > 7) continue;

            int endOffset = path[^1].offset + GetColumnState(path[^1].type).RngCost;
            if (gold < bestGold || (gold == bestGold && endOffset < bestEnd))
            {
                bestGold = gold;
                bestEnd = endOffset;
                best = path;
                // First feasible path in sorted order is near-optimal; keep scanning
            }
        }

        return best;
    }

    /// <summary>
    /// Grid-aware version: prepends sorted potion targets before card/relic combos.
    /// Potion targets auto-place at cur (TargetOffset=null), no Cartesian product needed.
    /// </summary>
    private List<(ColumnType, int)>? FindBestPathGrid(
        List<(ColumnType t, int[] offs, int benefit)> targetGroups,
        List<GridTarget> potionTargets,
        List<GridItem> gridStones)
    {
        var combos = targetGroups.Count > 0
            ? CartesianProduct(targetGroups.Select(g => g.offs).ToArray())
            : new List<int[]> { System.Array.Empty<int>() };
        if (combos.Count == 0) return null;

        combos = combos.OrderBy(c => c.Sum(o => Math.Abs(o - CardPredictionOffset))).ToList();

        if (PredictEverythingConfig.Instance.VerboseLogging)
            ModLogger.Info($"  Step 3 — Cartesian product: {combos.Count} combos");

        List<(ColumnType, int)>? best = null;
        int bestGoldCells = int.MaxValue;
        int attempts = 0;

        foreach (var combo in combos)
        {
            attempts++;
            var targets = new List<GridTarget>();

            // Potion targets first (sorted by RevealIndex), auto-place at cur
            foreach (var pt in potionTargets)
                targets.Add(pt);

            // Card/relic targets
            for (int i = 0; i < targetGroups.Count; i++)
            {
                targets.Add(new GridTarget
                {
                    Kind = targetGroups[i].t == ColumnType.Relic ? TargetKind.ExactRelic : TargetKind.ExactCard,
                    ColumnType = targetGroups[i].t,
                    TargetOffset = combo[i],
                    Benefit = targetGroups[i].benefit,
                    Cost = targetGroups[i].benefit == 6 ? 4
                         : targetGroups[i].t == ColumnType.Relic ? 16 : 1,
                    Label = GetColumnState(targetGroups[i].t).Label,
                });
            }

            if (PredictEverythingConfig.Instance.VerboseLogging)
            {
                var tc = string.Join(", ", targets.Select(t =>
                    t.TargetOffset.HasValue ? $"{t.ColumnType}@{t.TargetOffset}(c={t.Cost},b={t.Benefit})"
                    : $"{t.ColumnType}@idx{t.RevealIndex}(c={t.Cost},b={t.Benefit})"));
                ModLogger.Info($"    Try #{attempts}: [{tc}]");
            }

            var (path, goldCells) = UnifiedPathFinder.FindPath(targets, gridStones, CardPredictionOffset, 9);
            if (path == null)
            {
                if (PredictEverythingConfig.Instance.VerboseLogging)
                    ModLogger.Info($"      → INFEASIBLE");
                continue;
            }

            if (PredictEverythingConfig.Instance.VerboseLogging)
                ModLogger.Info($"      → FEASIBLE goldCells={goldCells} pathLen={path.Count}");

            if (goldCells < bestGoldCells || (goldCells == bestGoldCells && path.Count < best?.Count))
            {
                bestGoldCells = goldCells;
                best = path;
            }
        }

        if (best == null && PredictEverythingConfig.Instance.VerboseLogging)
            ModLogger.Info($"  All {attempts} grid combos failed, falling back to legacy");

        return best;
    }

    /// <summary>
    /// Compute the Cartesian product of N arrays.
    /// Each element of the result is an array of N values (one from each input array).
    /// </summary>
    private static List<int[]> CartesianProduct(int[][] arrays)
    {
        var result = new List<int[]>();
        if (arrays.Length == 0) return result;

        var indices = new int[arrays.Length];
        void Recurse(int depth)
        {
            if (depth == arrays.Length)
            {
                result.Add((int[])indices.Clone());
                return;
            }
            foreach (var val in arrays[depth])
            {
                indices[depth] = val;
                Recurse(depth + 1);
            }
        }
        Recurse(0);
        return result;
    }
}
