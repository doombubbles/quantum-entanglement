using System.Linq;
using BTD_Mod_Helper;
using BTD_Mod_Helper.Api;
using BTD_Mod_Helper.Api.Enums;
using BTD_Mod_Helper.Api.Helpers;
using BTD_Mod_Helper.Api.ModOptions;
using BTD_Mod_Helper.Extensions;
using HarmonyLib;
using Il2CppAssets.Scripts;
using Il2CppAssets.Scripts.Models;
using Il2CppAssets.Scripts.Models.Towers;
using Il2CppAssets.Scripts.Models.Towers.Behaviors.Abilities.Behaviors;
using Il2CppAssets.Scripts.Models.Towers.Weapons;
using Il2CppAssets.Scripts.Simulation.Objects;
using Il2CppAssets.Scripts.Simulation.Towers;
using Il2CppAssets.Scripts.Simulation.Towers.Behaviors.Abilities;
using Il2CppAssets.Scripts.Simulation.Towers.Behaviors.Abilities.Behaviors;
using Il2CppAssets.Scripts.Unity;
using Il2CppAssets.Scripts.Unity.Bridge;
using Il2CppAssets.Scripts.Unity.UI_New.InGame.TowerSelectionMenu;
using Il2CppSystem.Collections.Generic;
using MelonLoader;
using QuantumEntanglement;
using UnityEngine;
using UnityEngine.UI;
[assembly:
    MelonInfo(typeof(QuantumEntanglementMod), ModHelperData.Name, ModHelperData.Version, ModHelperData.RepoOwner)]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6")]

namespace QuantumEntanglement;

public class QuantumEntanglementMod : BloonsTD6Mod
{
    private const string ModelName = "QuantumEntangle";

    public static readonly ModSettingBool AbilitiesFix = new(true)
    {
        description = "Changes abilities like the Tsar Bomba so that the Wizard Lord Phoenix can make them permanent." +
                      " Note that most ability visuals / sounds will still not apply.",
        icon = VanillaSprites.TsarBombaUpgradeIcon
    };

    public static readonly ModSettingBool SuperEntanglement = new(false)
    {
        description = "Makes ALL Mutators applied on base towers also apply to their linked towers",
        icon = VanillaSprites.TotalTransformationUpgradeIcon
    };

    public static readonly ModSettingBool Visualizer = new(false)
    {
        description =
            "Adds a visualizer of the Entity Factory's cache, previously for the purposes of testing out the Total Transformation bug itself. " +
            "Doing this will disable the abilities, because those add an additional entity to towers and change things slightly.",
        icon = VanillaSprites.MkOnGreen
    };

    /// <summary>
    /// Add fake overclock abilities to all towers
    /// </summary>
    public override void OnNewGameModel(GameModel gameModel)
    {
        if (Visualizer) return;

        var entangle = Game.instance.model.GetTower(TowerType.EngineerMonkey, 0, 4).GetDescendant<OverclockModel>();

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

            if (AbilitiesFix)
            {
                tower.GetDescendants<ActivateAttackModel>().ForEach(activateAttackModel =>
                {
                    activateAttackModel.GetDescendants<WeaponModel>().ForEach(weapon =>
                    {
                        if (weapon.Rate > 60)
                        {
                            weapon.Rate = 2;
                        }
                    });

                    activateAttackModel.isOneShot = false;
                });
            }
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
    /// Don't save other information about the Quantum Entangle ability, can cause issues
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.GetSaveMetaData))]
    internal static class Ability_GetSaveMetaData
    {
        [HarmonyPrefix]
        private static bool Prefix(Ability __instance)
        {
            return !__instance.model.name.EndsWith(ModelName);
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

    [HarmonyPatch(typeof(TowerSelectionMenu), nameof(TowerSelectionMenu.SelectTower))]
    internal static class TowerSelectionMenu_SelectTower
    {
        [HarmonyPostfix]
        private static void Postfix(TowerSelectionMenu __instance, TowerToSimulation tower)
        {
            var geraldo = tower.tower?.towerModel?.towerSelectionMenuThemeId == "Geraldo";
            var abilityButtons =
                __instance.gameObject.GetComponentInChildrenByName<GridLayoutGroup>("AbilityButtons");
            abilityButtons.padding.top = geraldo ? 300 : 0;
            abilityButtons.rectTransform.localScale = Vector3.one * (geraldo ? 1.1f : 1);
            abilityButtons.SetDirty();
        }
    }


    [HarmonyPatch(typeof(Tower), nameof(Tower.AddMutator))]
    internal static class Tower_AddMutator
    {
        private static bool midPatch;

        [HarmonyPrefix]
        private static bool Prefix
        (
            Tower __instance, BehaviorMutator mutator,
            int time,
            bool updateDuration,
            bool applyMutation,
            bool onlyTimeoutWhenActive,
            bool useRoundTime,
            bool cascadeMutators,
            bool includeSubTowers,
            bool ignoreRecursionCheck,
            int roundsRemaining,
            bool isParagonMutator
        )
        {
            if (!SuperEntanglement || midPatch) return true;

            midPatch = true;

            __instance.entity.GetBehaviorsInDependants<Tower>().ForEach(tower =>
            {
                tower.AddMutator(mutator, time,
                    updateDuration, applyMutation, onlyTimeoutWhenActive, useRoundTime, cascadeMutators,
                    includeSubTowers,
                    ignoreRecursionCheck, roundsRemaining, isParagonMutator);
            });

            midPatch = false;

            return true;
        }
    }
}