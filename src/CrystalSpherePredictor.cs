using System;
using System.Collections.Generic;
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

    // Pre-read potions (reflection, no RNG simulation)
    private PotionPrediction _commonPotion = null!;
    private PotionPrediction _rarePotion = null!;
    private CrystalSpherePotion[] _potionItems = null!;
    private string[] _potionNames = null!;

    // Precomputed predictions
    public OffsetPrediction[] Predictions { get; private set; } = null!;
    private const int MaxOffset = 26;
    public bool IsActive => Predictions != null;

    // State tracking
    public int CurrentOffset { get; private set; } = 0;
    public ColumnState Rare { get; } = new(ColumnType.Rare);
    public ColumnState Uncommon { get; } = new(ColumnType.Uncommon);
    public ColumnState Common { get; } = new(ColumnType.Common);
    public ColumnState Relic { get; } = new(ColumnType.Relic);
    public int TotalPotionCount { get; private set; }
    public int RevealedPotionCount { get; private set; }
    public bool[] PotionRevealed { get; private set; } = null!;
    public Dictionary<(string n, bool u), int> RareCardMap { get; private set; } = null!;
    public Dictionary<(string n, bool u), int> UncommonCardMap { get; private set; } = null!;
    public Dictionary<(string n, bool u), int> CommonCardMap { get; private set; } = null!;
    public Dictionary<string, int> RelicMap { get; private set; } = null!;
    public List<(string n, bool u)> RareCardList { get; private set; } = null!;
    public List<(string n, bool u)> UncommonCardList { get; private set; } = null!;
    public List<(string n, bool u)> CommonCardList { get; private set; } = null!;
    public List<string> RelicList { get; private set; } = null!;

    // Events
    public event Action? StateChanged;
    public event Action? PlanChanged;

    // Reflection cache
    private static readonly FieldInfo? _potionField = typeof(CrystalSpherePotion)
        .GetField("_potion", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _dequesField = typeof(RelicGrabBag)
        .GetField("_deques", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly PropertyInfo? _upgradeScalingProp = typeof(CardFactory)
        .GetProperty("UpgradedCardOddScaling", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo? _rarityField = typeof(CrystalSphereCardReward)
        .GetField("_rarity", BindingFlags.NonPublic | BindingFlags.Instance);

    // =============== Public API ===============

    public void Initialize(Rng rng, Player player, CrystalSphereMinigame minigame)
    {
        Instance = this;
        _rngSeed = rng.Seed;
        _rngBaseCounter = rng.Counter;
        _player = player;
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

        // Read pre-determined potions via reflection (no RNG simulation)
        _potionItems = minigame.Items.OfType<CrystalSpherePotion>().ToArray();
        TotalPotionCount = _potionItems.Length;
        var commonPotionItem = _potionItems.FirstOrDefault(p =>
            ((PotionModel?)_potionField?.GetValue(p))?.Rarity == PotionRarity.Common);
        var rarePotionItem = _potionItems.FirstOrDefault(p =>
            ((PotionModel?)_potionField?.GetValue(p))?.Rarity == PotionRarity.Rare);
        _commonPotion = MakePotionPrediction(commonPotionItem);
        _rarePotion = MakePotionPrediction(rarePotionItem);
        _potionNames = _potionItems.Select(p => MakePotionPrediction(p).Name).ToArray();
        PotionRevealed = new bool[TotalPotionCount];
        ModLogger.Info($"Potion names: [{string.Join(", ", _potionNames)}]");

        // Precompute all offset predictions
        Predictions = new OffsetPrediction[MaxOffset + 1];
        for (int i = 0; i <= MaxOffset; i++)
            Predictions[i] = PredictAtOffset(i);

        CurrentOffset = 0;
        foreach (var col in new[] { Rare, Uncommon, Common, Relic })
        { col.LockedAt = -1; col.PlannedAt = null; }
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
                    $"Potions=[C:{p.CommonPotion.Name} R:{p.RarePotion.Name}]");
            }
        }
    }

    public OffsetPrediction GetPrediction(int offset)
    {
        if (offset < 0 || offset > MaxOffset) return null!;
        return Predictions[offset];
    }

    public void OnGoldRevealed()
    {
        // GoldReward.Populate() calls rng.NextInt(min, max+1), consuming 1 RNG.
        CurrentOffset++;
        StateChanged?.Invoke();
        PlanChanged?.Invoke();
        if (PredictEverythingConfig.Instance.VerboseLogging)
            ModLogger.Info($"Gold revealed → CurrentOffset={CurrentOffset}");
    }

    public void OnColumnRevealed(ColumnType col, int rngCost)
    {
        GetColumnState(col).LockedAt = CurrentOffset;
        GetColumnState(col).PlannedAt = CurrentOffset;
        CurrentOffset += rngCost;
        StateChanged?.Invoke();
        PlanChanged?.Invoke();
        if (PredictEverythingConfig.Instance.VerboseLogging)
            ModLogger.Info($"{col} revealed at offset {GetColumnState(col).LockedAt} (cost={rngCost}) → CurrentOffset={CurrentOffset}");
    }

    public void OnPotionRevealed(CrystalSpherePotion item)
    {
        // Find which potion index this is
        for (int i = 0; i < _potionItems.Length; i++)
        {
            if (_potionItems[i] == item)
            {
                PotionRevealed[i] = true;
                break;
            }
        }
        RevealedPotionCount++;
        StateChanged?.Invoke();
        PlanChanged?.Invoke();
        if (PredictEverythingConfig.Instance.VerboseLogging)
            ModLogger.Info($"Potion revealed ({RevealedPotionCount}/{TotalPotionCount}) — CurrentOffset stays at {CurrentOffset}");
    }

    public bool IsPotionRevealed(int index)
    {
        if (PotionRevealed == null || index < 0 || index >= PotionRevealed.Length) return false;
        return PotionRevealed[index];
    }

    public void TogglePlan(ColumnType col, int row)
    {
        var state = GetColumnState(col);
        if (state.PlannedAt == row)
        {
            state.PlannedAt = null;
            if (PredictEverythingConfig.Instance.VerboseLogging)
                ModLogger.Info($"Plan: UNPLAN {col}[{row}]");
        }
        else
        {
            state.PlannedAt = row;
            if (PredictEverythingConfig.Instance.VerboseLogging)
            {
                var pred = Predictions[row];
                string target = col switch
                {
                    ColumnType.Relic => $"Relic [{pred.Relic.Name}]",
                    _ => $"{col} [{string.Join(",", pred.GetCards(col).Select(c => c.Upgraded ? c.Name + "+" : c.Name))}]"
                };
                ModLogger.Info($"Plan: {target} at offset {row}");
            }
        }
        PlanChanged?.Invoke();
    }

    public (bool feasible, string sequence, string? error) ComputePlan()
    {
        var allColumns = new (ColumnType type, ColumnState state)[] {
            (ColumnType.Rare, Rare), (ColumnType.Uncommon, Uncommon),
            (ColumnType.Common, Common), (ColumnType.Relic, Relic)
        };
        var pending = allColumns
            .Where(x => x.state.HasPlan)
            .Select(x => (col: x.type, offset: x.state.PlannedAt!.Value,
                           cost: x.state.RngCost, resolved: x.state.IsLocked))
            .ToList();

        if (pending.Count == 0) return (true, "", null); // nothing planned yet

        var unresolved = pending.Where(x => !x.resolved)
            .OrderBy(x => x.offset).ToList();

        if (unresolved.Count == 0) return (true, I18n.Tr("plan_all_resolved"), null);

        int cur = CurrentOffset;
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
        return (true, string.Join(" → ", steps), null);
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
            _ => false
        };
    }

    public bool IsColumnPlannedAt(ColumnType col, int row)
    {
        var state = col switch
        {
            ColumnType.Rare => Rare,
            ColumnType.Uncommon => Uncommon,
            ColumnType.Common => Common,
            ColumnType.Relic => Relic,
            _ => throw new ArgumentOutOfRangeException(nameof(col))
        };
        return state.PlannedAt == row;
    }

    private ColumnState GetColumnState(ColumnType col) => col switch
    {
        ColumnType.Rare => Rare,
        ColumnType.Uncommon => Uncommon,
        ColumnType.Common => Common,
        ColumnType.Relic => Relic,
        _ => throw new ArgumentOutOfRangeException(nameof(col))
    };

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
        return new OffsetPrediction
        {
            RareCards = PredictCards(_rarePool, offset, CardRarity.Rare),
            UncommonCards = PredictCards(_uncommonPool, offset, CardRarity.Uncommon),
            CommonCards = PredictCards(_commonPool, offset, CardRarity.Common),
            Relic = PredictRelic(offset),
            CommonPotion = _commonPotion,
            RarePotion = _rarePotion
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

    // =============== Potion reflection helpers ===============

    private PotionPrediction MakePotionPrediction(CrystalSpherePotion? item)
    {
        if (item == null) return new PotionPrediction("?", null);
        try
        {
            var potion = (PotionModel?)_potionField?.GetValue(item);
            return potion != null ? new PotionPrediction(potion.Title.GetFormattedText(), potion) : new PotionPrediction("?", null);
        }
        catch { return new PotionPrediction("?", null); }
    }

    /// <summary>Get the Nth potion's display name (safe — pre-computed at init).</summary>
    public string? GetPotionName(int index)
    {
        if (_potionNames == null || index < 0 || index >= _potionNames.Length) return null;
        return _potionNames[index];
    }

    /// <summary>Get the Nth potion model for hover tooltip.</summary>
    public PotionModel? GetPotionModel(int index)
    {
        if (_potionItems == null || index < 0 || index >= _potionItems.Length) return null;
        return MakePotionPrediction(_potionItems[index]).Potion;
    }

    /// <summary>
    /// Count how many potion items of a given rarity exist.
    /// </summary>
    public int GetPotionCount(bool isRare)
    {
        if (_potionItems == null) return 0;
        return _potionItems.Count(p => ((PotionModel?)_potionField?.GetValue(p))?.Rarity
            == (isRare ? PotionRarity.Rare : PotionRarity.Common));
    }

    private void BuildCardMaps()
    {
        RareCardMap = new(); UncommonCardMap = new(); CommonCardMap = new(); RelicMap = new();
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
        }
        RareCardList = RareCardMap.Keys.OrderBy(k => k.u).ThenBy(k => k.n).ToList();
        UncommonCardList = UncommonCardMap.Keys.OrderBy(k => k.u).ThenBy(k => k.n).ToList();
        CommonCardList = CommonCardMap.Keys.OrderBy(k => k.u).ThenBy(k => k.n).ToList();
        RelicList = RelicMap.Keys.OrderBy(n => n).ToList();
    }

    public (bool f, string s, string? e) ComputeOptimalPath(
        (string n, bool u)? rt, (string n, bool u)? ut, (string n, bool u)? ct, string? relT)
    {
        Rare.PlannedAt = null; Uncommon.PlannedAt = null; Common.PlannedAt = null; Relic.PlannedAt = null;
        var all = new List<(ColumnType c, System.Func<int, bool> m, int cost, bool req)>();
        // Add selected targets as required
        if (rt.HasValue) all.Add((ColumnType.Rare, o => PredAt(o,0).Any(x => x.Name==rt.Value.n && x.Upgraded==rt.Value.u), 6, true));
        if (ut.HasValue) all.Add((ColumnType.Uncommon, o => PredAt(o,1).Any(x => x.Name==ut.Value.n && x.Upgraded==ut.Value.u), 6, true));
        if (ct.HasValue) all.Add((ColumnType.Common, o => PredAt(o,2).Any(x => x.Name==ct.Value.n && x.Upgraded==ct.Value.u), 6, true));
        if (relT != null) all.Add((ColumnType.Relic, o => Predictions[o].Relic.Name == relT, 1, true));
        // Add unselected unlocked card columns as optional wildcards
        bool hasTarget = rt.HasValue || ut.HasValue || ct.HasValue || relT != null;
        if (hasTarget)
        {
            if (!rt.HasValue && !Rare.IsLocked) all.Add((ColumnType.Rare, o => true, 6, false));
            if (!ut.HasValue && !Uncommon.IsLocked) all.Add((ColumnType.Uncommon, o => true, 6, false));
            if (!ct.HasValue && !Common.IsLocked) all.Add((ColumnType.Common, o => true, 6, false));
        }
        if (all.Count == 0) return (true, "", null);
        // Log
        foreach (var t in all)
        {
            var tag = t.req ? "req" : "opt";
            var offs = new List<int>();
            for (int o = 0; o <= MaxOffset; o++) if (t.m(o)) offs.Add(o);
            ModLogger.Info($"  OptPath {tag} {t.c}(cost={t.Item3}): offsets=[{string.Join(",", offs.Take(30))}]");
        }
        // Pass 1: without optional relic (prefer gold over relic)
        var best = TryPerms(all, 0, new List<(ColumnType, System.Func<int, bool>, int, bool)>());
        // Pass 2: with optional relic as last resort
        if (best == null && relT == null && !Relic.IsLocked && hasTarget)
        {
            ModLogger.Info("  OptPath pass1 failed, trying with optional Relic as last resort");
            all.Add((ColumnType.Relic, o => true, 1, false));
            best = TryPerms(all, 0, new List<(ColumnType, System.Func<int, bool>, int, bool)>());
        }
        if (best == null)
        {
            ModLogger.Info($"  OptPath result: INFEASIBLE (no path within 7 golds from offset {CurrentOffset})");
            ClearPlans(); return (false, "", "无可行的路径组合");
        }
        foreach (var it in best) GetColumnState(it.Item1).PlannedAt = it.Item2;
        PlanChanged?.Invoke();
        var steps = new List<string>(); int pos = CurrentOffset; int totalG = 0;
        foreach (var it in best)
        { int d = it.Item2 - pos; totalG += d; if (d > 0) steps.Add(I18n.Tr("gold_step") + d); steps.Add(GetColumnState(it.Item1).Label); pos = it.Item2 + it.Item3; }
        ModLogger.Info($"  OptPath result: [{string.Join(" -> ", steps)}] gold={totalG} end={pos}");
        return (true, string.Join(" -> ", steps), null);
    }

    List<(ColumnType, int, int)>? TryPerms(
        List<(ColumnType c, System.Func<int, bool> m, int cost, bool req)> all, int mask,
        List<(ColumnType, System.Func<int, bool>, int, bool)> order)
    {
        if (order.Count == all.Count) return Dfs(order, 0, CurrentOffset, 0, new(), null);
        List<(ColumnType, int, int)>? best = null;
        for (int i = 0; i < all.Count; i++)
        {
            if ((mask & (1<<i)) != 0) continue;
            order.Add(all[i]);
            var r = TryPerms(all, mask|(1<<i), order);
            if (r != null && (best == null || Less(r, best))) best = r;
            order.RemoveAt(order.Count-1);
        }
        return best;
    }

    static bool Less(List<(ColumnType, int, int)> a, List<(ColumnType, int, int)> b)
    {
        // Prefer fewer golds, then earlier end offset
        int ga = TotalGold(a), gb = TotalGold(b);
        if (ga != gb) return ga < gb;
        return a[^1].Item2 + a[^1].Item3 < b[^1].Item2 + b[^1].Item3;
    }

    static int TotalGold(List<(ColumnType, int, int)> path)
    {
        int g = 0, pos = 0;
        foreach (var it in path) { g += it.Item2 - pos; pos = it.Item2 + it.Item3; }
        return g;
    }

    List<(ColumnType, int, int)>? Dfs(
        List<(ColumnType c, System.Func<int, bool> m, int cost, bool req)> items,
        int idx, int cur, int goldUsed, List<(ColumnType, int, int)> cl, List<(ColumnType, int, int)>? best)
    {
        if (idx >= items.Count)
        {
            if (cl.Count == 0) return best;
            if (goldUsed > 7) return best;
            if (best == null) return new List<(ColumnType, int, int)>(cl);
            return Less(cl, best) ? new List<(ColumnType, int, int)>(cl) : best;
        }
        var it = items[idx];
        int maxOff = it.req ? MaxOffset : cur; // optional columns only need off=cur
        for (int off = cur; off <= maxOff; off++)
        {
            if (!it.m(off)) continue;
            int end = off + it.cost;
            if (cl.Any(r => r.Item2 < end && r.Item2 + r.Item3 > off)) continue;
            int stepGold = off - cur;
            if (goldUsed + stepGold > 7) continue;
            cl.Add((it.c, off, it.cost));
            best = Dfs(items, idx + 1, end, goldUsed + stepGold, cl, best);
            cl.RemoveAt(cl.Count - 1);
        }
        return best;
    }

    CardPrediction[] PredAt(int off, int col) => col switch
    { 0 => Predictions[off].RareCards, 1 => Predictions[off].UncommonCards, _ => Predictions[off].CommonCards };

    void ClearPlans()
    {
        foreach (var c in new[] { Rare, Uncommon, Common, Relic }) c.PlannedAt = null;
        PlanChanged?.Invoke();
    }
}
