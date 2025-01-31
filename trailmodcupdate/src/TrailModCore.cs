
using HarmonyLib;
using ProtoBuf;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TrailMod
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class TrailModConfig
    {
        public bool creativeTrampling = false;
        public bool dirtRoadsOnly = false;
        public bool foliageTrampleSounds = true;
        public bool onlyPlayersCreateTrails = false;
        public bool flowerTrampling = true;
        public bool fernTrampling = true;

        public bool onlyTrampleGrassOnTrailCreation = false;
        public bool onlyTrampleFlowersOnTrailCreation = true;
        public bool onlyTrampleFernsOnTrailCreation = true;

        public float trampledSoilDevolveDays = 7.0f;
        public float trailDevolveDays = 60.0f;

        public int normalToSparseGrassTouchCount = 1;
        public int sparseToVerySparseGrassTouchCount = 1;
        public int verySparseToSoilTouchCount = 1;
        public int soilToTrampledSoilTouchCount = 1;
        public int trampledSoilToNewTrailTouchCount = 3;
        public int newToEstablishedTrailTouchCount = 25;
        public int establishedToDirtRoadTouchCount = 50;
        public int dirtRoadToHighwayTouchCount = 75;

        public int forestFloorToSoilTouchCount = 2;

        public int cobLoseGrassTouchCount = 1;
        public int peatLoseGrassTouchCount = 1;
        public int clayLoseGrassTouchCount = 1;

        public float minEntityHullSizeToTrampleX = 0;
        public float minEntityHullSizeToTrampleY = 0;
    }
    public class TrailModCore : ModSystem
    {

        TrailModConfig config = new TrailModConfig();

        private Harmony harmony;
        private TrailChunkManager trailChunkManager;

        public override double ExecuteOrder()
        {
            return 0.0;
        }

        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);

            //Debug Test For Block Accessing. Comment out when done.
            //RuntimeEnv.DebugOutOfRangeBlockAccess = true;

            if ( api.Side == EnumAppSide.Server )
            {
                ReadConfigFromJson(api);
                ApplyConfigPatchFlags(api);
                ApplyConfigGlobalConsts();
            }
            
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            harmony = new Harmony("com.grifthegnome.trailmod.trailpatches");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            RegisterBlocksShared(api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            trailChunkManager = TrailChunkManager.GetTrailChunkManager();
            trailChunkManager.InitData( api.World, api );

            api.Event.RegisterCallback(trailChunkManager.Clean, (int)TrailChunkManager.TRAIL_CLEANUP_INTERVAL);

            api.Event.ChunkDirty += trailChunkManager.OnChunkDirty;
            api.Event.ChunkColumnUnloaded += trailChunkManager.OnChunkColumnUnloaded;

            api.Event.SaveGameLoaded += trailChunkManager.OnSaveGameLoading;
            api.Event.GameWorldSave += trailChunkManager.OnSaveGameSaving;

            api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, () => {
                trailChunkManager.ShutdownSaveState();
                //Clean up all manager stuff.
                //If we don't it persists between loads.
                trailChunkManager.ShutdownCleanup();
                trailChunkManager = null;
            });
        }

        private void RegisterBlocksShared(ICoreAPI api)
        {
            api.RegisterBlockClass("BlockTrail", typeof(BlockTrail));
        }

        private void ReadConfigFromJson(ICoreAPI api)
        {
            //Called Server Only
            try
            {
                TrailModConfig modConfig = api.LoadModConfig<TrailModConfig>("TrailModConfig.json");

                if (modConfig != null)
                {
                    config = modConfig;
                }
                else
                {
                    //We don't have a valid config.
                    throw new Exception();
                }

            }
            catch (Exception e)
            {
                api.World.Logger.Error("Failed loading TrailModConfig.json, Will initialize new one", e);
                config = new TrailModConfig();
                api.StoreModConfig(config, "TrailModConfig.json");
            }
        }

        private void ApplyConfigPatchFlags(ICoreAPI api)
        {
            //Enable/Disable Config Settngs
            api.World.Config.SetBool("dirtRoadsOnly", config.dirtRoadsOnly);
        }

        private void ApplyConfigGlobalConsts()
        {
            //GENERAL SETTINGS
            TMGlobalConstants.creativeTrampling                 = config.creativeTrampling;
            TMGlobalConstants.foliageTrampleSounds              = config.foliageTrampleSounds;
            TMGlobalConstants.onlyPlayersCreateTrails           = config.onlyPlayersCreateTrails;
            TMGlobalConstants.flowerTrampling                   = config.flowerTrampling;
            TMGlobalConstants.fernTrampling                     = config.fernTrampling;
            
            //FOLIAGE TRAMPLE SETTINGS
            TMGlobalConstants.onlyTrampleGrassOnTrailCreation = config.onlyTrampleGrassOnTrailCreation;
            TMGlobalConstants.onlyTrampleFlowersOnTrailCreation = config.onlyTrampleFlowersOnTrailCreation;
            TMGlobalConstants.onlyTrampleFernsOnTrailCreation = config.onlyTrampleFernsOnTrailCreation;

            //TRAIL DEVOLVE TIMES
            TMGlobalConstants.trampledSoilDevolveDays   = config.trampledSoilDevolveDays;
            TMGlobalConstants.trailDevolveDays          = config.trailDevolveDays;

            //SOIL
            TMGlobalConstants.normalToSparseGrassTouchCount     = config.normalToSparseGrassTouchCount;
            TMGlobalConstants.sparseToVerySparseGrassTouchCount = config.sparseToVerySparseGrassTouchCount;
            TMGlobalConstants.verySparseToSoilTouchCount        = config.verySparseToSoilTouchCount;
            TMGlobalConstants.soilToTrampledSoilTouchCount      = config.soilToTrampledSoilTouchCount;

            //TRAILS
            TMGlobalConstants.trampledSoilToNewTrailTouchCount  = config.trampledSoilToNewTrailTouchCount;
            TMGlobalConstants.newToEstablishedTrailTouchCount   = config.newToEstablishedTrailTouchCount;
            TMGlobalConstants.establishedToDirtRoadTouchCount   = config.establishedToDirtRoadTouchCount;
            TMGlobalConstants.dirtRoadToHighwayTouchCount       = config.dirtRoadToHighwayTouchCount;
            TMGlobalConstants.forestFloorToSoilTouchCount       = config.forestFloorToSoilTouchCount;

            //COB, PEAT, CLAY
            TMGlobalConstants.cobLoseGrassTouchCount  = config.cobLoseGrassTouchCount;
            TMGlobalConstants.peatLoseGrassTouchCount = config.peatLoseGrassTouchCount;
            TMGlobalConstants.clayLoseGrassTouchCount = config.clayLoseGrassTouchCount;

            //ENTITY MIN HULL SIZE TO TRAMPLE
            TMGlobalConstants.minEntityHullSizeToTrampleX = config.minEntityHullSizeToTrampleX;
            TMGlobalConstants.minEntityHullSizeToTrampleY = config.minEntityHullSizeToTrampleY;
    }

        public override void Dispose()
        {
            harmony.UnpatchAll(harmony.Id);
            base.Dispose();
        }
    }
}
