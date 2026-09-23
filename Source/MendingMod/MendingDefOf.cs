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

        static MendingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MendingDefOf));
        }
    }
}
