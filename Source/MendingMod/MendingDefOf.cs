using RimWorld;
using Verse;

namespace MendingMod
{
    [DefOf]
    public static class MendingDefOf
    {
        public static JobDef DoMend;
        public static JobDef EmptyMenderWaste;
        public static JobDef RepairWorn;

        public static RecipeDef MenderBill_Repair;
        public static RecipeDef MenderBill_Recycle;

        static MendingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MendingDefOf));
        }
    }
}
