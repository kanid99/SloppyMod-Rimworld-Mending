using HarmonyLib;
using RimWorld;
using Verse;

namespace MendingMod
{
    // The mod's only Harmony patch. Everything else - float menus, inspect previews, worn-gear
    // repair - goes through extension points the game provides; this is the one place it has
    // none. See Bill_Mend.
    [HarmonyPatch(typeof(BillUtility), nameof(BillUtility.MakeNewBill))]
    public static class Patch_BillUtility_MakeNewBill
    {
        public static void Postfix(RecipeDef recipe, Precept_ThingStyle precept, ref Bill __result)
        {
            // Exact type, not "is Bill_Production": a recipe vanilla routes to one of its own
            // subclasses (unfinished things, mech gestation) must keep it.
            if (__result == null || __result.GetType() != typeof(Bill_Production))
                return;

            if (!MendJobMaker.IsMendRecipe(recipe))
                return;

            __result = new Bill_Mend(recipe, precept);
        }
    }
}
