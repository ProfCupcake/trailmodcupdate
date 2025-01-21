using HarmonyLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace TrailMod
{
    public enum ETrailTrampleType
    {
        NO_TRAMPLE,
        DEFAULT,
        TALLGRASS,
    }
    public struct TrailBlockPosEntry
    {
        private BlockPos _blockPos;
        private long _lastTouchTime = -1;
        private long _lastTouchEntID = -1;
        private EntityPos _lastTouchEntityPos = null;
        private int _touchCount = 0;

        const double TRAIL_COOLDOWN_MS = 900000; //Block touch count decays every 15 minutes.

        public long lastTouchTime
        {
            get { return _lastTouchTime; }
            set { _lastTouchTime = value; }
        }

        public long lastTouchEntID
        {
            get { return _lastTouchEntID; }
            set { _lastTouchEntID = value; }
        }

        public EntityPos lastTouchEntityPos
        {
            get{ return _lastTouchEntityPos; } 
            set { _lastTouchEntityPos = value;}
        }

        public TrailBlockPosEntry( BlockPos blockPos, long newTouchEntID, EntityPos newTouchEntityPos, long newTouchTime, int touchCount )
        {
            _blockPos = blockPos.Copy();
            lastTouchEntID= newTouchEntID;
            lastTouchTime = newTouchTime;
            lastTouchEntityPos= newTouchEntityPos;
            _touchCount = touchCount;
        }

        public BlockPos GetBlockPos()
        {
            return _blockPos;
        }
        public int GetTouchCount()
        {
            return _touchCount;
        }

        public void DecayTouchCount(long currentElapsedMS)
        {
            //Decrement touchCount based on delta between current and previous touch time.
            long touchTimeDelta = currentElapsedMS - lastTouchTime;

            //Decay Touch Count
            int touchDecay = (int)(touchTimeDelta / TRAIL_COOLDOWN_MS);
            _touchCount = Math.Max(0, _touchCount - touchDecay);
        }

        public void ClearTouchCount()
        {
            _touchCount = 0;
        }

        public bool BlockTouched(long newTouchEntID, EntityPos newTouchEntityPos, long newTouchTime)
        {
            //Before setting lastTouchTime, determine if we need to decrement touchCount based on delta between current and previous touch time.
            long touchTimeDelta = newTouchTime - lastTouchTime;

            lastTouchEntID = newTouchEntID;
            lastTouchTime = newTouchTime;
            lastTouchEntityPos = newTouchEntityPos;

            if (touchTimeDelta > 500)
            {
                //Decay Touch Count
                int touchDecay = (int)(touchTimeDelta / TRAIL_COOLDOWN_MS);
                _touchCount = Math.Max(0, _touchCount - touchDecay);

                _touchCount++;
                return true;
            }

            return false;
        
        }

        public void BlockTransformed()
        {
            _touchCount = 0;
        }
    }

    public struct TrailTouchCallbackData
    {
        public readonly int blockIDTransformTo;
        TrailTouchCallbackData( int blockIDTransformTo ) 
        { 
            this.blockIDTransformTo = blockIDTransformTo;
        }
    }

    public struct TrailBlockTouchTransformData
    {
        public readonly AssetLocation code; //Readable Block Code; 
        public readonly int transformOnTouchCount = -1;
        public readonly int transformBlockID = -1;
        public readonly bool transformByPlayerOnly = false;

        public TrailBlockTouchTransformData(AssetLocation code, int transformOnTouchCount, int transformBlockID, bool transformByPlayerOnly )
        {
            this.code = code;
            this.transformOnTouchCount = transformOnTouchCount;
            this.transformBlockID = transformBlockID;
            this.transformByPlayerOnly = transformByPlayerOnly;
        }
    }

    public class TrailChunkManager
    {
        public const long TRAIL_CLEANUP_INTERVAL = 15000; //Run Cleanup every 15 seconds
        public const long TRAIL_POS_MONITOR_TIMEOUT = 900000; //Blocks time out and are cleared from the tracking system after 15 minutes.

        const string TRAIL_MOD_DATA_SAVE_KEY_TOUCH_COUNT = "trailModChunkData_TouchCount";
        const string TRAIL_MOD_DATA_SAVE_KEY_BLOCK_POS = "trailModChunkData_BlockPos";

        public const string TRAIL_MOD_DATA_SAVE_KEY_BLOCK_TRAIL_LAST_TOUCH_DAY = "trailModChunkData_LastTouchDay";
        public const string TRAIL_MOD_DATA_SAVE_KEY_BLOCK_TRAIL_BLOCK_POS = "trailModChunkData_BlockTrailBlockPos";

        const string AIR_CODE = "game:air";
        const string SOIL_CODE = "soil";
        const string SOIL_LOW_NONE_CODE = "soil-low-none";
        const string FOREST_FLOOR_CODE = "forestfloor";
        const string COB_CODE = "cob";
        const string PEAT_CODE = "peat";
        const string CLAY_CODE = "rawclay";
        const string TALLGRASS_END_CODE = "free";
        const string TRAIL_CODE = "trailmod:trail";
        const string TRAIL_WEAR_VARIANT_NEW_CODE = "new";
        const string TRAIL_WEAR_VARIANT_OLD_CODE = "old";
        const string PRETRAIL_START_CODE = "trailmod:soil";
        const string TRAIL_PRETRAIL_END_CODE = "pretrail";
        const string PACKED_DIRT_CODE = "packeddirt";
        const string PACKED_DIRT_ARID_CODE = "drypackeddirt";
        const string STONE_PATH_CODE = "stonepath-free";
        const string TALLGRASS_SHORT_CODE = "tallgrass-short-free";
        const string TALLGRASS_EATEN_CODE = "tallgrass-eaten-free";

        const int FOREST_FLOOR_VARIATION_COUNT = 8;

        private static readonly string[] FERTILITY_VARIANTS = { "high", "compost", "medium", "low", "verylow" };
        private static readonly string[] SOIL_GRASS_VARIANTS = { "normal", "sparse", "verysparse", "none" };
        private static readonly string[] PEAT_AND_CLAY_GRASS_VARIANTS = { "verysparse", "none" };
        private static readonly string[] CLAY_TYPE_VARIANTS = { "blue", "fire", "red" };
        private static readonly string[] TRAIL_WEAR_VARIANTS = { "new", "established", "veryestablished", "old" };
        private static readonly string[] TALLGRASS_LENGTH_VARIANTS = { "verytall", "tall", "medium", "mediumshort", "short", "veryshort" };

        public IWorldAccessor worldAccessor;
        private ICoreServerAPI serverApi;

        object trailModificationLock = new object();

        //Callbacks based on number of block touches, stored by block ID;
        private static Dictionary<int, TrailBlockTouchTransformData> trailBlockTouchTransforms = new Dictionary<int, TrailBlockTouchTransformData>();

        //Current World Trail Data Stored in Memory.
        private Dictionary<IWorldChunk, Dictionary<long, TrailBlockPosEntry>> trailChunkEntries = new Dictionary<IWorldChunk, Dictionary<long, TrailBlockPosEntry>>();

        public static TrailChunkManager trailChunkManagerSingleton;
        private TrailChunkManager()
        {

        }

        public static TrailChunkManager GetTrailChunkManager()
        {
            if ( trailChunkManagerSingleton == null)
                trailChunkManagerSingleton = new TrailChunkManager();
            
            return trailChunkManagerSingleton;
        }

        /*
          _____          _ _   ____        _        
         |_   _| __ __ _(_) | |  _ \  __ _| |_ __ _ 
           | || '__/ _` | | | | | | |/ _` | __/ _` |
           | || | | (_| | | | | |_| | (_| | || (_| |
           |_||_|  \__,_|_|_| |____/ \__,_|\__\__,_|

        */
        public void InitData( IWorldAccessor world, ICoreServerAPI sapi )
        {
            worldAccessor = world;
            serverApi = sapi;
            BuildAllTrailBlockData( world );
        }

        public void ShutdownCleanup()
        {
            trailChunkEntries.Clear();
            trailBlockTouchTransforms.Clear();
            trailChunkManagerSingleton = null;
        }

        public void ShutdownSaveState()
        {
            OnSaveGameSaving();
        }

        public void OnSaveGameLoading()
        {
            
        }

        public void OnSaveGameSaving()
        {
            foreach ( IWorldChunk chunk in trailChunkEntries.Keys ) 
            {
                WriteTrailSaveStateForChunk(chunk);
            }
        }

        public void OnChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
        {
            if (reason == EnumChunkDirtyReason.NewlyLoaded )
            {
                ReadTrailSaveStateForChunk(chunk);
            }
        }

        public void OnChunkColumnUnloaded(Vec3i chunkCoord)
        {
            IWorldChunk chunk = worldAccessor.BlockAccessor.GetChunk(chunkCoord.X, chunkCoord.Y, chunkCoord.Z);

            //Handle the case where the world is pruned of chunks a runtime.
            if (chunk == null)
                return;

            if (trailChunkEntries.ContainsKey(chunk))
            {
                WriteTrailSaveStateForChunk(chunk);
                trailChunkEntries[chunk].Clear();
                trailChunkEntries.Remove(chunk);
            }

        }

        private void WriteTrailSaveStateForChunk( IWorldChunk chunk )
        {
            Debug.Assert(chunk is IServerChunk);
            IServerChunk serverChunk = (IServerChunk)chunk;

            //Store Last Touch Day For Trail Devolution
            List<double> saveBlockTrailLastTouchDay = new List<double>();
            List<BlockPos> saveBlockTrailPos = new List<BlockPos>();

            //Store Touch Count For Trail Evolution
            List<int> saveBlockTouchCounts = new List<int>();
            List<BlockPos> saveBlockPos = new List<BlockPos>();

            Dictionary<long, TrailBlockPosEntry> trailBlockPosEntries = trailChunkEntries[chunk];

            foreach (long trailBlockPosID in trailBlockPosEntries.Keys)
            {
                //Decay our touch count before saving so that we can strip out any non-trail blocks with a fully decayed touch count.
                trailBlockPosEntries[trailBlockPosID].DecayTouchCount(worldAccessor.ElapsedMilliseconds);
                int touchCount = trailBlockPosEntries[trailBlockPosID].GetTouchCount();

                Block blockToSave = chunk.GetLocalBlockAtBlockPos(worldAccessor, trailBlockPosEntries[trailBlockPosID].GetBlockPos());
                if (blockToSave is BlockTrail)
                {
                    BlockTrail blockTrailToSave = (BlockTrail)blockToSave;
                    saveBlockTrailLastTouchDay.Add(blockTrailToSave.GetLastTouchDay());
                    saveBlockTrailPos.Add(trailBlockPosEntries[trailBlockPosID].GetBlockPos());
                }
                else
                {
                    if (touchCount == 0 )
                        continue;
                }

                saveBlockTouchCounts.Add(touchCount);
                saveBlockPos.Add(trailBlockPosEntries[trailBlockPosID].GetBlockPos());
            }

            if (saveBlockTrailPos.Count > 0)
            {
                double[] blockTrailSaveLastTouchDay = saveBlockTrailLastTouchDay.ToArray();
                BlockPos[] blockTrailSaveBlockPos = saveBlockTrailPos.ToArray();

                byte[] lastTouchDayByte = SerializerUtil.Serialize(blockTrailSaveLastTouchDay);
                byte[] blockTrailBlockPosByte = SerializerUtil.Serialize(blockTrailSaveBlockPos);

                serverChunk.SetServerModdata( TRAIL_MOD_DATA_SAVE_KEY_BLOCK_TRAIL_LAST_TOUCH_DAY, lastTouchDayByte );
                serverChunk.SetServerModdata( TRAIL_MOD_DATA_SAVE_KEY_BLOCK_TRAIL_BLOCK_POS, blockTrailBlockPosByte );
            }

            if (saveBlockPos.Count > 0)
            {
                int[] trailBlockSaveTouchCount = saveBlockTouchCounts.ToArray();
                BlockPos[] trailBlockSaveBlockPos = saveBlockPos.ToArray();

                byte[] touchCountByte = SerializerUtil.Serialize<int[]>(trailBlockSaveTouchCount);
                byte[] blockPosByte = SerializerUtil.Serialize<BlockPos[]>(trailBlockSaveBlockPos);


                serverChunk.SetServerModdata(TRAIL_MOD_DATA_SAVE_KEY_TOUCH_COUNT, touchCountByte);
                serverChunk.SetServerModdata(TRAIL_MOD_DATA_SAVE_KEY_BLOCK_POS, blockPosByte);
            }
        }

        private void ReadTrailSaveStateForChunk( IWorldChunk chunk )
        {
            Debug.Assert(chunk is IServerChunk);
            IServerChunk serverChunk = (IServerChunk)chunk;

            byte[] touchCountToLoad = serverChunk.GetServerModdata(TRAIL_MOD_DATA_SAVE_KEY_TOUCH_COUNT);
            byte[] blockPosToLoad = serverChunk.GetServerModdata(TRAIL_MOD_DATA_SAVE_KEY_BLOCK_POS);

            if (touchCountToLoad == null)
                return;

            if ( blockPosToLoad == null) 
                return;

            int[] loadedTouchCount = SerializerUtil.Deserialize<int[]>(touchCountToLoad);
            BlockPos[] loadedBlockPos = SerializerUtil.Deserialize<BlockPos[]>(blockPosToLoad);

            for ( int i = 0; i < loadedBlockPos.Length; i++)
            {
                
                int touchCount = loadedTouchCount[i];
                BlockPos blockPos = loadedBlockPos[i];

                if ( trailChunkEntries.ContainsKey(chunk) )
                {
                    long trailBlockPosID = ConvertBlockPositionToTrailPosID(blockPos);
                    if (trailChunkEntries[chunk].ContainsKey(trailBlockPosID) ) 
                    {
                        Debug.Assert(false, "We should not be loading a duplicate of this data.");
                    }
                    else
                    {
                        TrailBlockPosEntry trailBlockPosEntry = new TrailBlockPosEntry(blockPos, -1, new EntityPos(0, 0, 0), 0, touchCount);
                        trailChunkEntries[chunk].Add(trailBlockPosID, trailBlockPosEntry);
                    }
                }
                else
                {
                    long trailBlockPosID = ConvertBlockPositionToTrailPosID(blockPos);
                    Dictionary<long,TrailBlockPosEntry> trailBlockPosEntries = new Dictionary<long,TrailBlockPosEntry>();
                    trailBlockPosEntries.Add(trailBlockPosID, new TrailBlockPosEntry(blockPos, -1, new EntityPos(0, 0, 0), 0, touchCount));
                    trailChunkEntries.Add(chunk, trailBlockPosEntries);
                }  
            }

            //LOAD LAST TOUCH DAY FOR TRAIL BLOCKS IN THIS CHUNK.
            byte[] lastTouchDayToLoad = serverChunk.GetServerModdata(TrailChunkManager.TRAIL_MOD_DATA_SAVE_KEY_BLOCK_TRAIL_LAST_TOUCH_DAY);
            byte[] blockTrailPosToLoad = serverChunk.GetServerModdata(TrailChunkManager.TRAIL_MOD_DATA_SAVE_KEY_BLOCK_TRAIL_BLOCK_POS);

            if (lastTouchDayToLoad == null)
                return;

            if (blockTrailPosToLoad == null)
                return;

            //We are storing this data in a dictionary so we can access it quickly, but we are not holding onto it once it is loaded.
            double[] loadedLastTouchDay = SerializerUtil.Deserialize<double[]>(lastTouchDayToLoad);
            BlockPos[] loadedBlockTrailPos = SerializerUtil.Deserialize<BlockPos[]>(blockTrailPosToLoad);

            for (  int i = 0; i < loadedBlockTrailPos.Length; i++)
            {
                Block loadedBlock = worldAccessor.BlockAccessor.GetBlock(loadedBlockTrailPos[i]);
                if ( loadedBlock is BlockTrail)
                {
                    BlockTrail loadedTrailBlock = (BlockTrail) loadedBlock;
                    loadedTrailBlock.UpdateLastTrailTouchDayFromLoadedData(loadedLastTouchDay[i], loadedBlockTrailPos[i]);
                }
            }
        }

        private static List<long> timedOutBlocks = new List<long>();
        private static List<IWorldChunk> chunksToRemove = new List<IWorldChunk>();
        public void Clean( float dt )
        {

            chunksToRemove.Clear();
            foreach ( IWorldChunk chunk in trailChunkEntries.Keys )
            {

                timedOutBlocks.Clear();
                foreach ( long blockTrailID in trailChunkEntries[chunk].Keys )
                {
                    TrailBlockPosEntry blockPosEntry = trailChunkEntries[chunk].GetValueSafe( blockTrailID );

                    BlockPos posToCheck = blockPosEntry.GetBlockPos();

                    //Handle case where block position is invalid.
                    if (posToCheck == null)
                    {
                        timedOutBlocks.Add(blockTrailID);
                        continue;
                    }
                        
                    //Handle case where this is an invalid or air block.
                    Block blockToCheck = worldAccessor.BlockAccessor.GetBlock(posToCheck);
                    if ( blockToCheck.Id == 0 )
                    {
                        timedOutBlocks.Add(blockTrailID);
                        continue;
                    }

                    //We never time out trail blocks.
                    if (blockToCheck is BlockTrail)
                        continue;

                    //If the block hasn't been touched in the timeout time, clean it up.
                    if( (worldAccessor.ElapsedMilliseconds - blockPosEntry.lastTouchTime) > TRAIL_POS_MONITOR_TIMEOUT)
                    {
                        timedOutBlocks.Add(blockTrailID);
                    }
                }

                Dictionary<long, TrailBlockPosEntry> trailEntries = trailChunkEntries.GetValueSafe(chunk);

                foreach ( long blockToRemove in timedOutBlocks )
                {
                    trailEntries.Remove( blockToRemove );
                }

                trailChunkEntries[chunk] = trailEntries;

                if (trailChunkEntries[chunk].Values.Count() == 0)
                    chunksToRemove.Add(chunk);
            }

            foreach( IWorldChunk chunk in chunksToRemove)
            {
                trailChunkEntries.Remove( chunk );
            }

            serverApi.Event.RegisterCallback(Clean, ((int)TRAIL_CLEANUP_INTERVAL));
        }

        private string[] BuildSoilBlockVariants()
        {
            int variantIndex = 0;
            string[] soilVariants = new string[FERTILITY_VARIANTS.Count() * SOIL_GRASS_VARIANTS.Count()];
            for ( int fertilityIndex = 0; fertilityIndex < FERTILITY_VARIANTS.Length; fertilityIndex++ ) 
            {
                for (int grassIndex = 0; grassIndex < SOIL_GRASS_VARIANTS.Length; grassIndex++ )
                {
                    soilVariants[variantIndex] = SOIL_CODE + "-" + FERTILITY_VARIANTS[fertilityIndex] + "-" + SOIL_GRASS_VARIANTS[grassIndex];
                    variantIndex++;
                }
            }

            return soilVariants;
        }

        private string[] BuildSoilBlockVariantsFertilityOnly()
        {
            string[] soilVariants = new string[FERTILITY_VARIANTS.Count()];
            for (int fertilityIndex = 0; fertilityIndex < FERTILITY_VARIANTS.Length; fertilityIndex++)
            {
                soilVariants[fertilityIndex] = SOIL_CODE + "-" + FERTILITY_VARIANTS[fertilityIndex];
            }

            return soilVariants;
        }

        private string[] BuildTrailBlockVariantsFertilityOnly()
        {
            string[] trailVariants = new string[FERTILITY_VARIANTS.Count()];
            for (int fertilityIndex = 0; fertilityIndex < FERTILITY_VARIANTS.Length; fertilityIndex++)
            {
                trailVariants[fertilityIndex] = TRAIL_CODE + "-" + FERTILITY_VARIANTS[fertilityIndex];
            }

            return trailVariants;
        }

        private string[] BuildPretrailBlockVariants()
        {
            string[] pretrailVariants = new string[FERTILITY_VARIANTS.Count()];
            for (int fertilityIndex = 0; fertilityIndex < FERTILITY_VARIANTS.Length; fertilityIndex++)
            {
                pretrailVariants[fertilityIndex] = PRETRAIL_START_CODE + "-" + FERTILITY_VARIANTS[fertilityIndex] + "-" + TRAIL_PRETRAIL_END_CODE;
            }

            return pretrailVariants;
        }

        private string[] BuildCobBlockVariants()
        {
            string[] cobVariants = new string[SOIL_GRASS_VARIANTS.Count()];
            for (int grassIndex = 0; grassIndex < SOIL_GRASS_VARIANTS.Length; grassIndex++ )
            {
                cobVariants[grassIndex] = COB_CODE + "-" + SOIL_GRASS_VARIANTS[grassIndex];
            }

            return cobVariants;
        }

        private string[] BuildClayVariantsTypeOnly()
        {
            string[] clayVariants = new string[CLAY_TYPE_VARIANTS.Count()];
            for (int typeIndex = 0; typeIndex < CLAY_TYPE_VARIANTS.Length; typeIndex++)
            {
                clayVariants[typeIndex] = CLAY_CODE + "-" + CLAY_TYPE_VARIANTS[typeIndex];
            }

            return clayVariants;
        }

        private string[] BuildForestFloorVariants()
        {
            
            string[] forestFloorVariants = new string[FOREST_FLOOR_VARIATION_COUNT];
            for ( int i = 0; i < FOREST_FLOOR_VARIATION_COUNT; i++ )
            {
                forestFloorVariants[i] = FOREST_FLOOR_CODE + "-" + i;
            }

            return forestFloorVariants;
        }

        private void BuildAllTrailBlockData( IWorldAccessor world )
        {
            string[] soilBlockCodes = BuildSoilBlockVariants();
            string[] cobBlockCodes = BuildCobBlockVariants();
            string[] forestFloorCodes = BuildForestFloorVariants();

            ValidateTrailBlocks(world, soilBlockCodes);
            ValidateTrailBlocks(world, cobBlockCodes);
            ValidateTrailBlocks(world, forestFloorCodes);
            ValidateTrailBlocks(world, new string[] { PACKED_DIRT_CODE, PACKED_DIRT_ARID_CODE, STONE_PATH_CODE });

            //////////////////////////////////////////////////////////////////////////////////////////
            //PRETRAIL                                                                              //
            //We want pretrails to stay in their fertility category, become trails if walked on     //
            //It evolves until it reaches an old trail, then it stops                               //
            //////////////////////////////////////////////////////////////////////////////////////////
            string[] pretrailFertilityBlockVariants = BuildPretrailBlockVariants();

            int[] pretrailTransformTouchCountByVariant = new int[FERTILITY_VARIANTS.Length];
            bool[] pretrailTransformByPlayerOnlyByVarian = new bool[FERTILITY_VARIANTS.Length];

            for (int i = 0; i < pretrailFertilityBlockVariants.Length; i++ )
            {
                AssetLocation blockAsset = new AssetLocation(pretrailFertilityBlockVariants[i]);
                Block block = world.GetBlock(blockAsset);

                Debug.Assert(block != null);

                string transformCode = TRAIL_CODE + "-" + FERTILITY_VARIANTS[i] + "-" + TRAIL_WEAR_VARIANT_NEW_CODE;
                AssetLocation transformBlockAsset = new AssetLocation(transformCode);
                Block transformBlock = world.GetBlock(transformBlockAsset);

                Debug.Assert(transformBlock != null);

                CreateTrailBlockTransform(blockAsset, block.BlockId, TMGlobalConstants.trampledSoilToNewTrailTouchCount, transformBlock.BlockId, true);
            }


            //////////////////////////////////////////////////////////////////////////////////////////
            //TRAIL                                                                                 //
            //We want trails to stay in their fertility category, but to slowy evolve over time.    //
            //It evolves until it reaches an old trail, then it stops                               //
            //////////////////////////////////////////////////////////////////////////////////////////
            string[] trailFertilityBlockVariants = BuildTrailBlockVariantsFertilityOnly();

            int[] trailTransformTouchCountByVariant = new int[TRAIL_WEAR_VARIANTS.Length];
            bool[] trailTransformByPlayerOnlyByVariant = new bool[TRAIL_WEAR_VARIANTS.Length];

            for (int i = 0; i < TRAIL_WEAR_VARIANTS.Length; i++)
            {
                switch( i )
                {
                    case 0: //New Trail to Established
                        trailTransformTouchCountByVariant[i] = TMGlobalConstants.newToEstablishedTrailTouchCount;
                        trailTransformByPlayerOnlyByVariant[i] = true;
                        break;
                    case 1: //Established Trail to Very Established
                        trailTransformTouchCountByVariant[i] = TMGlobalConstants.establishedToDirtRoadTouchCount;
                        trailTransformByPlayerOnlyByVariant[i] = true;
                        break;
                    case 2: //Very Established Trail to Old Trail
                        trailTransformTouchCountByVariant[i] = TMGlobalConstants.dirtRoadToHighwayTouchCount;
                        trailTransformByPlayerOnlyByVariant[i] = true;
                        break;
                    case 3: //Old Trail to Old Trail
                        trailTransformTouchCountByVariant[i] = 100;
                        trailTransformByPlayerOnlyByVariant[i] = true;
                        break;
                    default:
                        Debug.Assert(false, "invalid index");
                        break;
                }
            }

            //Note: we are intentially assigning the final old trail variant to cyclically trasform into old trail, this is to fix an issue where it degrades because it has not entry and is not counting touches.
            for (int trailFertilityVariantIndex = 0; trailFertilityVariantIndex < trailFertilityBlockVariants.Length; trailFertilityVariantIndex++)
            {
                BuildTrailTouchBlockVariantProgression(world, trailFertilityBlockVariants[trailFertilityVariantIndex], TRAIL_WEAR_VARIANTS, trailTransformTouchCountByVariant, trailTransformByPlayerOnlyByVariant, trailFertilityBlockVariants[trailFertilityVariantIndex] + "-" + TRAIL_WEAR_VARIANT_OLD_CODE);
            }

            //////////////////////////////////////////////////////////////////////////////////////////
            //SOIL                                                                                  //
            //We want soil to stay in it's fertility category, but to slowy strip the grass layer.  //
            //Once it is fully stripped it becomes packed dirt.                                     //
            //////////////////////////////////////////////////////////////////////////////////////////
            string[] soilFertilityBlockVariants = BuildSoilBlockVariantsFertilityOnly();

            int[] soilTransformTouchCountByVariant = new int[SOIL_GRASS_VARIANTS.Length];
            bool[] soilTransformByPlayerOnlyByVariant = new bool[SOIL_GRASS_VARIANTS.Length];
            for( int i = 0; i < soilTransformTouchCountByVariant.Length; i++ ) 
            {
                switch( i )
                {
                    case 0: //Normal to Sparse
                        soilTransformTouchCountByVariant[i] = TMGlobalConstants.normalToSparseGrassTouchCount;
                        soilTransformByPlayerOnlyByVariant[i] = false;
                        break;
                    case 1: //Sparse to Very Sparse
                        soilTransformTouchCountByVariant[i] = TMGlobalConstants.sparseToVerySparseGrassTouchCount;
                        soilTransformByPlayerOnlyByVariant[i] = false;
                        break;
                    case 2: //Very Sparse to None
                        soilTransformTouchCountByVariant[i] = TMGlobalConstants.verySparseToSoilTouchCount;
                        soilTransformByPlayerOnlyByVariant[i] = false;
                        break;
                    case 3: //None to pretrail
                        soilTransformTouchCountByVariant[i] = TMGlobalConstants.soilToTrampledSoilTouchCount;
                        soilTransformByPlayerOnlyByVariant[i] = true; //only players can make new pretrails.
                        break;
                    default:
                        Debug.Assert(false, "invalid index");
                        break;

                }  
            }

            for ( int soilFertilityVariantIndex = 0; soilFertilityVariantIndex < soilFertilityBlockVariants.Length; soilFertilityVariantIndex++ ) 
            {
                //To do rework this to turn into trails.
                BuildTrailTouchBlockVariantProgression(world, soilFertilityBlockVariants[soilFertilityVariantIndex], SOIL_GRASS_VARIANTS, soilTransformTouchCountByVariant, soilTransformByPlayerOnlyByVariant, pretrailFertilityBlockVariants[soilFertilityVariantIndex]);
            }

            ////////////////////////////////////////////////////////////////////
            //COB                                                             //
            //We want cob to strip its grass layer, but to never change type. //
            ////////////////////////////////////////////////////////////////////
            int[] cobTransformTouchCountByVariants = new int[SOIL_GRASS_VARIANTS.Length];
            bool[] cobTransformByPlayerOnlyByVariants = new bool[SOIL_GRASS_VARIANTS.Length];
            for( int i = 0; i < cobTransformTouchCountByVariants.Length; i++)
            {
                //Cob just loses grass over time but never evolves.
                cobTransformTouchCountByVariants[i] = TMGlobalConstants.cobLoseGrassTouchCount;
                cobTransformByPlayerOnlyByVariants[i] = false;
            }

            BuildTrailTouchBlockVariantProgression(world, COB_CODE, SOIL_GRASS_VARIANTS, cobTransformTouchCountByVariants, cobTransformByPlayerOnlyByVariants, "");

            /////////////////////////////////////////////////////////////////////
            //PEAT                                                             //
            //We want peat to strip its grass layer, but to never change type. //
            /////////////////////////////////////////////////////////////////////
            int[] peatTransformTouchCountByVariants = new int[PEAT_AND_CLAY_GRASS_VARIANTS.Length];
            bool[] peatTransformByPlayerOnlyByVariants = new bool[PEAT_AND_CLAY_GRASS_VARIANTS.Length];
            for( int i = 0; i < peatTransformTouchCountByVariants.Length; i++ )
            {
                //Peat just loses surface grass but never evolves.
                peatTransformTouchCountByVariants[i] = TMGlobalConstants.peatLoseGrassTouchCount;
                peatTransformByPlayerOnlyByVariants[i] = false;
            }

            BuildTrailTouchBlockVariantProgression(world, PEAT_CODE, PEAT_AND_CLAY_GRASS_VARIANTS, peatTransformTouchCountByVariants, peatTransformByPlayerOnlyByVariants, "");

            /////////////////////////////////////////////////////////////////////
            //CLAY                                                             //
            //We want clay to strip its grass layer, but to never change type. //
            /////////////////////////////////////////////////////////////////////

            string[] clayTypeBlockVariants = BuildClayVariantsTypeOnly();
            int[] clayTransformTouchCountByVariants = new int[PEAT_AND_CLAY_GRASS_VARIANTS.Length];
            bool[] clayTransformByPlayerOnlyByVariants = new bool[PEAT_AND_CLAY_GRASS_VARIANTS.Length];
            for( int i = 0; i < clayTransformTouchCountByVariants.Length; i++ )
            {
                //Clay just loses surface grass but never evolves.
                clayTransformTouchCountByVariants[i] = TMGlobalConstants.clayLoseGrassTouchCount;
                clayTransformByPlayerOnlyByVariants[i] = false;
            }

            for (int clayTypeVariantIndex = 0; clayTypeVariantIndex < clayTypeBlockVariants.Length; clayTypeVariantIndex++)
            {
                BuildTrailTouchBlockVariantProgression(world, clayTypeBlockVariants[clayTypeVariantIndex], PEAT_AND_CLAY_GRASS_VARIANTS, clayTransformTouchCountByVariants, clayTransformByPlayerOnlyByVariants, "");
            }

            /////////////////////////////////////////////////////////////////////
            //FOREST FLOOR                                                     //
            //We want forest floor to strip, then become low fertility dirt.   //
            /////////////////////////////////////////////////////////////////////
            string[] forestFloorVariants = new string[FOREST_FLOOR_VARIATION_COUNT];
            for ( int i = 0;i < forestFloorVariants.Length; i++)
            {
                forestFloorVariants[ (forestFloorVariants.Length - 1) - i] = i.ToString();
            }

            int[] forestFloorTransformTouchCountByVariants = new int[FOREST_FLOOR_VARIATION_COUNT];
            bool[] forestFloorTransfromByPlayerOnlyByVariants = new bool[FOREST_FLOOR_VARIATION_COUNT];
            for ( int i = 0; i < forestFloorTransformTouchCountByVariants.Length; i++)
            {
                if ( i == 0 )
                {
                    //Forest Floor Full to forest floor sparse.
                    forestFloorTransformTouchCountByVariants[i] = TMGlobalConstants.normalToSparseGrassTouchCount;
                    forestFloorTransfromByPlayerOnlyByVariants[i] = false;
                }
                else if ( i == forestFloorTransformTouchCountByVariants.Length - 1)
                {
                    //Forest floor to low tier soil.
                    forestFloorTransformTouchCountByVariants[i] = TMGlobalConstants.forestFloorToSoilTouchCount;
                    forestFloorTransfromByPlayerOnlyByVariants[i] = false;
                }
                else
                {
                    //Progression through forest floor sparse
                    forestFloorTransformTouchCountByVariants[i] = TMGlobalConstants.sparseToVerySparseGrassTouchCount;
                    forestFloorTransfromByPlayerOnlyByVariants[i] = false;
                } 
            }

            BuildTrailTouchBlockVariantProgression(world, FOREST_FLOOR_CODE, forestFloorVariants, forestFloorTransformTouchCountByVariants, forestFloorTransfromByPlayerOnlyByVariants, SOIL_LOW_NONE_CODE);
        }

        private void ValidateTrailBlocks(IWorldAccessor world, string[] blockCodes )
        {
            foreach( string code in blockCodes ) 
            { 
                AssetLocation blockAsset = new AssetLocation( code );

                Block block = world.GetBlock(blockAsset);

                Debug.Assert( block != null );

                int blockID = block.BlockId;
            }
        }

        private void CreateTrailBlockTransform( AssetLocation blockAsset, int blockID, int transformOnTouchCount, int transformBlockID, bool transformByPlayerOnly)
        {
            Debug.Assert( !trailBlockTouchTransforms.ContainsKey(blockID), "Block " + trailBlockTouchTransforms + " is already registered with the trail block transform system.");

            TrailBlockTouchTransformData touchTransformData = new TrailBlockTouchTransformData(blockAsset, transformOnTouchCount, transformBlockID, transformByPlayerOnly);
            trailBlockTouchTransforms.Add(blockID, touchTransformData);
        }

        private void BuildTrailTouchBlockVariantProgression( IWorldAccessor world, string baseCode, string[] variantCodeProgression, int[] transformOnTouchCountByVariant, bool[] transformByPlayerOnlyByVariant, string variantExitTransformationBlockCode )
        {
            Debug.Assert(variantCodeProgression.Length > 0);
            Debug.Assert( variantCodeProgression.Length == transformOnTouchCountByVariant.Length );

            for ( int variantIndex = 0;  variantIndex < variantCodeProgression.Length; variantIndex++)
            {
                string blockCode = baseCode + "-" + variantCodeProgression[variantIndex];
                string transformBlockCode = "";

                if ( variantIndex == variantCodeProgression.Length - 1 ) 
                    transformBlockCode = variantExitTransformationBlockCode;
                else
                    transformBlockCode = baseCode + "-" + variantCodeProgression[variantIndex + 1];

                if (transformBlockCode == "")
                    continue;

                AssetLocation blockAsset = new AssetLocation(blockCode);
                AssetLocation transformBlockAsset = new AssetLocation( transformBlockCode );

                Block block = world.GetBlock(blockAsset);
                Block transformBlock = world.GetBlock(transformBlockAsset);

                int blockID = block.BlockId;
                int transformBlockID = transformBlock.BlockId;

                CreateTrailBlockTransform(blockAsset, blockID, transformOnTouchCountByVariant[variantIndex], transformBlockID, transformByPlayerOnlyByVariant[variantIndex]);
            }
        }

        /*
          _____          _ _   __  __                                                   _   
         |_   _| __ __ _(_) | |  \/  | __ _ _ __   __ _  __ _  ___ _ __ ___   ___ _ __ | |_ 
           | || '__/ _` | | | | |\/| |/ _` | '_ \ / _` |/ _` |/ _ \ '_ ` _ \ / _ \ '_ \| __|
           | || | | (_| | | | | |  | | (_| | | | | (_| | (_| |  __/ | | | | |  __/ | | | |_ 
           |_||_|  \__,_|_|_| |_|  |_|\__,_|_| |_|\__,_|\__, |\___|_| |_| |_|\___|_| |_|\__|
                                                        |___/                               
        */

        public void AddOrUpdateBlockPosTrailData( IWorldAccessor world, Block block, BlockPos blockPos, Entity touchEnt )
        {
            lock (trailModificationLock)
            {
                IWorldChunk chunk = touchEnt.World.BlockAccessor.GetChunkAtBlockPos(blockPos);
                Debug.Assert(chunk != null);

                //If this block position doesn't contain a block we should monitor, remove it from tracking.
                if (!ShouldTrackBlockTrailData(block))
                    RemoveBlockPosTrailData(world, blockPos);

                bool touchIsPlayer = (touchEnt is EntityPlayer);

                if (TMGlobalConstants.onlyPlayersCreateTrails && !touchIsPlayer)
                    return;

                Debug.Assert(touchEnt is EntityAgent);

                if (!CanEntityTouchBlocks((EntityAgent)touchEnt))
                    return;

                //If the block is only trasformable by players do not count touches or transform when touched by a non-player.
                if (trailBlockTouchTransforms.ContainsKey(block.BlockId))
                {
                    if (trailBlockTouchTransforms[block.BlockId].transformByPlayerOnly && !touchIsPlayer)
                        return;
                }

                long blockTrailID = ConvertBlockPositionToTrailPosID(blockPos);

                if (block is BlockTrail)
                {
                    BlockTrail blockTrail = (BlockTrail)block;
                    blockTrail.TrailBlockTouched(world);
                }

                if (trailChunkEntries.ContainsKey(chunk))
                {
                    if (trailChunkEntries[chunk].ContainsKey(blockTrailID))
                    {
                        TrailBlockPosEntry entryToUpdate = trailChunkEntries[chunk].GetValueSafe(blockTrailID);
                        bool shouldTryTransform = entryToUpdate.BlockTouched(touchEnt.EntityId, touchEnt.ServerPos, touchEnt.World.ElapsedMilliseconds);

                        if (shouldTryTransform)
                        {
                            if (TryToTransformTrailBlock(world, blockPos, block.BlockId, touchEnt, entryToUpdate.GetTouchCount()))
                                entryToUpdate.BlockTransformed();
                        }

                        trailChunkEntries[chunk][blockTrailID] = entryToUpdate;
                    }
                    else
                    {
                        TrailBlockPosEntry trailBlockEntry = new TrailBlockPosEntry(blockPos, touchEnt.EntityId, touchEnt.ServerPos, touchEnt.World.ElapsedMilliseconds, 1);
                        trailChunkEntries[chunk].Add(blockTrailID, trailBlockEntry);

                        if (TryToTransformTrailBlock(world, blockPos, block.BlockId, touchEnt, trailBlockEntry.GetTouchCount()))
                        {
                            trailBlockEntry.BlockTransformed();
                            trailChunkEntries[chunk][blockTrailID] = trailBlockEntry;
                        }

                    }
                }
                else
                {
                    TrailBlockPosEntry trailBlockEntry = new TrailBlockPosEntry(blockPos, touchEnt.EntityId, touchEnt.ServerPos, touchEnt.World.ElapsedMilliseconds, 1);
                    Dictionary<long, TrailBlockPosEntry> trailChunkEntry = new Dictionary<long, TrailBlockPosEntry>();
                    trailChunkEntry.Add(blockTrailID, trailBlockEntry);
                    trailChunkEntries.Add(chunk, trailChunkEntry);

                    if (TryToTransformTrailBlock(world, blockPos, block.BlockId, touchEnt, trailBlockEntry.GetTouchCount()))
                    {
                        trailBlockEntry.BlockTransformed();
                        trailChunkEntries[chunk][blockTrailID] = trailBlockEntry;
                    }
                }
            }
        }

        private void RemoveBlockPosTrailData( IWorldAccessor world, BlockPos blockPos )
        {
            lock (trailModificationLock)
            {
                IWorldChunk chunk = world.BlockAccessor.GetChunkAtBlockPos(blockPos);
                Debug.Assert(chunk != null);

                long blockTrailID = ConvertBlockPositionToTrailPosID(blockPos);

                Debug.Assert(trailChunkEntries.ContainsKey( chunk ) );
                Debug.Assert(trailChunkEntries[chunk].ContainsKey(blockTrailID) );

                trailChunkEntries[chunk].Remove(blockTrailID);

                if ( trailChunkEntries[chunk].Count() ==0 )
                    trailChunkEntries.Remove(chunk);
            }
        }

        public void ClearBlockTouchCount( BlockPos blockPos )
        {
            lock (trailModificationLock)
            {
                IWorldChunk chunk = worldAccessor.BlockAccessor.GetChunkAtBlockPos(blockPos);
                Debug.Assert(chunk != null);

                long blockTrailID = ConvertBlockPositionToTrailPosID(blockPos);

                Debug.Assert(trailChunkEntries.ContainsKey(chunk));
                Debug.Assert(trailChunkEntries[chunk].ContainsKey(blockTrailID));

                trailChunkEntries[chunk][blockTrailID].ClearTouchCount();
            }
        }

        public bool BlockPosHasTrailData( BlockPos blockPos)
        {
            IWorldChunk chunk = worldAccessor.BlockAccessor.GetChunkAtBlockPos(blockPos);
            Debug.Assert(chunk != null);

            long blockTrailID = ConvertBlockPositionToTrailPosID(blockPos);

            if (trailChunkEntries.ContainsKey(chunk))
            {
                if (trailChunkEntries[chunk].ContainsKey(blockTrailID))
                    return true;
            }

            return false;
        }

        public TrailBlockPosEntry GetBlockPosTrailData( BlockPos blockPos )
        {
            IWorldChunk chunk = worldAccessor.BlockAccessor.GetChunkAtBlockPos( blockPos );
            Debug.Assert(chunk != null);

            if (trailChunkEntries.ContainsKey(chunk))
            {
                long blockTrailID = ConvertBlockPositionToTrailPosID(blockPos);

                if (trailChunkEntries[chunk].ContainsKey(blockTrailID))
                    return trailChunkEntries[chunk][blockTrailID];
            }

            Debug.Assert(false, "BlockPos does not have trail data, call BlockPosHasTrailData to check before calling this function.");

            return new TrailBlockPosEntry();
        }

        //Save From Mod Chunk

        //Load From Mod Chunk

        //Utility
        private bool TryToTransformTrailBlock( IWorldAccessor world, BlockPos blockPos, int blockID, Entity touchEnt, int touchCount )
        {
            lock (trailModificationLock)
            {
                if (trailBlockTouchTransforms.ContainsKey(blockID))
                {
                    TrailBlockTouchTransformData trailBlockTransformData = trailBlockTouchTransforms[blockID];

                    bool touchIsPlayer = false;
                    bool touchIsSneaking = false;

                    if (touchEnt is EntityPlayer)
                    {
                        touchIsPlayer = true;

                        EntityPlayer touchPlayer = (EntityPlayer)touchEnt;

                        if (touchPlayer.Controls.Sneak)
                            touchIsSneaking = true;
                    }

                    if (!touchIsSneaking)
                    {
                        BlockPos upPos = blockPos.UpCopy();
                        Block upBlock = world.BlockAccessor.GetBlock(upPos);
                        Block groundBlock = world.BlockAccessor.GetBlock(blockPos);
                        if (upBlock != null)
                        {
                            ETrailTrampleType trampleType = CanTramplePlant(upBlock, upPos, groundBlock, blockPos, touchEnt);
                            if (trampleType != ETrailTrampleType.NO_TRAMPLE)
                            {
                                ResolveTrampleType(world, trampleType, upPos, upBlock, groundBlock);
                            }
                        }
                    }

                    if (trailBlockTransformData.transformByPlayerOnly && !touchIsPlayer)
                        return false;

                    if (touchCount >= trailBlockTransformData.transformOnTouchCount)
                    {
                        Dictionary<int,Block> decors = world.BlockAccessor.GetSubDecors(blockPos);
                        if (decors != null)
                        {
                            foreach (KeyValuePair<int,Block> kvp in decors)
                            {
                                world.BlockAccessor.SetDecor(world.GetBlock(0), blockPos, kvp.Key);
                            }
                        }
                        world.BlockAccessor.SetBlock(trailBlockTransformData.transformBlockID, blockPos);
                        if (decors != null)
                        {
                            foreach (KeyValuePair<int,Block> kvp in decors)
                            {
                                world.BlockAccessor.SetDecor(kvp.Value, blockPos, kvp.Key);
                            }
                        }

                        return true;
                    }
                }

                return false;
            }
        }

        public bool BlockCenterHorizontalInEntityBoundingBox(Entity ent, BlockPos blockPos ) 
        {
            if (blockPos == null )
                return false;

            if ( ent == null )
                return false;

            if ( ent.Code == null )
                return false;

            EntityProperties agentProperties = ent.World.GetEntityType(ent.Code);

            //Hande the case where the entry is invalid, or has been disabled by mod flags.
            if (agentProperties == null)
                return false;

            Vec2f selBox = agentProperties.SelectionBoxSize;

            //Selection box defaults to null, so an entity agent with no selection box would crash this, unless we early out.
            if ( selBox == null )
                return false;

            Vec3d posDeltaFlat = blockPos.ToVec3d() - ent.ServerPos.XYZ;

            float boundsMin = -Math.Max(1, selBox.X);
            float boundsMax = Math.Max(1, selBox.X);

            if (boundsMin < posDeltaFlat.X && boundsMax > posDeltaFlat.X &&
                boundsMin < posDeltaFlat.Z && boundsMax > posDeltaFlat.Z)
                return true;

            return false;
        }

        public bool ShouldTrackBlockTrailData( Block block ) 
        {
            if( trailBlockTouchTransforms.ContainsKey(block.Id) )
                return true;

            if (block.BlockMaterial == EnumBlockMaterial.Snow)
                return true;

            if (block.BlockMaterial == EnumBlockMaterial.Ice)
                return true;

            return false;
        }

        public static long ConvertBlockPositionToTrailPosID( BlockPos blockPos )
        {
            long XY = AppendDigits(blockPos.X, blockPos.Y);
            long XYZ = AppendDigits(XY, blockPos.Z);

            //Remove this when we are confindent this function returns good values consistently.
            //long concatTest = (blockPos.X.ToString() + blockPos.Y.ToString() + blockPos.Z.ToString()).ToLong();
            //Debug.Assert(XYZ == concatTest);

            return XYZ;
        }

        private static long AppendDigits(long value1, long value2)
        {
            long dn = (long)(Math.Ceiling(Math.Log10(value2 + 0.001)));     //0.001 is for exact 10, exact 100, ...
            long finalVal = (long)(value1 * Math.Ceiling(Math.Pow(10, dn))); //< ----because pow would give 99.999999(for some optimization modes)
            finalVal += value2;
            return finalVal;
        }

        private static int CountDigits( long number )
        {
            int digits = 0;
            while ( number > 0 )
            {
                number /= 10;
                digits++;
            }

            return digits;
        }

        private static ETrailTrampleType CanTramplePlant( Block plantBlock, BlockPos plantPos, Block groundBlock, BlockPos groundPos, Entity touchEnt )
        {
            bool groundIsTrail = groundBlock is BlockTrail;

            ModSystemTrampleProtection modTramplePro = touchEnt.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();

            if (modTramplePro.IsTrampleProtected(groundPos))
                return ETrailTrampleType.NO_TRAMPLE;

            if (modTramplePro.IsTrampleProtected(plantPos))
                return ETrailTrampleType.NO_TRAMPLE;

            if (plantBlock.BlockMaterial == EnumBlockMaterial.Plant)
            {
                if (plantBlock is BlockTallGrass)
                {
                    if (!TMGlobalConstants.onlyTrampleGrassOnTrailCreation || groundIsTrail)
                        return ETrailTrampleType.TALLGRASS;
                }
                    
                if (TMGlobalConstants.flowerTrampling)
                {
                    if (plantBlock is BlockLupine)
                        if (!TMGlobalConstants.onlyTrampleFlowersOnTrailCreation || groundIsTrail)
                            return ETrailTrampleType.DEFAULT;
                }

                string code = plantBlock.Code.FirstCodePart();

                if (TMGlobalConstants.flowerTrampling)
                {
                    if (code == "flower" && groundIsTrail)
                        if (!TMGlobalConstants.onlyTrampleFlowersOnTrailCreation || groundIsTrail)
                            return ETrailTrampleType.DEFAULT;
                }

                if (TMGlobalConstants.fernTrampling)
                {
                    if (!TMGlobalConstants.onlyTrampleFernsOnTrailCreation || groundIsTrail)
                    {
                        if (plantBlock is BlockFern && groundIsTrail)
                            return ETrailTrampleType.DEFAULT;

                        if (code == "tallfern" && groundIsTrail)
                            return ETrailTrampleType.DEFAULT;
                    }
                }
            }

            return ETrailTrampleType.NO_TRAMPLE;
        }

        private static bool ShouldDropPlantOnTrample(Block block)
        {
            if (block.BlockMaterial == EnumBlockMaterial.Plant)
            {
                if (block is BlockFern)
                    return false;

                if (block is BlockTallGrass)
                    return false;

                if (block is BlockLupine)
                    return true;

                string code = block.Code.FirstCodePart();

                if (code == "flower")
                    return true;

                if (code == "tallfern")
                    return false;
            }

            return false;
        }
        private static void ResolveTrampleType(IWorldAccessor world, ETrailTrampleType trampleType, BlockPos upPos, Block upBlock, Block groundBlock )
        {

            switch(trampleType)
            {
                case ETrailTrampleType.DEFAULT:
                    float dropRate = ShouldDropPlantOnTrample(upBlock) ? 1.0f : 0.0f;
                    if (TMGlobalConstants.foliageTrampleSounds || dropRate > 0)
                    {
                        world.BlockAccessor.BreakBlock(upPos, null, dropRate);
                    }
                    else
                    {
                        AssetLocation assetLocation = new AssetLocation(AIR_CODE);
                        Block airBlock = world.GetBlock(assetLocation);
                        world.BlockAccessor.SetBlock(airBlock.Id, upPos);
                    }
                    break;

                case ETrailTrampleType.TALLGRASS:

                    bool groundIsTrail = groundBlock is BlockTrail;


                    if ( groundIsTrail)
                    {
                        if (TMGlobalConstants.foliageTrampleSounds)
                        {
                            world.BlockAccessor.BreakBlock(upPos, null, 0);
                        }
                        else
                        {
                            AssetLocation assetLocation = new AssetLocation(AIR_CODE);
                            Block airBlock = world.GetBlock(assetLocation);
                            world.BlockAccessor.SetBlock(airBlock.Id, upPos);
                        }                        
                    }
                    else
                    {
                        //We want to imediately trample grass on clay and peat deposits.
                        if ( groundBlock is BlockSoilDeposit )
                        {
                            if (TMGlobalConstants.foliageTrampleSounds)
                            {
                                world.BlockAccessor.BreakBlock(upPos, null, 0);
                            }
                            else
                            {
                                AssetLocation assetLocation = new AssetLocation(AIR_CODE);
                                Block airBlock = world.GetBlock(assetLocation);
                                world.BlockAccessor.SetBlock(airBlock.Id, upPos);
                            }

                            return;
                        }

                        if ( upBlock.Code.Path != TALLGRASS_EATEN_CODE )
                        {
                            AssetLocation assetLocation = new AssetLocation(TALLGRASS_EATEN_CODE);
                            Block grassBlock = world.GetBlock(assetLocation);
                            world.BlockAccessor.SetBlock(grassBlock.Id, upPos);
                        }                        
                    }

                    break;

            }
        }

        private bool CanEntityTouchBlocks( EntityAgent touchEnt )
        {
            Vec2f selBox = touchEnt.Properties.SelectionBoxSize;

            if ( selBox == null )
                return false;

            if (selBox.X < TMGlobalConstants.minEntityHullSizeToTrampleX)
                return false;

            if (selBox.Y < TMGlobalConstants.minEntityHullSizeToTrampleY)
                return false;

            return true;
        }

    }
}
