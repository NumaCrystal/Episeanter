using Episeanter.Content.Shared.Maps.Grids;
using Episeanter.Content.Shared.Maps.Network;
using Mirror;
using UnityEngine;

namespace Episeanter.Content.Client.Maps
{
    public sealed class ClientGridModule : GridModule<ClientGridSystem>
    {
        public static ClientGridModule Instance { get; } = new();

        private ClientGridModule() { }

        protected override void RegisterHandlers()
        {
            NetworkClient.ReplaceHandler<ChunkDataMessage>(OnChunkData);
            NetworkClient.ReplaceHandler<TilesChangedMessage>(OnTilesChanged);
            NetworkClient.ReplaceHandler<TileHealthMessage>(OnHealth);
        }

        protected override void UnregisterHandlers()
        {
            NetworkClient.UnregisterHandler<ChunkDataMessage>();
            NetworkClient.UnregisterHandler<TilesChangedMessage>();
            NetworkClient.UnregisterHandler<TileHealthMessage>();
        }

        private void OnChunkData(ChunkDataMessage message)
        {
            if (!TryGetGrid(message.GridNetworkId, out var grid)) return;
            if (grid.Data == null) return;
            if (!grid.Data.ImportChunk(message.ChunkPosition, message.Ids)) return;

            grid.Data.ReplaceChunkHealth(message.ChunkPosition, message.DamagedPositions, message.Healths);
        }

        private void OnTilesChanged(TilesChangedMessage message)
        {
            if (!TryGetGrid(message.GridNetworkId, out var grid)) return;
            if (grid.Data == null || message.Positions == null || message.Ids == null) return;

            int count = Mathf.Min(message.Positions.Length, message.Ids.Length);
            for (int i = 0; i < count; i++)
            {
                if (message.Ids[i] == 0)
                    grid.Data.RemoveTile(message.Positions[i]);
                else
                    grid.Data.SetTile(message.Positions[i], message.Ids[i]);
            }
        }

        private void OnHealth(TileHealthMessage message)
        {
            if (!TryGetGrid(message.GridNetworkId, out var grid)) return;
            if (grid.Data == null || message.Positions == null || message.Healths == null) return;

            int count = Mathf.Min(message.Positions.Length, message.Healths.Length);
            for (int i = 0; i < count; i++)
                grid.Data.SetHealth(message.Positions[i], message.Healths[i]);
        }
    }
}