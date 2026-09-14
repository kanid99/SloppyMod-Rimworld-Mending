using RimWorld;
using Verse;

namespace MendingMod
{
    [DefOf]
    public static class MendingDefOf
    {
        public static JobDef DoMend;

        static MendingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MendingDefOf));
        }
    }
}
