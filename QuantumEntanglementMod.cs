using System.Linq;
using BTD_Mod_Helper;
using BTD_Mod_Helper.Api;
using BTD_Mod_Helper.Api.Helpers;
using BTD_Mod_Helper.Extensions;
using HarmonyLib;
using Il2CppAssets.Scripts;
using Il2CppAssets.Scripts.Models;
using Il2CppAssets.Scripts.Models.Towers;
using Il2CppAssets.Scripts.Models.Towers.Behaviors.Abilities.Behaviors;
using Il2CppAssets.Scripts.Simulation.Towers;
using Il2CppAssets.Scripts.Simulation.Towers.Behaviors.Abilities;
using Il2CppAssets.Scripts.Simulation.Towers.Behaviors.Abilities.Behaviors;
using Il2CppAssets.Scripts.Unity;
using Il2CppAssets.Scripts.Unity.UI_New.InGame.TowerSelectionMenu;
using Il2CppSystem.Collections.Generic;
using MelonLoader;
using QuantumEntanglement;

[assembly: MelonInfo(typeof(QuantumEntanglementMod), ModHelperData.Name, ModHelperData.Version, ModHelperData.RepoOwner)]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6")]

namespace QuantumEntanglement;

public class QuantumEntanglementMod : BloonsTD6Mod
{
    private const string ModelName = "QuantumEntangle";

    /// <summary>
    /// Add fake overclock abilities to all towers
    /// </summary>
    public override void OnNewGameModel(GameModel gameModel)
    {
        var entangle = Game.instance.model.GetTower(TowerType.EngineerMonkey, 0, 4, 0).GetDescendant<OverclockModel>().Duplicate();

        foreach (var tower in gameModel.towers)
        {
            tower.AddBehavior(new AbilityHelper(ModelName)
            {
                IconReference = ModContent.GetSpriteReference(this, "Icon"),
                CanActivateBetweenRounds = true,
                Behaviors = new Model[]
                {
                    entangle.Duplicate(ModelName)
                }
            });
        }
    }

    /// <summary>
    /// Perform Quantum Entanglement. It's a fancy name for something this simple lol.
    /// </summary>
    private static void LinkTower(Tower baseTower, Tower linkedTower)
    {
        if (linkedTower.entity.GetBehaviorsInDependants<Tower>().Any(tower => tower.Id == baseTower.Id))
        {
            ModHelper.Warning<QuantumEntanglementMod>("Prevented recursive entanglement");
            return;
        }

        foreach (var entity in baseTower.entity.dependantEntities.ToList()
                     .Where(entity => entity.Id == linkedTower.entity.Id))
        {
            baseTower.entity.RemoveDependant(entity);
            return;
        }

        baseTower.entity.AddDependant(linkedTower.entity);
    }

    /// <summary>
    /// Link with the selected tower instead of overclocking it
    /// </summary>
    [HarmonyPatch(typeof(Overclock), nameof(Overclock.Activate))]
    internal static class Overclock_Activate
    {
        [HarmonyPrefix]
        private static bool Prefix(Overclock __instance)
        {
            if (!__instance.model.name.EndsWith(ModelName) ||
                !__instance.Sim.towerManager.GetTowerById(__instance.selectedTowerId).Is(out var selectedTower))
                return true;

            LinkTower(__instance.ability.tower, selectedTower);

            __instance.ability.tower.UnHighlight();
            selectedTower.UnHighlight();
            __instance.ability.tower.Hilight();

            return false;
        }
    }

    /// <summary>
    /// Save linked towers
    /// </summary>
    [HarmonyPatch(typeof(Overclock), nameof(Overclock.GetSaveMetaData))]
    internal static class Overclock_GetSaveMetaData
    {
        [HarmonyPrefix]
        private static bool Prefix(Overclock __instance, Dictionary<string, string> metaData)
        {
            if (!__instance.model.name.EndsWith(ModelName)) return true;

            var towerEntity = __instance.ability.tower.entity;

            var i = 0;
            towerEntity.dependantEntities.ForEach(entity =>
            {
                if (entity.Parent == null && entity.HasBehavior<Tower>())
                {
                    metaData[ModelName + i++] = entity.GetBehavior<Tower>().Id.ToString();
                }
            });

            return false;
        }
    }

    /// <summary>
    /// Load linked towers
    /// </summary>
    [HarmonyPatch(typeof(Overclock), nameof(Overclock.LateSetSaveMetaData))]
    internal static class Overclock_LateSetSaveMetaData
    {
        [HarmonyPrefix]
        private static bool Prefix(Overclock __instance, Dictionary<string, string> metaData)
        {
            if (!__instance.model.name.EndsWith(ModelName)) return true;

            foreach (var (k, v) in metaData)
            {
                if (k.StartsWith(ModelName))
                {
                    var towerId = ObjectId.FromString(v);
                    var tower = __instance.Sim.towerManager.GetTowerById(towerId);
                    if (tower != null)
                    {
                        LinkTower(__instance.ability.tower, tower);
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Don't show Quantum Entagle ability in bottom row
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.ShowAbilityInBottomRow), MethodType.Getter)]
    internal static class Ability_ShowAbilityInBottomRow
    {
        [HarmonyPrefix]
        private static bool Prefix(Ability __instance, ref bool __result)
        {
            if (__instance.model.name.EndsWith(ModelName))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Don't show the Quantum Entangle abilities for linked towers
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.ShowInAbilityMenu), MethodType.Getter)]
    internal static class Ability_ShowInAbilityMenu
    {
        [HarmonyPrefix]
        private static bool Prefix(Ability __instance, ref bool __result)
        {
            var tower = TowerSelectionMenu.instance.selectedTower;

            if (tower == null) return true;

            if (__instance.model.name.EndsWith(ModelName) && __instance.tower.Id != tower.Id)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}