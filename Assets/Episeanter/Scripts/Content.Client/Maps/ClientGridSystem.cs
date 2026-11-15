using Episeanter.Content.Shared.Maps;
using Mirror;
using UnityEngine;

namespace Episeanter.Content.Client.Maps
{
    [RequireComponent(typeof(GridRenderer))]
    public sealed class ClientGridSystem : NetworkBehaviour
    {
        [SerializeField]
        private GridSettings settings;

        [SerializeField]
        private GridRenderer gridRenderer;

        public GridData Data { get; private set; }

        public override void OnStartClient()
        {
            if (gridRenderer == null)
                gridRenderer = GetComponent<GridRenderer>();

            Data = new GridData(settings.HeightLimit, settings.ChunkSize);
            gridRenderer.Bind(Data);

            NetworkClient.RegisterHandler<ChunkDataMessage>(OnChunkData);
            NetworkClient.RegisterHandler<TilesChangedMessage>(OnTilesChanged);
            NetworkClient.RegisterHandler<TileHealthMessage>(OnHealth);

            NetworkClient.Send(new GridSnapshotRequest { RegistryHash = TileRegistry.Instance.Hash });
        }

        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<ChunkDataMessage>();
            NetworkClient.UnregisterHandler<TilesChangedMessage>();
            NetworkClient.UnregisterHandler<TileHealthMessage>();

            if (gridRenderer != null)
                gridRenderer.Unbind();

            Data = null;
        }

        public void RequestPlace(Vector3Int position, uint tileId)
        {
            if (!NetworkClient.active || Data == null)
                return;
            if (!GridRules.CanPlace(Data, TileRegistry.Instance, position, tileId))
                return;

            NetworkClient.Send(new PlaceTileRequest { Position = position, TileId = tileId });
        }

        public void RequestRemove(Vector3Int position)
        {
            if (!NetworkClient.active || Data == null)
                return;
            if (!GridRules.CanRemove(Data, position))
                return;

            NetworkClient.Send(new RemoveTileRequest { Position = position });
        }

        public void RequestDamage(Vector3Int position, float damage)
        {
            if (!NetworkClient.active || Data == null)
                return;
            if (!GridRules.CanDamage(Data, TileRegistry.Instance, position, damage, out _))
                return;

            NetworkClient.Send(new DamageTileRequest { Position = position, Damage = damage });
        }

        private void OnChunkData(ChunkDataMessage message)
        {
            if (Data == null)
                return;

            if (!Data.ImportChunk(message.ChunkPosition, message.Ids))
                return;

            Data.ReplaceChunkHealth(message.ChunkPosition, message.DamagedPositions, message.Healths);
        }

        private void OnTilesChanged(TilesChangedMessage message)
        {
            if (Data == null || message.Positions == null || message.Ids == null)
                return;

            int count = Mathf.Min(message.Positions.Length, message.Ids.Length);
            for (int i = 0; i < count; i++)
            {
                if (message.Ids[i] == 0)
                    Data.RemoveTile(message.Positions[i]);
                else
                    Data.SetTile(message.Positions[i], message.Ids[i]);
            }
        }

        private void OnHealth(TileHealthMessage message)
        {
            if (Data == null || message.Positions == null || message.Healths == null)
                return;

            int count = Mathf.Min(message.Positions.Length, message.Healths.Length);
            for (int i = 0; i < count; i++)
                Data.SetHealth(message.Positions[i], message.Healths[i]);
        }
    }
}
