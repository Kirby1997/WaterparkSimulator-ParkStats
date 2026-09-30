using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace ParkStats.Plugin;

/// <summary>
/// Read-only hooks into the game. None of them change arguments, results or game state, and
/// none may throw: an exception escaping into native code would take the game down with it.
/// </summary>
[HarmonyPatch]
internal static class Hooks
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TabletUI), nameof(TabletUI.UpdateManagementPage))]
    private static void AfterUpdateManagementPage(TabletUI __instance) => Tablet(__instance, "UpdateManagementPage");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TabletUI), nameof(TabletUI.OnManagementButtonPressed))]
    private static void AfterManagementButton(TabletUI __instance) => Tablet(__instance, "OnManagementButtonPressed");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TabletUI), nameof(TabletUI.ToggleSubMenu))]
    private static void AfterToggleSubMenu(TabletUI __instance) => Tablet(__instance, "ToggleSubMenu");

    // The tablet clock ticks while the tablet is up, which keeps the numbers current.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TabletUI), nameof(TabletUI.UpdateTime))]
    private static void AfterUpdateTime(TabletUI __instance) => Tablet(__instance, "UpdateTime");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TabletUI), nameof(TabletUI.UpdateVisitorsInfo))]
    private static void AfterUpdateVisitorsInfo(TabletUI __instance) => Tablet(__instance, "UpdateVisitorsInfo");

    // The game clears the day's money trackers when a day is over; record the day just before.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FinanceSystem), nameof(FinanceSystem.ResetTrackers))]
    private static void BeforeResetTrackers()
    {
        try
        {
            Plugin.Instance.RecordDay();
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogError($"Could not record the day: {e}");
        }
    }

    // The game clears its running satisfaction average around the end of the day.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.FinalizeDailySatisfaction))]
    private static void BeforeFinalizeSatisfaction() => CaptureSatisfaction("finalise");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ResetDailySatisfaction))]
    private static void BeforeResetSatisfaction() => CaptureSatisfaction("reset");

    /// <summary>
    /// The game shows a tablet page one frame after its button is pressed, inside a coroutine.
    /// The coroutine class has a compiler-generated name that changes between game versions,
    /// so it is found by what it is called after rather than named here.
    /// </summary>
    public static void PatchPageToggle(Harmony harmony, ManualLogSource log)
    {
        const string coroutine = "WaitOneFrameThenToggleSubMenu";
        var type = typeof(TabletUI).GetNestedTypes().FirstOrDefault(t => t.Name.Contains(coroutine));
        var moveNext = type?.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance);
        _coroutineOwner = type?.GetProperty("__4__this");
        if (moveNext == null || _coroutineOwner == null)
        {
            log.LogWarning($"TabletUI.{coroutine} not found; the stats tabs may appear a moment late.");
            return;
        }

        harmony.Patch(moveNext, postfix: new HarmonyMethod(typeof(Hooks), nameof(AfterPageToggle)));
    }

    private static PropertyInfo _coroutineOwner;

    private static void AfterPageToggle(object __instance)
    {
        try
        {
            if (_coroutineOwner.GetValue(__instance) is TabletUI tablet) Tablet(tablet, "PageToggle");
        }
        catch (Exception e)
        {
            Plugin.Instance.ReportOnce("tablet hook PageToggle", e);
        }
    }

    private static void CaptureSatisfaction(string moment)
    {
        try
        {
            Plugin.Instance.CaptureDaySatisfaction(moment);
        }
        catch (Exception e)
        {
            Plugin.Instance.ReportOnce("satisfaction hook " + moment, e);
        }
    }

    private static void Tablet(TabletUI tablet, string source)
    {
        try
        {
            Plugin.Instance.Panel.OnTabletEvent(tablet, source);
        }
        catch (Exception e)
        {
            Plugin.Instance.ReportOnce("tablet hook " + source, e);
        }
    }
}
