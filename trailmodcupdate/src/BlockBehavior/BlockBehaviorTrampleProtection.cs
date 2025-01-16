using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using static HarmonyLib.Code;

namespace TrailMod
{
    public class BlockBehaviorTrampleProtection : BlockBehavior
    {

        public BlockBehaviorTrampleProtection(Block block) : base(block)
        {
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, ref EnumHandling handling)
        {
            //Remove Trample Protection From Block.
            ModSystemTrampleProtection modTramplePro;
            modTramplePro = world.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();

            if (modTramplePro.IsTrampleProtected(pos) )
            {
                string errorCode = "";
                modTramplePro.TryRemoveTrampleProtection(pos, byPlayer, ref errorCode);
            }

            base.OnBlockBroken(world, pos, byPlayer, ref handling);
        }

        public override void OnBlockExploded(IWorldAccessor world, BlockPos pos, BlockPos explosionCenter, EnumBlastType blastType, ref EnumHandling handling)
        {
            //Remove Trample Protection From Block.
            ModSystemTrampleProtection modTramplePro;
            modTramplePro = world.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();

            if (modTramplePro.IsTrampleProtected(pos))
                modTramplePro.ClearTrampleProtection(pos);

            base.OnBlockExploded(world, pos, explosionCenter, blastType, ref handling);
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
        {
            if (world.Side == EnumAppSide.Server)
            {
                //Remove Trample Protection From Block.
                ModSystemTrampleProtection modTramplePro;
                modTramplePro = world.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();

                if (modTramplePro.IsTrampleProtected(pos))
                    modTramplePro.ClearTrampleProtection(pos);
            }
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            //Verify if block at position has trample protection.
            ModSystemTrampleProtection modTramplePro;
            modTramplePro = world.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();

            if ( modTramplePro.IsTrampleProtected(pos) )
            {
                TrampleProtection trampleProtection = modTramplePro.GetTrampleProtection(pos);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(Lang.Get("Has been trample protected by {0}.", trampleProtection.LastPlayername));

                return sb.ToString();
            }

            return null;
        }
    }
}
