
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
    [ProtoContract]
    public class TrailModConfig
    {
        [ProtoMember(1)]
        public bool dirtRoadsOnly = false;

        [ProtoMember(2)]
        public bool foliageTrampleSounds = true;

        [ProtoMember(3)]
        public bool onlyPlayersCreateTrails = false;

        [ProtoMember(4)]
        public bool flowerTrampling = true;

        [ProtoMember(5)]
        public bool fernTrampling = true;

        [ProtoMember(6)]
        public bool onlyTrampleFoliageOnTrailCreation = false;

        [ProtoMember(7)]
        public float trampledSoilDevolveDays = 7.0f;

        [ProtoMember(8)]
        public float trailDevolveDays = 60.0f;

        [ProtoMember(9)]
        public int normalToSparseGrassTouchCount = 1;

        [ProtoMember(10)]
        public int sparseToVerySparseGrassTouchCount = 1;

        [ProtoMember(11)]
        public int verySparseToSoilTouchCount = 1;

        [ProtoMember(12)]
        public int soilToTrampledSoilTouchCount = 1;

        [ProtoMember(13)]
        public int trampledSoilToNewTrailTouchCount = 3;

        [ProtoMember(14)]
        public int newToEstablishedTrailTouchCount = 25;

        [ProtoMember(15)]
        public int establishedToDirtRoadTouchCount = 50;

        [ProtoMember(16)]
        public int dirtRoadToHighwayTouchCount = 75;

        [ProtoMember(17)]
        public int forestFloorToSoilTouchCount = 2;

        [ProtoMember(18)]
        public int cobLoseGrassTouchCount = 1;

        [ProtoMember(19)]
        public int peatLoseGrassTouchCount = 1;

        [ProtoMember(20)]
        public int clayLoseGrassTouchCount = 1;

        [ProtoMember(21)]
        public float minEntityHullSizeToTrampleX = 0;

        [ProtoMember(22)]
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
            TMGlobalConstants.foliageTrampleSounds              = config.foliageTrampleSounds;
            TMGlobalConstants.onlyPlayersCreateTrails           = config.onlyPlayersCreateTrails;
            TMGlobalConstants.flowerTrampling                   = config.flowerTrampling;
            TMGlobalConstants.fernTrampling                     = config.fernTrampling;
            TMGlobalConstants.onlyTrampleFoliageOnTrailCreation = config.onlyTrampleFoliageOnTrailCreation;

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
