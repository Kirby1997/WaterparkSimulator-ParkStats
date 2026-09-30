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
