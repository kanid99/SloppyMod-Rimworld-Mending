using RimWorld;
using Verse;

namespace MendingMod
{
    // Caps what a given bench is allowed to repair. Left off a def, that bench repairs anything.
    public class MendingTechLimitExtension : DefModExtension
    {
        public TechLevel maxTechLevel = TechLevel.Archotech;

        // Hand tools can shape cloth, leather and metal, but nothing on a bench rebuilds a
        // component - so the unpowered station turns away anything whose construction needed one,
        // whatever tech level the item itself declares.
        public bool allowComponents = true;
    }
}
