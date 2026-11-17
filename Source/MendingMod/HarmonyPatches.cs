using HarmonyLib;
using RimWorld;
using Verse;

namespace MendingMod
{
    // Player-added mend bills. Everything else - float menus, inspect previews, worn-gear
    // repair - goes through extension points the game provides; this is one of the two places it
    // has none. See Bill_Mend.
    [HarmonyPatch(typeof(BillUtility), nameof(BillUtility.MakeNewBill))]
    public static class Patch_BillUtility_MakeNewBill
    {
        public static void Postfix(RecipeDef recipe, Precept_ThingStyle precept, ref Bill __result)
        {
            // Exact type, not "is Bill_Production": a recipe vanilla routes to one of its own
            // subclasses (unfinished things, mech gestation) must keep it.
            if (__result == null || __result.GetType() != typeof(Bill_Production))
                return;

            // Recycle bills too: "least valuable first" is at least as useful when choosing
            // what to break down as when choosing what to fix.
            if (!MendJobMaker.IsMendRecipe(recipe) && !MendJobMaker.IsRecycleRecipe(recipe))
                return;

            __result = new Bill_Mend(recipe, precept);
        }
    }

    // The other: CompBreakdownable has no way to vary its breakdown rate per building, and the
    // repair center's should follow its overclock. The BreakdownManager's roll for that one
    // building is handed to Building_AutomatedMender.CheckForBreakdown; every other building
    // runs vanilla's untouched.
    [HarmonyPatch(typeof(CompBreakdownable), nameof(CompBreakdownable.CheckForBreakdown))]
    public static class Patch_CompBreakdownable_CheckForBreakdown
    {
        public static bool Prefix(CompBreakdownable __instance)
        {
            if (!(__instance.parent is Building_AutomatedMender mender))
                return true;

            mender.CheckForBreakdown(__instance);
            return false;
        }
    }
}
