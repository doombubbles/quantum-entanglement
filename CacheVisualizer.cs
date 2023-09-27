using System;
using System.Linq;
using BTD_Mod_Helper.Api.Components;
using BTD_Mod_Helper.Api.Enums;
using BTD_Mod_Helper.Extensions;
using HarmonyLib;
using Il2CppAssets.Scripts.Simulation;
using Il2CppAssets.Scripts.Simulation.Objects;
using Il2CppAssets.Scripts.Unity.UI_New;
using Il2CppAssets.Scripts.Unity.UI_New.InGame;
using Il2CppSystem.Collections.Generic;
using Il2CppSystem.Linq;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace QuantumEntanglement;

[RegisterTypeInIl2Cpp(false)]
public class CacheVisualizer : MonoBehaviour
{
    public static CacheVisualizer? Instance { get; private set; }

    private const int Size = 90;
    private const int OffsetX = -132;

    private ModHelperPanel panel;
    private readonly List<ModHelperImage> ticks = new();

    private const string Good = VanillaSprites.MkOnGreen;
    private const string Bad = VanillaSprites.MkOffRed;

    private float y;
    private Transform matchX;

    public CacheVisualizer(IntPtr ptr) : base(ptr)
    {
    }

    public static void Create(MainHudLeftAlign mainHudLeftAlign)
    {
        var panel = mainHudLeftAlign.gameObject.AddModHelperPanel(new Info("CacheVisualizer")
        {
            Width = Size,
            Y = 70,
            Anchor = new Vector2(0, 1),
            Pivot = new Vector2(0.5f, 1)
        }, null, RectTransform.Axis.Vertical, 7);
        var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Instance = panel.gameObject.AddComponent<CacheVisualizer>();
        Instance.panel = panel;
        Instance.y = panel.RectTransform.position.y / panel.transform.lossyScale.y;
        Instance.matchX = mainHudLeftAlign.healthButton.transform;
    }

    private ModHelperImage CreateTick()
    {
        var tick = panel.AddImage(new Info("Tick" + ticks.Count, Size), "");
        ticks.Add(tick);
        return tick;
    }

    private void Update()
    {
        var rectTransform = transform;
        var scale = rectTransform.lossyScale.x;
        rectTransform.position = rectTransform.position with
        {
            y = y * scale,
            x = matchX.position.x + OffsetX * scale
        };
    }

    public void Process(Simulation simulation)
    {
        var factory = simulation.factory.GetFactory<Entity>();
        var cache = factory.pool.Cast<IEnumerable<Entity>>().Skip(factory.freePtr).ToArray();
        var goodPointers = InGame.Bridge.Simulation.towerManager.GetTowers()
            .ToArray()
            .SelectMany(tower => tower.entity.dependantEntities.ToArray())
            .Where(entity => entity.IsDestroyed)
            .Select(entity => entity.Pointer)
            .ToHashSet();

        for (var i = 0; i < cache.Count; i++)
        {
            var entity = cache[i];
            var tick = i >= ticks.Count ? CreateTick() : ticks.Get(i);
            tick.SetActive(true);
            tick.Image.SetSprite(goodPointers.Contains(entity.Pointer) ? Good : Bad);
        }

        for (var i = cache.Count; i < ticks.Count; i++)
        {
            ticks.Get(i).gameObject.SetActive(false);
        }
    }
}

[HarmonyPatch(typeof(MainHudLeftAlign), nameof(MainHudLeftAlign.Initialise))]
internal static class MainHudLeftAlign_PostInitialised
{
    [HarmonyPostfix]
    private static void Postfix(MainHudLeftAlign __instance)
    {
        if (QuantumEntanglementMod.Visualizer)
        {
            CacheVisualizer.Create(__instance);
        }
    }
}

[HarmonyPatch(typeof(Simulation), nameof(Simulation.Simulate))]
internal static class Simulation_Simulate
{
    [HarmonyPostfix]
    private static void Postfix(Simulation __instance)
    {
        if (QuantumEntanglementMod.Visualizer)
        {
            CacheVisualizer.Instance.Exists()?.Process(__instance);
        }
    }
}