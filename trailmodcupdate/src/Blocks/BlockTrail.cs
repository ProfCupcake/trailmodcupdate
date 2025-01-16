using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace TrailMod
{

    public class BlockTrail : Block
    {
        private const string SOIL_CODE = "soil";
        private const string SOIL_GRASS_NONE_CODE = "none";
        private const string SOIL_GRASS_SPARSE_CODE = "sparse";
        private const string PRETRAIL_START_CODE = "soil"; //We are intentionally not including the trailmod: domain here becauase it is appended to the string at runtime.
        private const string PRETRAIL_END_CODE = "pretrail";

        private readonly string[] trailVariants = { "pretrail", "new", "established", "veryestablished", "old" };

        //To Do: Devolve Trails Over Time
        double lastTrailTouchDay = 0;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
        }

        public override void OnServerGameTick(IWorldAccessor world, BlockPos pos, object extra = null)
        {
            base.OnServerGameTick(world, pos, extra);

            string endVariant = this.Code.EndVariant();

            if (world.Calendar.ElapsedDays - lastTrailTouchDay >= GetTrailDevolveDays(endVariant) )
            {
                TrailChunkManager trailChunkManager = TrailChunkManager.GetTrailChunkManager();

                //Devolve the block to the previous level.
                int wearVariantID = GetTrailWearIndexFromWearCode(endVariant);

                double daysSinceTouched = trailChunkManager.worldAccessor.Calendar.ElapsedDays - lastTrailTouchDay;

                double devolveDays = GetTrailDevolveDays(endVariant);
                int devolveLevels = (int)(daysSinceTouched / devolveDays); //This should round down, not up.

                if (devolveLevels == 0)
                    return;

                int startingLevel = GetTrailWearIndexFromWearCode(endVariant);
                int finalLevel = startingLevel - devolveLevels;
                string devolveBlockCode = "";

                //If we are a new trail devolving to pretrail.
                if (finalLevel > 0)
                {
                     string baseCode = this.CodeWithoutParts(1);
                     string newWearVariantCode = trailVariants[finalLevel];
                     devolveBlockCode = this.Code.ShortDomain() + ":" + baseCode + "-" + newWearVariantCode;
                }
                else if (finalLevel == 0 ) 
                {
                    string fertilityVariantCode = this.Code.SecondCodePart();
                    devolveBlockCode = this.Code.ShortDomain() + ":" + PRETRAIL_START_CODE + "-" + fertilityVariantCode + "-" + PRETRAIL_END_CODE;
                }
                else if ( finalLevel < 0 ) 
                {
                    string fertilityVariantCode = this.Code.SecondCodePart();
                    devolveBlockCode = SOIL_CODE + "-" + fertilityVariantCode + "-" + SOIL_GRASS_NONE_CODE;
                }

                Debug.Assert(devolveBlockCode != "");

                AssetLocation devolveBlockAsset = new AssetLocation(devolveBlockCode);
                Block devolveBlock = world.GetBlock(devolveBlockAsset);
                
                Debug.Assert( devolveBlock != null );

                SetLastTrailTouchDay(world.Calendar.ElapsedDays);
                world.BlockAccessor.SetBlock(devolveBlock.Id, pos);

                if (trailChunkManager.BlockPosHasTrailData(pos) )
                    trailChunkManager.ClearBlockTouchCount(pos);
   
            }
        }

        public override bool ShouldReceiveServerGameTicks(IWorldAccessor world, BlockPos pos, Random offThreadRandom, out object extra)
        {
            //Look at making this more efficient.
            extra = null;
            return true; //base.ShouldReceiveServerGameTicks(world, pos, offThreadRandom, out extra);
        }

        public void UpdateLastTrailTouchDayFromLoadedData( double lastTouchDay, BlockPos pos )
        {
            //Make sure this only ever runs on the server.
            
            lastTrailTouchDay = lastTouchDay;

            TrailChunkManager trailChunkManager = TrailChunkManager.GetTrailChunkManager();

            double daysSinceTouched = trailChunkManager.worldAccessor.Calendar.ElapsedDays - lastTrailTouchDay;

            string endVariant = this.Code.EndVariant();
            double devolveDays = GetTrailDevolveDays(endVariant);
            int devolveLevels = (int)(daysSinceTouched / devolveDays); //This should round down, not up.
            
            if (devolveLevels == 0)
                return;
            
            int startingLevel = GetTrailWearIndexFromWearCode(endVariant);
            int finalLevel = startingLevel - devolveLevels;

            if (finalLevel > 0 ) 
            {
                //Devolve the block to the previous level.
                int wearVariantID = GetTrailWearIndexFromWearCode(endVariant);

                string baseCode = this.CodeWithoutParts(1);
                string newWearVariantCode = trailVariants[finalLevel];
                string devolveBlockCode = baseCode + "-" + newWearVariantCode;

                AssetLocation devolveBlockAsset = new AssetLocation(this.Code.ShortDomain() + ":" + devolveBlockCode);
                Block devolveBlock = trailChunkManager.worldAccessor.GetBlock(devolveBlockAsset);

                Debug.Assert(devolveBlock != null);

                SetLastTrailTouchDay(trailChunkManager.worldAccessor.Calendar.ElapsedDays);
                trailChunkManager.worldAccessor.BlockAccessor.SetBlock(devolveBlock.Id, pos);

                if (trailChunkManager.BlockPosHasTrailData(pos))
                    trailChunkManager.ClearBlockTouchCount(pos);
            }
            //if we are new trail devolving to pretrail.
            else if ( finalLevel == 0 )
            {
                string fertilityVariantCode = this.Code.SecondCodePart();
                string devolveBlockCode = PRETRAIL_START_CODE + "-" + fertilityVariantCode + "-" + PRETRAIL_END_CODE;

                AssetLocation devolveBlockAsset = new AssetLocation(this.Code.ShortDomain() + ":" + devolveBlockCode);
                Block devolveBlock = trailChunkManager.worldAccessor.GetBlock(devolveBlockAsset);

                Debug.Assert(devolveBlock != null);

                SetLastTrailTouchDay(trailChunkManager.worldAccessor.Calendar.ElapsedDays);
                trailChunkManager.worldAccessor.BlockAccessor.SetBlock(devolveBlock.Id, pos);

                if (trailChunkManager.BlockPosHasTrailData(pos))
                    trailChunkManager.ClearBlockTouchCount(pos);
            }
            //if we are native soil
            else if(finalLevel < 0 )
            {
                string fertilityVariantCode = this.Code.SecondCodePart();

                string devolveToSoilCode = SOIL_CODE + "-" + fertilityVariantCode + "-" + SOIL_GRASS_SPARSE_CODE;

                AssetLocation devolveSoilBlockAsset = new AssetLocation(devolveToSoilCode);

                Block devolveSoilBlock = trailChunkManager.worldAccessor.GetBlock(devolveSoilBlockAsset);

                Debug.Assert(devolveSoilBlock != null);

                SetLastTrailTouchDay(trailChunkManager.worldAccessor.Calendar.ElapsedDays);
                trailChunkManager.worldAccessor.BlockAccessor.SetBlock(devolveSoilBlock.Id, pos);

                if (trailChunkManager.BlockPosHasTrailData(pos))
                    trailChunkManager.ClearBlockTouchCount(pos);
            }
        }

        private double GetTrailDevolveDays( string wearVariant )
        {

            if ( wearVariant == "pretrail")
                return TMGlobalConstants.trampledSoilDevolveDays;

            return TMGlobalConstants.trailDevolveDays;
        }

        private int GetTrailWearIndexFromWearCode( string wearCode )
        {
            switch( wearCode )
            {
                case "pretrail":
                    return 0;
                case "new":
                    return 1;
                case "established":
                    return 2;
                case "veryestablished":
                    return 3;
                case "old":
                    return 4;
            }

            Debug.Assert(false, "Wear code is invalid.");

            return -1;
        }

        private void SetLastTrailTouchDay( double lastTouchDay ) 
        {
            lastTrailTouchDay = lastTouchDay;
        }

        public void TrailBlockTouched( IWorldAccessor world )
        {
            SetLastTrailTouchDay(world.Calendar.ElapsedDays);
        }

        public double GetLastTouchDay()
        {
            return lastTrailTouchDay;
        }
    }
}
