namespace TrailMod
{
    public class TMGlobalConstants
    {
        public static bool foliageTrampleSounds                 = true;
        public static bool onlyPlayersCreateTrails              = false;
        public static bool flowerTrampling                      = true;
        public static bool fernTrampling                        = true;
        public static bool onlyTrampleFoliageOnTrailCreation    = false;
        public static float trampledSoilDevolveDays = 7.0f;
        public static float trailDevolveDays = 60.0f;
        public static int normalToSparseGrassTouchCount     = 1;
        public static int sparseToVerySparseGrassTouchCount = 1;
        public static int verySparseToSoilTouchCount        = 1;
        public static int soilToTrampledSoilTouchCount      = 1;
        public static int trampledSoilToNewTrailTouchCount  = 3;
        public static int newToEstablishedTrailTouchCount   = 25;
        public static int establishedToDirtRoadTouchCount   = 50;
        public static int dirtRoadToHighwayTouchCount       = 75;
        public static int forestFloorToSoilTouchCount       = 2;
        public static int cobLoseGrassTouchCount            = 1;
        public static int peatLoseGrassTouchCount           = 1;
        public static int clayLoseGrassTouchCount           = 1;
        public static float minEntityHullSizeToTrampleX     = 0;
        public static float minEntityHullSizeToTrampleY     = 0;
    }
}
