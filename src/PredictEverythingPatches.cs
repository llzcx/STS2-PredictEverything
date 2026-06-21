using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;

namespace PredictEverything;

public static class PredictEverythingPatches
{
    private static int _eventCounter;
    private static int _divinationClick;
    private static int _divinationTotal;
    private static readonly List<string> _revealLog = new();
    private static int _goldsRevealed;
    private static int _potionsRevealed;
    private static int _cardsRevealed;
    private static int _relicsRevealed;
    private static int _cursesRevealed;

    private static readonly FieldInfo? _rarityField = typeof(CrystalSphereCardReward)
        .GetField("_rarity", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _isBigField = typeof(CrystalSphereGold)
        .GetField("_isBig", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _rngOverrideField = typeof(Reward)
        .GetField("_rngOverride", BindingFlags.NonPublic | BindingFlags.Instance);

    // ============= Patch 1: Minigame constructor — event header =============

    [HarmonyPatch(typeof(CrystalSphereMinigame), MethodType.Constructor,
        [typeof(Player), typeof(Rng), typeof(int)])]
    [HarmonyPostfix]
    public static void CrystalSphereMinigame_ctor_Postfix(
        CrystalSphereMinigame __instance, Player owner, Rng rng)
    {
        if (!LocalContext.IsMe(owner)) return;

        _eventCounter++;
        _divinationClick = 0;
        _divinationTotal = __instance.DivinationCount;
        _revealLog.Clear();
        _goldsRevealed = 0;
        _potionsRevealed = 0;
        _cardsRevealed = 0;
        _relicsRevealed = 0;
        _cursesRevealed = 0;

        CrystalSpherePredictor.Instance?.Reset();
        var predictor = new CrystalSpherePredictor();
        predictor.Initialize(rng, owner, __instance);

        ModLogger.Info("");
        ModLogger.Info($"╔══════════════════════════════════════════════════════════════╗");
        ModLogger.Info($"║         CRYSTAL SPHERE EVENT #{_eventCounter}                               ║");
        ModLogger.Info($"╠══════════════════════════════════════════════════════════════╣");
        ModLogger.Info($"║ Seed: {rng.Seed,-10}  Initial Counter: {rng.Counter,-5}                       ║");
        ModLogger.Info($"║ Items on grid (15 total):                                   ║");

        var items = __instance.Items;
        int relicCount = items.OfType<CrystalSphereRelic>().Count();
        int potionCount = items.OfType<CrystalSpherePotion>().Count();
        int cardCount = items.OfType<CrystalSphereCardReward>().Count();
        int curseCount = items.OfType<CrystalSphereCurse>().Count();
        int goldCount = items.OfType<CrystalSphereGold>().Count();

        ModLogger.Info($"║   Relic(4x4)×{relicCount}  Potion×{potionCount}  Card×{cardCount}  Curse×{curseCount}  Gold×{goldCount}              ║");
        ModLogger.Info($"║ Divinations: 6                                              ║");
        ModLogger.Info($"╚══════════════════════════════════════════════════════════════╝");
        ModLogger.Info("");

        if (PredictEverythingConfig.Instance.VerboseLogging)
        {
            ModLogger.Info("--- Item Grid Layout ---");
            foreach (var item in items)
                ModLogger.Info($"  {item.GetType().Name,-22} pos=({item.Position.X,2},{item.Position.Y,2}) size=({item.Size.X},{item.Size.Y})");
            ModLogger.Info("--- Prediction Table (all offsets) ---");
            for (int i = 0; i <= CrystalSpherePredictor.MaxOffset; i++)
            {
                var p = predictor.Predictions[i];
                ModLogger.Info($"  Offset {i,2}: " +
                    $"R=[{string.Join("|", p.RareCards.Select(c => c.Upgraded ? c.Name + "+" : c.Name))}] " +
                    $"U=[{string.Join("|", p.UncommonCards.Select(c => c.Upgraded ? c.Name + "+" : c.Name))}] " +
                    $"C=[{string.Join("|", p.CommonCards.Select(c => c.Upgraded ? c.Name + "+" : c.Name))}] " +
                    $"Relic=[{p.Relic.Name}]");
            }
            ModLogger.Info("");
        }
    }

    // ============= Patch 2: CellClicked — track actual divination count =============

    [HarmonyPatch(typeof(CrystalSphereMinigame), nameof(CrystalSphereMinigame.CellClicked))]
    [HarmonyPrefix]
    public static void CellClicked_Prefix()
    {
        _divinationClick++;
        ModLogger.Info($"┌─ Click {_divinationClick}/{_divinationTotal} ────────────────────────────────────┐");
    }

    // ============= Patch 3: Item Revealed =============

    [HarmonyPatch(typeof(CrystalSphereItem), nameof(CrystalSphereItem.RevealItem))]
    [HarmonyPrefix]
    public static void RevealItem_Prefix(CrystalSphereItem __instance, Player _)
    {
        try
        {
        if (CrystalSpherePredictor.Instance == null) return;
        var pred = CrystalSpherePredictor.Instance;

        string itemDesc;
        string rngImpact = "";
        bool predictable = true;

        switch (__instance)
        {
            case CrystalSphereGold gold:
            {
                bool isBig = (bool?)_isBigField?.GetValue(gold) == true;
                itemDesc = $"Gold ({(isBig ? "Big 30g" : "Small 10g")}) at ({gold.Position.X},{gold.Position.Y}) size=({gold.Size.X}x{gold.Size.Y})";
                rngImpact = "ToReward: 0 RNG (creates GoldReward with fixed amount)\n" +
                           "│  Populate: +1 RNG (NextInt — deterministic, always returns fixed amount)";
                predictable = true;
                _goldsRevealed++;
                pred.OnGoldRevealed();
                break;
            }
            case CrystalSphereCardReward card:
            {
                var rarity = (CardRarity?)_rarityField?.GetValue(card) ?? CardRarity.Common;
                var colType = rarity switch
                {
                    CardRarity.Rare => ColumnType.Rare,
                    CardRarity.Uncommon => ColumnType.Uncommon,
                    _ => ColumnType.Common
                };
                itemDesc = $"{colType} Card at ({card.Position.X},{card.Position.Y}) size=({card.Size.X}x{card.Size.Y})";
                rngImpact = "ToReward: 0 RNG (stores RNG in CardCreationOptions)\n" +
                           "│  Populate: +6 RNG (3×NextItem + 3×NextFloat)";
                predictable = true;

                int offset = pred.CardPredictionOffset;
                var predictedCards = pred.Predictions[offset].GetCards(colType);
                ModLogger.Info($"│  → CardPredictionOffset = {offset} (counter={pred.BaseCounter + offset})");
                ModLogger.Info($"│  → Predicted {colType}: [{string.Join(", ", predictedCards.Select(c => c.Upgraded ? c.Name + "+" : c.Name))}]");
                _cardsRevealed++;
                pred.OnColumnRevealed(colType, 6);
                break;
            }
            case CrystalSphereRelic relic:
            {
                itemDesc = $"Relic at ({relic.Position.X},{relic.Position.Y}) size=({relic.Size.X}x{relic.Size.Y})";
                rngImpact = "ToReward: 0 RNG (stores RNG via SetRng)\n" +
                           "│  Populate: depends on RelicFactory.PullNextRelicFromFront";
                predictable = true;
                _relicsRevealed++;
                pred.OnColumnRevealed(ColumnType.Relic, 1);
                break;
            }
            case CrystalSpherePotion potion:
            {
                itemDesc = $"Potion #{_potionsRevealed + 1} at ({potion.Position.X},{potion.Position.Y}) size=({potion.Size.X}x{potion.Size.Y})";
                rngImpact = "ToReward: +1 RNG (NextItem — picks random potion from rarity pool)\n" +
                           "│  Populate: 0 RNG (potion already set in ToReward)\n" +
                           "│  ⚠ This RNG runs before ALL card Populates (Phase 1)!";
                predictable = true;
                _potionsRevealed++;
                pred.OnPotionRevealed(potion);
                break;
            }
            case CrystalSphereCurse curse:
            {
                itemDesc = $"Curse at ({curse.Position.X},{curse.Position.Y}) size=({curse.Size.X}x{curse.Size.Y})";
                rngImpact = "ToReward: returns null (no reward)\n" +
                           "│  Effect: immediately adds Doubt to deck";
                predictable = false;
                _cursesRevealed++;
                break;
            }
            default:
                itemDesc = $"Unknown: {__instance.GetType().Name}";
                rngImpact = "Unknown RNG impact";
                predictable = false;
                break;
        }

        ModLogger.Info($"│  Revealed: {itemDesc}");
        ModLogger.Info($"│  RNG Impact:");
        foreach (var line in rngImpact.Split('\n'))
            ModLogger.Info($"│    {line}");
        ModLogger.Info($"│  Predictable: {(predictable ? "YES" : "NO (not part of reward RNG)")}");
        ModLogger.Info($"│  Revealed so far: {_goldsRevealed}G {_potionsRevealed}P {_cardsRevealed}C {_relicsRevealed}R {_cursesRevealed}Curse");
        ModLogger.Info($"│  Click {_divinationClick}/{_divinationTotal}, RNG offset: {pred.CurrentOffset}");
        ModLogger.Info($"├──────────────────────────────────────────────────────────────┤");

        _revealLog.Add($"Click {_divinationClick}: {itemDesc}");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"RevealItem_Prefix crashed — original RevealItem WILL still run. Error: {ex}");
        }
    }

    // ============= Patch 4: ToReward per subclass (virtual override requires per-type patching) =============

    [HarmonyPatch(typeof(CrystalSphereGold), nameof(CrystalSphereGold.ToReward))]
    [HarmonyPrefix]
    public static void Gold_ToReward_Prefix(CrystalSphereGold __instance, Player owner, Rng rng)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        ModLogger.Info($"  [Phase1-ToReward] CrystalSphereGold → counter: {rng.Counter} (consumes 0 RNG)");
    }

    [HarmonyPatch(typeof(CrystalSpherePotion), nameof(CrystalSpherePotion.ToReward))]
    [HarmonyPrefix]
    public static void Potion_ToReward_Prefix(CrystalSpherePotion __instance, Player owner, Rng rng)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        ModLogger.Info($"  [Phase1-ToReward] CrystalSpherePotion → counter before: {rng.Counter}");
    }

    [HarmonyPatch(typeof(CrystalSpherePotion), nameof(CrystalSpherePotion.ToReward))]
    [HarmonyPostfix]
    public static void Potion_ToReward_Postfix(CrystalSpherePotion __instance, Player owner, Rng rng, Reward? __result)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        string actualName = (__result as PotionReward)?.Potion?.Title?.GetFormattedText() ?? "?";
        CrystalSpherePredictor.Instance.UpdateActualPotionName(__instance, actualName);
        ModLogger.Info($"  [Phase1-ToReward] CrystalSpherePotion → counter after:  {rng.Counter} (+1 NextItem), ACTUAL: [{actualName}]");
    }

    [HarmonyPatch(typeof(CrystalSphereCardReward), nameof(CrystalSphereCardReward.ToReward))]
    [HarmonyPrefix]
    public static void Card_ToReward_Prefix(CrystalSphereCardReward __instance, Player owner, Rng rng)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        ModLogger.Info($"  [Phase1-ToReward] CrystalSphereCardReward → counter: {rng.Counter} (stores RNG, 0 consumed)");
    }

    [HarmonyPatch(typeof(CrystalSphereRelic), nameof(CrystalSphereRelic.ToReward))]
    [HarmonyPrefix]
    public static void Relic_ToReward_Prefix(CrystalSphereRelic __instance, Player owner, Rng rng)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        ModLogger.Info($"  [Phase1-ToReward] CrystalSphereRelic → counter: {rng.Counter} (stores RNG, 0 consumed)");
    }

    // CrystalSphereCurse does NOT override ToReward — inherits base (returns null)

    // ============= Patch 5: Populate diagnostics =============

    [HarmonyPatch(typeof(GoldReward), nameof(GoldReward.Populate))]
    [HarmonyPrefix]
    public static void GoldReward_Populate_Prefix(GoldReward __instance)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        var rng = (Rng?)_rngOverrideField?.GetValue(__instance);
        if (rng != null)
            ModLogger.Info($"  [Phase2-Populate] GoldReward (amount={__instance.Amount}) → counter before: {rng.Counter}");
    }

    [HarmonyPatch(typeof(GoldReward), nameof(GoldReward.Populate))]
    [HarmonyPostfix]
    public static void GoldReward_Populate_Postfix(GoldReward __instance)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        var rng = (Rng?)_rngOverrideField?.GetValue(__instance);
        if (rng != null)
            ModLogger.Info($"  [Phase2-Populate] GoldReward → counter after: {rng.Counter} (+1 NextInt)");
    }

    [HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
    [HarmonyPrefix]
    public static void CardReward_Populate_Prefix(CardReward __instance)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        try
        {
            var optProp = typeof(CardReward).GetProperty("Options", BindingFlags.NonPublic | BindingFlags.Instance);
            var options = optProp?.GetValue(__instance);
            if (options != null)
            {
                var rngOverrideProp = options.GetType().GetProperty("RngOverride");
                var rng = (Rng?)rngOverrideProp?.GetValue(options);
                if (rng != null)
                    ModLogger.Info($"  [Phase2-Populate] CardReward → counter before: {rng.Counter}");
                else
                    ModLogger.Info($"  [Phase2-Populate] CardReward → RngOverride is null, using PlayerRng");
            }
        }
        catch (Exception ex) { ModLogger.Info($"  [Phase2-Populate] CardReward error: {ex.Message}"); }
    }

    [HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
    [HarmonyPostfix]
    public static void CardReward_Populate_Postfix(CardReward __instance)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        try
        {
            var cardsProp = typeof(CardReward).GetProperty("Cards");
            if (cardsProp == null) return;
            var cards = cardsProp.GetValue(__instance) as System.Collections.IEnumerable;
            if (cards == null) return;
            var names = new List<string>();
            foreach (var c in cards)
            {
                var titleProp = c.GetType().GetProperty("Title");
                if (titleProp != null)
                {
                    var title = titleProp.GetValue(c);
                    if (title != null)
                    {
                        var fmtMethod = title.GetType().GetMethod("GetFormattedText");
                        if (fmtMethod != null)
                            names.Add((string?)fmtMethod.Invoke(title, null) ?? "?");
                        else
                            names.Add(title.ToString() ?? "?");
                    }
                    else names.Add("?");
                }
                else names.Add("?");
            }

            var optProp = typeof(CardReward).GetProperty("Options", BindingFlags.NonPublic | BindingFlags.Instance);
            var options = optProp?.GetValue(__instance);
            var rngOverrideProp = options?.GetType().GetProperty("RngOverride");
            var rng = (Rng?)rngOverrideProp?.GetValue(options);
            int counterAfter = rng?.Counter ?? -1;
            ModLogger.Info($"  [Phase2-Populate] CardReward → counter after: {counterAfter} (+6 RNG)");
            ModLogger.Info($"  [Phase2-Populate]   ACTUAL CARDS: [{string.Join(", ", names)}]");
        }
        catch (Exception ex) { ModLogger.Info($"  [Phase2-Populate] CardReward postfix error: {ex.Message}"); }
    }

    [HarmonyPatch(typeof(RelicReward), nameof(RelicReward.Populate))]
    [HarmonyPrefix]
    public static void RelicReward_Populate_Prefix(RelicReward __instance)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        var rng = (Rng?)_rngOverrideField?.GetValue(__instance);
        if (rng != null)
            ModLogger.Info($"  [Phase2-Populate] RelicReward → counter before: {rng.Counter}");
    }

    [HarmonyPatch(typeof(RelicReward), nameof(RelicReward.Populate))]
    [HarmonyPostfix]
    public static void RelicReward_Populate_Postfix(RelicReward __instance)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        var rng = (Rng?)_rngOverrideField?.GetValue(__instance);
        int counterAfter = rng?.Counter ?? -1;
        string actualName = "?";
        try
        {
            var relicProp = typeof(RelicReward).GetProperty("ClaimedRelic", BindingFlags.Public | BindingFlags.Instance)
                ?? typeof(RelicReward).GetProperty("_relic", BindingFlags.NonPublic | BindingFlags.Instance);
            if (relicProp != null)
            {
                var relic = relicProp.GetValue(__instance);
                if (relic != null)
                {
                    var titleProp = relic.GetType().GetProperty("Title");
                    if (titleProp != null)
                    {
                        var title = titleProp.GetValue(relic);
                        var fmtMethod = title?.GetType().GetMethod("GetFormattedText");
                        actualName = (string?)fmtMethod?.Invoke(title, null) ?? title?.ToString() ?? "?";
                    }
                }
            }
        }
        catch { }
        ModLogger.Info($"  [Phase2-Populate] RelicReward → counter after: {counterAfter} (+1 PullNextRelicFromFront), ACTUAL: [{actualName}]");
    }

    // ============= Patch 5: MinigameComplete — full summary =============

    [HarmonyPatch(typeof(CrystalSphereMinigame), "CompleteMinigame")]
    [HarmonyPrefix]
    public static void CompleteMinigame_Prefix(CrystalSphereMinigame __instance)
    {
        if (CrystalSpherePredictor.Instance?.IsActive != true) return;
        var pred = CrystalSpherePredictor.Instance;

        ModLogger.Info("");
        ModLogger.Info($"╔══════════════════════════════════════════════════════════════╗");
        ModLogger.Info($"║         COMPLETE MINIGAME — REWARD GENERATION                ║");
        ModLogger.Info($"╠══════════════════════════════════════════════════════════════╣");

        int revealed = _goldsRevealed + _potionsRevealed + _cardsRevealed + _relicsRevealed + _cursesRevealed;
        int unrevealed = 15 - revealed;
        ModLogger.Info($"║ Revealed: {revealed,2} items ({_goldsRevealed}G {_potionsRevealed}P {_cardsRevealed}C {_relicsRevealed}R {_cursesRevealed}Curse)");
        ModLogger.Info($"║ Unrevealed: {unrevealed,2} items remain hidden");
        ModLogger.Info($"╚══════════════════════════════════════════════════════════════╝");
        ModLogger.Info("");
        // Notify UI that all divinations are exhausted — triggers "all resolved" message
        pred.NotifyComplete();

        ModLogger.Info(">>> Phase 1: ToReward for ALL revealed items (in reveal order)");
        ModLogger.Info("    (All potion RNG consumed here, before any Populate)");
    }

    [HarmonyPatch(typeof(CrystalSphereMinigame), "CompleteMinigame")]
    [HarmonyPostfix]
    public static void CompleteMinigame_Postfix(CrystalSphereMinigame __instance)
    {
        if (CrystalSpherePredictor.Instance?.IsActive != true) return;
        var pred = CrystalSpherePredictor.Instance;

        ModLogger.Info("");
        ModLogger.Info(">>> Phase 2 Complete: All Populates finished");
        ModLogger.Info("");

        // Reveal history
        ModLogger.Info("=== REVEAL HISTORY ===");
        for (int i = 0; i < _revealLog.Count; i++)
            ModLogger.Info($"  {i + 1}. {_revealLog[i]}");

        // Prediction accuracy
        ModLogger.Info("");
        ModLogger.Info("=== PREDICTION ACCURACY ===");
        foreach (var col in new[] { ColumnType.Rare, ColumnType.Uncommon, ColumnType.Common, ColumnType.Relic, ColumnType.CommonPotion, ColumnType.RarePotion })
        {
            var state = col switch
            {
                ColumnType.Rare => pred.Rare,
                ColumnType.Uncommon => pred.Uncommon,
                ColumnType.Common => pred.Common,
                ColumnType.Relic => pred.Relic,
                ColumnType.CommonPotion => pred.CommonPotionColumn,
                ColumnType.RarePotion => pred.RarePotionColumn,
                _ => throw new ArgumentOutOfRangeException()
            };

            bool isPotionCol = col == ColumnType.CommonPotion || col == ColumnType.RarePotion;
            bool hasPotionRevealed = false;
            if (isPotionCol)
            {
                var rarity = col == ColumnType.CommonPotion ? PotionRarity.Common : PotionRarity.Rare;
                for (int i = 0; i < pred.PotionData.TotalCount; i++)
                    if (pred.PotionData.Revealed[i] && pred.PotionData.Rarities[i] == rarity)
                        { hasPotionRevealed = true; break; }
            }

            if (isPotionCol ? hasPotionRevealed : state.IsLocked)
            {
                string colName = col switch
                {
                    ColumnType.Rare => "金卡(Rare)",
                    ColumnType.Uncommon => "蓝卡(Uncommon)",
                    ColumnType.Common => "白卡(Common)",
                    ColumnType.Relic => "遗物(Relic)",
                    ColumnType.CommonPotion => "白药水(Common)",
                    ColumnType.RarePotion => "金药水(Rare)",
                    _ => col.ToString()
                };
                if (col == ColumnType.CommonPotion || col == ColumnType.RarePotion)
                {
                    bool isRare = col == ColumnType.RarePotion;
                    // Sort ALL revealed potions globally by LockedAt (reveal order)
                    var allRevealed = new List<(int idx, int lockedAt, bool rare)> ();
                    for (int i = 0; i < pred.PotionData.TotalCount; i++)
                        if (pred.PotionData.Revealed[i])
                            allRevealed.Add((i, pred.PotionData.LockedAt[i],
                                pred.PotionData.Rarities[i] == PotionRarity.Rare));
                    allRevealed.Sort((a, b) => a.lockedAt.CompareTo(b.lockedAt));
                    var filtered = allRevealed.Where(r => r.rare == isRare).ToList();
                    var revealedNames = new List<string>();
                    foreach (var r in filtered)
                    {
                        int globalIdx = allRevealed.IndexOf(r);
                        var seq = isRare ? pred.RarePotionSequence : pred.CommonPotionSequence;
                        string name = seq != null && globalIdx < seq.Length ? seq[globalIdx].Name ?? "?" : "?";
                        revealedNames.Add($"[{r.lockedAt}] {name}");
                    }
                    ModLogger.Info($"  [{colName}]: {string.Join(", ", revealedNames)}");
                }
                else
                {
                int offset = state.LockedAt;
                var prediction = pred.Predictions[offset];
                string predictedStr = col switch
                {
                    ColumnType.Relic => prediction.Relic.Name,
                    _ => string.Join(", ", prediction.GetCards(col).Select(c => c.Upgraded ? c.Name + "+" : c.Name))
                };
                ModLogger.Info($"  [{colName}] offset={offset}: predicted [{predictedStr}]");
                }
            }
            else
            {
                ModLogger.Info($"  [{col}] NOT REVEALED");
            }
        }

        ModLogger.Info("");
        ModLogger.Info($"╔══════════════════════════════════════════════════════════════╗");
        ModLogger.Info($"║         EVENT #{_eventCounter} COMPLETE                                  ║");
        ModLogger.Info($"╚══════════════════════════════════════════════════════════════╝");
        ModLogger.Info("");
    }

    // ============= Patch 6: Mask transparency =============

    [HarmonyPatch(typeof(NCrystalSphereMask), nameof(NCrystalSphereMask._Ready))]
    [HarmonyPostfix]
    public static void NCrystalSphereMask_Ready_Postfix(NCrystalSphereMask __instance)
    {
        if (!PredictEverythingConfig.Instance.EnableTransparentMask) return;
        __instance.SelfModulate = new Color(1f, 1f, 1f, 0.25f);
    }

    // ============= Patch 7: Screen ready — create UI panels =============

    [HarmonyPatch(typeof(NCrystalSphereScreen), "_Ready")]
    [HarmonyPostfix]
    public static void Screen_Ready_Postfix(NCrystalSphereScreen __instance)
    {
        if (CrystalSpherePredictor.Instance?.IsActive != true) return;

        var cfg = PredictEverythingConfig.Instance;

        if (cfg.EnablePredictionPanel)
        {
            InfoPanel.Create(__instance);
            HoverTooltip.Init(__instance);
        }
        if (cfg.EnableHoverPopup)
            HoverPopup.Create(__instance);
        if (PredictEverythingConfig.Instance.EnableLockedDashboard)
            LockedDashboard.Create(__instance);

        if (!PredictEverythingConfig.Instance.TutorialShown)
            TutorialPopup.ShowIfNeeded(__instance);
    }
}
