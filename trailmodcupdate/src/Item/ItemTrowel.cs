using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace TrailMod
{
    public class ItemTrowel : Item
    {
        const int TROWL_HIGHLIGHT_SLOT_ID = 9001;


        WorldInteraction[] interactions;
        ICoreClientAPI capi;

        List<BlockPos> blockHighlightPositions = new List<BlockPos>();
        List<int> blockHighlightColors = new List<int>();

        private const int DRAW_RADIUS = 20;

        public override void OnLoaded(ICoreAPI api)
        {
            if (api.Side != EnumAppSide.Client) 
                return;

            capi = api as ICoreClientAPI;

            interactions = ObjectCacheUtil.GetOrCreate(api, "trowelInteractions", () =>
            {
                return new WorldInteraction[]
                {
                    new WorldInteraction()
                    {
                        ActionLangCode = "trailmod:heldhelp-trampleprotectblock",
                        MouseButton = EnumMouseButton.Right,
                    },
                    new WorldInteraction()
                    {
                        ActionLangCode = "trailmod:heldhelp-removetrampleprotection",
                        MouseButton = EnumMouseButton.Left,
                    }
                };
            });
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            
            if (handling == EnumHandHandling.PreventDefault) 
                return;

            if (byEntity.World.Side == EnumAppSide.Client)
            {
                handling = EnumHandHandling.PreventDefaultAction;
                return;
            }

            if (blockSel == null)
            {
                return;
            }

            ModSystemTrampleProtection modTramplePro = byEntity.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();

            IPlayer player = (byEntity as EntityPlayer).Player;
            
            if (player == null) 
                return;

            int toolMode = slot.Itemstack.Attributes.GetInt("toolMode");
            int groupUid = 0;
            var groups = player.GetGroups();
            
            if (toolMode > 0 && toolMode - 1 < groups.Length)
            {
                groupUid = groups[toolMode - 1].GroupUid;
            }

            if (!api.World.BlockAccessor.GetBlock(blockSel.Position).HasBehavior<BlockBehaviorTrampleProtection>())
            {
                (player as IServerPlayer).SendIngameError("nottrampleprotectable", "This block can not be trample protected!");
                return;
            }

            if ( modTramplePro.IsTrampleProtected( blockSel.Position ) )
            {
                (player as IServerPlayer).SendIngameError("alreadytrampleprotected", "Cannot trample protect block, it's already protected!");
                return;
            }

            modTramplePro.TryAddTrampleProtection(blockSel.Position, player);

            BlockPos pos = blockSel.Position;
            byEntity.World.PlaySoundAt(new AssetLocation("sounds/tool/reinforce"), pos.X, pos.Y, pos.Z, null);

            handling = EnumHandHandling.PreventDefaultAction;
            if (byEntity.World.Side == EnumAppSide.Client) 
                ((byEntity as EntityPlayer)?.Player as IClientPlayer).TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        }


        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            if (byEntity.World.Side == EnumAppSide.Client)
            {
                handling = EnumHandHandling.PreventDefaultAction;
                return;
            }

            if (blockSel == null)
            {
                return;
            }

            ModSystemTrampleProtection modTramplePro = byEntity.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();
            
            IServerPlayer player = (byEntity as EntityPlayer).Player as IServerPlayer;
            
            if (player == null) 
                return;

            if ( modTramplePro.IsTrampleProtected( blockSel.Position ) )
            {
                string errorCode = "";
                modTramplePro.TryRemoveTrampleProtection(blockSel.Position, player, ref errorCode);
            }

            BlockPos pos = blockSel.Position;
            byEntity.World.PlaySoundAt(new AssetLocation("sounds/tool/reinforce"), pos.X, pos.Y, pos.Z, null);

            handling = EnumHandHandling.PreventDefaultAction;
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return interactions.Append(base.GetHeldInteractionHelp(inSlot));
        }
    }
}
