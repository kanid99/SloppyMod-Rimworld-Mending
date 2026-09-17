using RimWorld;
using Verse;

namespace RiimbaMod
{
    [DefOf]
    public static class RiimbaDefOf
    {
        public static JobDef Riimba_Clean;
        public static JobDef Riimba_Dock;
        public static JobDef Riimba_SignOn;

        public static ThingDef RiimbaStation;

        static RiimbaDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RiimbaDefOf));
        }
    }

    // Reach and team size, read off the station's def. See Buildings_RiimbaStation.xml for why
    // this is an extension rather than a comp.
    public class RiimbaStationExtension : DefModExtension
    {
        public float radius = 10f;
        public int maxUnits = 3;
        public float powerPerChargingUnit = 150f;
    }
}
