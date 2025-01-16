using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace TrailMod
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class TrampleProtection
    {
        public string PlayerUID;
        public string LastPlayername;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ChunkTrampleProtectionData
    {
        public byte[] Data;
        public int chunkX, chunkY, chunkZ;
    }

    public class ModSystemTrampleProtection : ModSystem
    {
        private const string TRAMPLE_PROTECTION_MODDATA = "trailprotection";
        private const string TRAMPLE_PROTECTION_CHANNEL = "trailprotection";

        ICoreAPI api;
        IServerNetworkChannel serverChannel;
        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return true;
        }

        public override void Start(ICoreAPI api)
        {
            this.api = api;

            api.RegisterItemClass("ItemTrowel", typeof(ItemTrowel));
            api.RegisterBlockBehaviorClass("BlockBehaviorTrampleProtection", typeof(BlockBehaviorTrampleProtection));
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            // Needs to be done before assets are ready because it rewrites Behavior and CollectibleBehavior
            if (api.Side == EnumAppSide.Server) // No need to add it twice on the client
            {
                AddTrampleProtectionBehavior();
            }
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            api.Network
                .RegisterChannel(TRAMPLE_PROTECTION_CHANNEL)
                .RegisterMessageType(typeof(ChunkTrampleProtectionData))
                .SetMessageHandler<ChunkTrampleProtectionData>(OnChunkData)
            ;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            serverChannel = api.Network
                .RegisterChannel(TRAMPLE_PROTECTION_CHANNEL)
                .RegisterMessageType(typeof(ChunkTrampleProtectionData))
            ;
        }

        private void OnChunkData(ChunkTrampleProtectionData msg)
        {
            IWorldChunk chunk = api.World.BlockAccessor.GetChunk(msg.chunkX, msg.chunkY, msg.chunkZ);
            if (chunk != null)
            {
                chunk.SetModdata(TRAMPLE_PROTECTION_MODDATA, msg.Data);
            }
        }

        private void AddTrampleProtectionBehavior()
        {
            foreach (Block block in api.World.Blocks)
            {
                if (block.Code == null || block.Id == 0) continue;

                if (CanHaveTrampleProtection(block))
                {
                    block.BlockBehaviors = block.BlockBehaviors.Append(new BlockBehaviorTrampleProtection(block));
                    block.CollectibleBehaviors = block.CollectibleBehaviors.Append(new BlockBehaviorTrampleProtection(block));
                }
            }
        }

        protected bool CanHaveTrampleProtection(Block block)
        {
            if ( block.BlockMaterial == EnumBlockMaterial.Soil ||
                block.BlockMaterial == EnumBlockMaterial.Plant)
                return true;

            return false;
        }

        public bool TryAddTrampleProtection( BlockPos pos, IPlayer forPlayer )
        {
            Dictionary<int, TrampleProtection> trampleProtectionsOfChunk = GetOrCreateTrampleProtectionAt(pos);

            if (forPlayer == null)
                return false;

            if (trampleProtectionsOfChunk == null)
                return false;

            int index3d = toLocalIndex(pos);

            TrampleProtection tramplePro = new TrampleProtection();
            tramplePro.PlayerUID = forPlayer.PlayerUID;
            tramplePro.LastPlayername = forPlayer.PlayerName;

            trampleProtectionsOfChunk.Add(index3d, tramplePro);
            SaveTrampleProtection(trampleProtectionsOfChunk, pos);

            //Quality of Life: If the block we just protected is a plant, see if the block below it is soil and protect that as well.
            Block protectedBlock = this.api.World.BlockAccessor.GetBlock(pos);

            if ( protectedBlock.Id != 0 )
            {
                if ( protectedBlock.BlockMaterial == EnumBlockMaterial.Plant )
                {
                    BlockPos downCopy = pos.DownCopy();
                    Block downBlock = this.api.World.BlockAccessor.GetBlock(downCopy);

                    if ( downBlock.Id != 0 )
                    {
                        if ( CanHaveTrampleProtection(downBlock) )
                        {
                            if ( downBlock.BlockMaterial == EnumBlockMaterial.Soil )
                            {
                                TrampleProtection downBlockTramplePro = new TrampleProtection();
                                downBlockTramplePro.PlayerUID = forPlayer.PlayerUID;
                                downBlockTramplePro.LastPlayername = forPlayer.PlayerName;

                                Dictionary<int, TrampleProtection> downTrampleProtectionsOfChunk = GetOrCreateTrampleProtectionAt(downCopy);

                                int downIndex3d = toLocalIndex(downCopy);

                                if (!downTrampleProtectionsOfChunk.ContainsKey(downIndex3d))
                                { 
                                    downTrampleProtectionsOfChunk.Add(downIndex3d, downBlockTramplePro);
                                    SaveTrampleProtection(downTrampleProtectionsOfChunk, downCopy);
                                }                                
                            }
                        }
                    }                    
                }
            }

            return true;
        }

        public bool TryRemoveTrampleProtection(BlockPos pos, IPlayer forPlayer, ref string errorCode)
        {
            Dictionary<int, TrampleProtection> trampleProtectionsOfChunk = GetOrCreateTrampleProtectionAt(pos);
            
            if (trampleProtectionsOfChunk == null) 
                return false;

            int index3d = toLocalIndex(pos);
            if (!trampleProtectionsOfChunk.ContainsKey(index3d))
            {
                errorCode = "nottrampleprotected";
                return false;
            }

            trampleProtectionsOfChunk.Remove(index3d);

            SaveTrampleProtection(trampleProtectionsOfChunk, pos);
            return true;
        }

        public void ClearTrampleProtection(BlockPos pos)
        {
            Dictionary<int, TrampleProtection> trampleProtectionsOfChunk = GetOrCreateTrampleProtectionAt(pos);
            if (trampleProtectionsOfChunk == null) return;

            int index3d = toLocalIndex(pos);
            if (!trampleProtectionsOfChunk.ContainsKey(index3d)) return;

            if (trampleProtectionsOfChunk.Remove(index3d))
            {
                SaveTrampleProtection(trampleProtectionsOfChunk, pos);
            }
        }

        public bool IsTrampleProtected(BlockPos pos)
        {
            Dictionary<int, TrampleProtection> trampleProtectionsOfChunk = GetOrCreateTrampleProtectionAt(pos);
            
            if (trampleProtectionsOfChunk == null) 
                return false;
            
            int index3d = toLocalIndex(pos);

            return trampleProtectionsOfChunk.ContainsKey(index3d);
        }

        Dictionary<int, TrampleProtection> GetOrCreateTrampleProtectionAt(BlockPos pos)
        {
            byte[] data;

            IWorldChunk chunk = api.World.BlockAccessor.GetChunkAtBlockPos(pos);
            
            if (chunk == null) 
                return null;

            data = chunk.GetModdata(TRAMPLE_PROTECTION_MODDATA);

            Dictionary<int, TrampleProtection> trampleProtectionsOfChunk;

            if (data != null)
            {
                try
                {
                    trampleProtectionsOfChunk = SerializerUtil.Deserialize<Dictionary<int, TrampleProtection>>(data);
                }
                catch (Exception)
                {
                    trampleProtectionsOfChunk = new Dictionary<int, TrampleProtection>();
                }
            }
            else
            {
                trampleProtectionsOfChunk = new Dictionary<int, TrampleProtection>();
            }

            return trampleProtectionsOfChunk;
        }

        public TrampleProtection GetTrampleProtection(BlockPos pos)
        {
            Dictionary<int, TrampleProtection> trampleProtectionsOfChunk = GetOrCreateTrampleProtectionAt(pos);
            
            if (trampleProtectionsOfChunk == null) 
                return null;

            int index3d = toLocalIndex(pos);
            
            if (!trampleProtectionsOfChunk.ContainsKey(index3d)) 
                return null;

            return trampleProtectionsOfChunk[index3d];
        }

        private void SaveTrampleProtection(Dictionary<int, TrampleProtection> reif, BlockPos pos)
        {
            const int chunksize = GlobalConstants.ChunkSize;
            int chunkX = pos.X / chunksize;
            int chunkY = pos.Y / chunksize;
            int chunkZ = pos.Z / chunksize;

            byte[] data = SerializerUtil.Serialize(reif);

            IWorldChunk chunk = api.World.BlockAccessor.GetChunk(chunkX, chunkY, chunkZ);
            chunk.SetModdata(TRAMPLE_PROTECTION_MODDATA, data);

            // Todo: Send only to players that have this chunk in their loaded range
            serverChannel?.BroadcastPacket(new ChunkTrampleProtectionData() { chunkX = chunkX, chunkY = chunkY, chunkZ = chunkZ, Data = data });
        }

        private int toLocalIndex(BlockPos pos)
        {
            return toLocalIndex(pos.X % GlobalConstants.ChunkSize, pos.Y % GlobalConstants.ChunkSize, pos.Z % GlobalConstants.ChunkSize);
        }

        private int toLocalIndex(int x, int y, int z)
        {
            return (y << 16) | (z << 8) | (x);
        }
    }
}
