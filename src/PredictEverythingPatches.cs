using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;

namespace PredictEverything;

/// <summary>
/// Harmony patches that wire the CrystalSpherePredictor into the game's
/// CrystalSphere event flow. Patches cover: minigame init, gold reveal,
/// card column reveal, relic reveal, potion reveal, and mask transparency.
/// </summary>
public static class PredictEverythingPatches
{
    private static readonly FieldInfo? _rarityField = typeof(CrystalSphereCardReward)
        .GetField("_rarity", BindingFlags.NonPublic | BindingFlags.Instance);

    // ============= Patch 1: Minigame constructor =============

    /// <summary>
    /// After CrystalSphereMinigame is constructed for the local player,
    /// initialize the predictor with the minigame's RNG state.
    /// </summary>
    [HarmonyPatch(typeof(CrystalSphereMinigame), MethodType.Constructor,
        [typeof(Player), typeof(Rng), typeof(int)])]
    [HarmonyPostfix]
    public static void CrystalSphereMinigame_ctor_Postfix(
        CrystalSphereMinigame __instance, Player owner, Rng rng)
    {
        if (!LocalContext.IsMe(owner)) return;

        // Clean up any previous predictor instance before creating a new one
        CrystalSpherePredictor.Instance?.Reset();

        var predictor = new CrystalSpherePredictor();
        predictor.Initialize(rng, owner, __instance);
    }

    // ============= Patch 2: Gold reveal =============

    [HarmonyPatch(typeof(CrystalSphereGold), nameof(CrystalSphereGold.RevealItem))]
    [HarmonyPrefix]
    public static void CrystalSphereGold_RevealItem_Prefix(CrystalSphereGold __instance, Player owner)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        if (!LocalContext.IsMe(owner)) return;
        var pred = CrystalSpherePredictor.Instance;
        // Read actual RNG counter from the grid to compare
        var gridField = typeof(CrystalSphereGold).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);
        var grid = gridField?.GetValue(__instance) as CrystalSphereMinigame;
        int actualCounter = grid?.Rng.Counter ?? -1;
        string isBig = (bool?)typeof(CrystalSphereGold).GetField("_isBig", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(__instance) == true ? "Big" : "Small";
        ModLogger.Info($"  RNG counter before Gold: {actualCounter}");
        ModLogger.Info($"REVEAL Gold ({isBig}) at offset {pred.CurrentOffset}");
        pred.OnGoldRevealed();
    }

    [HarmonyPatch(typeof(CrystalSphereGold), nameof(CrystalSphereGold.RevealItem))]
    [HarmonyPostfix]
    public static void CrystalSphereGold_RevealItem_Postfix(CrystalSphereGold __instance)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        var gridField = typeof(CrystalSphereGold).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);
        var grid = gridField?.GetValue(__instance) as CrystalSphereMinigame;
        int actualCounter = grid?.Rng.Counter ?? -1;
        ModLogger.Info($"  RNG counter after Gold: {actualCounter}");
    }

    // ============= Patch 2b: Actual card logging (Populate hook) =============

    [HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
    [HarmonyPostfix]
    public static void CardReward_Populate_Postfix(CardReward __instance)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        try
        {
            var cardsProp = typeof(CardReward).GetProperty("Cards");
            if (cardsProp != null)
            {
                var cards = cardsProp.GetValue(__instance) as System.Collections.IEnumerable;
                if (cards != null)
                {
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
                    ModLogger.Info($"  ACTUAL cards: [{string.Join(", ", names)}]");
                }
            }
        }
        catch (Exception ex) { ModLogger.Info($"  ACTUAL err: {ex.Message}"); }
    }

    // ============= Patch 2c: Reward logging (AddReward hook) =============

    [HarmonyPatch(typeof(CrystalSphereMinigame), nameof(CrystalSphereMinigame.AddReward))]
    [HarmonyPostfix]
    public static void AddReward_Postfix(CrystalSphereMinigame __instance)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        try
        {
            var rewardsField = typeof(CrystalSphereMinigame).GetField("_rewards",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var rewards = rewardsField?.GetValue(__instance) as System.Collections.IList;
            if (rewards == null || rewards.Count == 0) return;
            var last = rewards[rewards.Count - 1];
            var type = last.GetType();
            // CardReward
            var cardsProp = type.GetProperty("Cards");
            if (cardsProp != null)
            {
                var cards = cardsProp.GetValue(last) as System.Collections.IEnumerable;
                if (cards != null)
                {
                    var names = new List<string>();
                    foreach (var c in cards)
                    {
                        var titleProp = c.GetType().GetProperty("Title");
                        if (titleProp != null)
                        {
                            var title = titleProp.GetValue(c) as MegaCrit.Sts2.Core.Localization.LocString;
                            names.Add(title?.GetFormattedText() ?? "?");
                        }
                    }
                    ModLogger.Info($"  ACTUAL: [{string.Join(", ", names)}]");
                    return;
                }
            }
            // RelicReward
            var relicField = type.GetField("_relic", BindingFlags.NonPublic | BindingFlags.Instance);
            if (relicField != null)
            {
                var relic = relicField.GetValue(last);
                if (relic != null)
                {
                    var titleProp = relic.GetType().GetProperty("Title");
                    if (titleProp != null)
                    {
                        var title = titleProp.GetValue(relic) as MegaCrit.Sts2.Core.Localization.LocString;
                        ModLogger.Info($"  ACTUAL: {title?.GetFormattedText() ?? "?"}");
                        return;
                    }
                }
            }
            // PotionReward
            var potionField = type.GetField("_potion", BindingFlags.NonPublic | BindingFlags.Instance);
            if (potionField != null)
            {
                var potion = potionField.GetValue(last);
                if (potion != null)
                {
                    var titleProp = potion.GetType().GetProperty("Title");
                    if (titleProp != null)
                    {
                        var title = titleProp.GetValue(potion) as MegaCrit.Sts2.Core.Localization.LocString;
                        ModLogger.Info($"  ACTUAL: {title?.GetFormattedText() ?? "?"}");
                        return;
                    }
                }
            }
        }
        catch (Exception ex) { ModLogger.Info($"  ACTUAL err: {ex.Message}"); }
    }

    // ============= Patch 3: Card column reveal =============

    /// <summary>
    /// When a card reward column is revealed, determine the rarity via
    /// reflection and notify the predictor with the appropriate column type.
    /// </summary>
    [HarmonyPatch(typeof(CrystalSphereCardReward), nameof(CrystalSphereCardReward.RevealItem))]
    [HarmonyPrefix]
    public static void CrystalSphereCardReward_RevealItem_Prefix(
        CrystalSphereCardReward __instance, Player owner)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        if (!LocalContext.IsMe(owner)) return;

        var rarity = (CardRarity?)_rarityField?.GetValue(__instance) ?? CardRarity.Common;
        var colType = rarity switch
        {
            CardRarity.Rare => ColumnType.Rare,
            CardRarity.Uncommon => ColumnType.Uncommon,
            _ => ColumnType.Common
        };
        var pred = CrystalSpherePredictor.Instance;
        var offset = pred.CurrentOffset;
        var predictedCards = pred.Predictions[offset].GetCards(colType);
        ModLogger.Info($"REVEAL {colType} at offset {offset} — predicted: [{string.Join(", ", predictedCards.Select(c => c.Upgraded ? c.Name + "+" : c.Name))}]");
        CrystalSpherePredictor.Instance.OnColumnRevealed(colType, 6);
    }

    [HarmonyPatch(typeof(CrystalSphereCardReward), nameof(CrystalSphereCardReward.RevealItem))]
    [HarmonyPostfix]
    public static void CrystalSphereCardReward_RevealItem_Postfix(CrystalSphereCardReward __instance)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        var gridField = typeof(CrystalSphereCardReward).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);
        var grid = gridField?.GetValue(__instance) as CrystalSphereMinigame;
        int actualCounter = grid?.Rng.Counter ?? -1;
        ModLogger.Info($"  RNG counter after Card: {actualCounter}");
    }

    [HarmonyPatch(typeof(CrystalSphereCardReward), nameof(CrystalSphereCardReward.RevealItem))]
    // ============= Patch 4: Relic reveal =============

    /// <summary>
    /// When the relic column is revealed, notify the predictor.
    /// Consumes 1 RNG slot.
    /// </summary>
    [HarmonyPatch(typeof(CrystalSphereRelic), nameof(CrystalSphereRelic.RevealItem))]
    [HarmonyPrefix]
    public static void CrystalSphereRelic_RevealItem_Prefix(Player owner)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        if (!LocalContext.IsMe(owner)) return;
        var pred = CrystalSpherePredictor.Instance;
        var offset = pred.CurrentOffset;
        ModLogger.Info($"REVEAL Relic at offset {offset} — predicted: [{pred.Predictions[offset].Relic.Name}]");
        pred.OnColumnRevealed(ColumnType.Relic, 1);
    }

    [HarmonyPatch(typeof(CrystalSphereRelic), nameof(CrystalSphereRelic.RevealItem))]
    [HarmonyPostfix]
    public static void CrystalSphereRelic_RevealItem_Postfix(CrystalSphereRelic __instance)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        var gridField = typeof(CrystalSphereRelic).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);
        var grid = gridField?.GetValue(__instance) as CrystalSphereMinigame;
        int actualCounter = grid?.Rng.Counter ?? -1;
        ModLogger.Info($"  RNG counter after Relic: {actualCounter}");
    }

    // ============= Patch 5: Potion reveal =============

    /// <summary>
    /// When a potion item is revealed, shift locked/planned column offsets
    /// and advance the predictor's potion counter.
    /// </summary>
    [HarmonyPatch(typeof(CrystalSpherePotion), nameof(CrystalSpherePotion.RevealItem))]
    [HarmonyPrefix]
    public static void CrystalSpherePotion_RevealItem_Prefix(CrystalSpherePotion __instance, Player owner)
    {
        if (CrystalSpherePredictor.Instance == null) return;
        if (!LocalContext.IsMe(owner)) return;
        var pred = CrystalSpherePredictor.Instance;
        var gridField = typeof(CrystalSpherePotion).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);
        var grid = gridField?.GetValue(__instance) as CrystalSphereMinigame;
        int actualCounter = grid?.Rng.Counter ?? -1;
        ModLogger.Info($"  RNG counter before Potion: {actualCounter}");
        ModLogger.Info($"REVEAL Potion #{pred.RevealedPotionCount + 1}/{pred.TotalPotionCount} at offset {pred.CurrentOffset}");
        pred.OnPotionRevealed(__instance);
    }

    // ============= Patch 6: Mask transparency =============

    /// <summary>
    /// After NCrystalSphereMask enters the scene, apply transparency
    /// to make hidden items partially visible behind the fog.
    /// </summary>
    [HarmonyPatch(typeof(NCrystalSphereMask), nameof(NCrystalSphereMask._Ready))]
    [HarmonyPostfix]
    public static void NCrystalSphereMask_Ready_Postfix(NCrystalSphereMask __instance)
    {
        if (!PredictEverythingConfig.Instance.EnableTransparentMask) return;
        // Set alpha to 25% — grid contents become faintly visible through the fog
        __instance.SelfModulate = new Color(1f, 1f, 1f, 0.25f);
    }

    // ============= Patch 7: Screen ready — create prediction panel =============

    /// <summary>
    /// After NCrystalSphereScreen._Ready, create InfoPanel and LockedDashboard
    /// if the predictor is active and the respective config flags are enabled.
    /// </summary>
    [HarmonyPatch(typeof(NCrystalSphereScreen), "_Ready")]
    [HarmonyPostfix]
    public static void Screen_Ready_Postfix(NCrystalSphereScreen __instance)
    {
        if (CrystalSpherePredictor.Instance?.IsActive != true) return;

        if (PredictEverythingConfig.Instance.EnablePredictionPanel)
        {
            InfoPanel.Create(__instance);
            HoverTooltip.Init(__instance);
        }
        if (PredictEverythingConfig.Instance.EnableHoverPopup)
            HoverPopup.Create(__instance);
        if (PredictEverythingConfig.Instance.EnableLockedDashboard)
            LockedDashboard.Create(__instance);

        // Show first-time tutorial if not seen yet
        if (!PredictEverythingConfig.Instance.TutorialShown)
            TutorialPopup.ShowIfNeeded(__instance);
    }
}
