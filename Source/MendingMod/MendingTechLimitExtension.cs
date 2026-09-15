using RimWorld;
using Verse;

namespace MendingMod
{
    // Caps what a given bench is allowed to repair. Left off a def, that bench repairs anything.
    public class MendingTechLimitExtension : DefModExtension
    {
        public TechLevel maxTechLevel = TechLevel.Archotech;
    }
}
