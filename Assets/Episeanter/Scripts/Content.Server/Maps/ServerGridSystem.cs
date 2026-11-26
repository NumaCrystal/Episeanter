using System;
using System.Collections.Generic;
using Episeanter.Content.Shared.Maps.Grids;
using Episeanter.Content.Shared.Maps.Tiles;
using Episeanter.Content.Shared.Maps.Network;
using Episeanter.Content.Shared.Maps.Util;
using Episeanter.Content.Shared.Events;
using Episeanter.Content.Shared.Maps.Events;
using Mirror;
using UnityEngine;

namespace Episeanter.Content.Server.Maps
{
    public sealed class ServerGridSystem : GridSystem
    {
        private const int MaxChangesPerMessage = 512;

        [Header("Validation")]
        [SerializeField]
        private bool allowClientEdits = true;

        [SerializeField, Min(0f)]
        private float maxReach = 0f;

        [Header("Generation")]
        [SerializeField]
        private bool generateOnStart = true;

        [SerializeField]
        private Vector2Int generateSize = new Vector2Int(100, 100);

        [SerializeField, Min(1)]
        private uint generateTileId = 1;

        private readonly List<Vector3Int> pendingPositions = new List<Vector3Int>();
        private readonly List<uint> pendingIds = new List<uint>();
        private readonly Dictionary<Vector3Int, float> pendingHealth = new Dictionary<Vector3Int, float>();
        private readonly List<Vector3Int> healthPositions = new List<Vector3Int>();
        private readonly List<float> healthValues = new List<float>();

        public override void OnStartServer()
        {
            ServerGridModule.Instance.Add(this);
            Initialize();

            EventBus.Subscribe<PlaceTileEvent>(OnTilePlaced);
            EventBus.Subscribe<RemoveTileEvent>(OnTileRemoved);
            EventBus.Subscribe<TileHealthChangedEvent>(OnHealthChanged);

            if (generateOnStart)
                Generate();
        }

        public override void OnStopServer()
        {
            ServerGridModule.Instance.Remove(this);
            if (Data != null)
            {
                EventBus.Unsubscribe<RemoveTileEvent>(OnTileRemoved);
                EventBus.Unsubscribe<PlaceTileEvent>(OnTilePlaced);
                EventBus.Unsubscribe<TileHealthChangedEvent>(OnHealthChanged);
            }

            Data = null;
            pendingPositions.Clear();
            pendingIds.Clear();
            pendingHealth.Clear();
        }

        public bool SetTile(Vector3Int position, uint tileId)
             => Data != null && GridRules.CanPlace(ServerGridModule.Instance.Grids.Values, this, position, tileId) && Data.SetTile(position, tileId);

        public bool RemoveTile(Vector3Int position)
            => Data != null && GridRules.CanRemove(Data, position) && Data.RemoveTile(position);

        public bool DamageTile(Vector3Int position, float damage)
            => Data != null && GridRules.TryApplyDamage(Data, position, damage);

        public static bool CreateGrid(Vector3 position, float rotation, out ServerGridSystem gridSystem, bool checkRules = true)
        {
            if (checkRules && !GridRules.CanCreate<ServerGridSystem>(ServerGridModule.Instance.Grids.Values, position))
            {
                gridSystem = null;
                return false;
            }

            GameObject go = Instantiate(
                GridSettings.Instance.GridPrefab, position, Quaternion.Euler(0f, rotation, 0f));

            if (!go.TryGetComponent(out gridSystem))
            {
                Destroy(go);
                return false;
            }

            NetworkServer.Spawn(go);
            return true;
        }

        private void Generate()
        {
            for (int x = 0; x < generateSize.x; x++)
            {
                for (int z = 0; z < generateSize.y; z++)
                    Data.SetTile(new Vector3Int(x, 0, z), generateTileId);
            }

            pendingPositions.Clear();
            pendingIds.Clear();
            pendingHealth.Clear();
        }

        private void OnTilePlaced(PlaceTileEvent e)
        {
            if(!IsGridEqual(this, e.GridNetId, out _))
                return;

            pendingPositions.Add(e.Position);
            pendingIds.Add(e.TileId);
        }

        private void OnTileRemoved(RemoveTileEvent e)
        {
            if(!IsGridEqual(this, e.GridNetId, out _))
                return;

            pendingPositions.Add(e.Position);
            pendingIds.Add(0);
            pendingHealth.Remove(e.Position);
        }

        public void SendSnapshot(NetworkConnectionToClient conn, GridSnapshotRequest request)
        {
            if (request.RegistryHash != TileRegistry.Instance.Hash)
                return;

            foreach (Vector2Int chunkPosition in Data.ChunkPositions)
            {
                Data.CopyDamagedTiles(chunkPosition, healthPositions, healthValues);
                conn.Send(new ChunkDataMessage
                {
                    GridNetworkId = netId,
                    ChunkPosition = chunkPosition,
                    Ids = Data.ExportChunk(chunkPosition),
                    DamagedPositions = healthPositions.ToArray(),
                    Healths = healthValues.ToArray()
                });
            }
        }

        private void OnHealthChanged(TileHealthChangedEvent e)
        {
            if(!IsGridEqual(this, e.GridNetId, out _))
                return;

            pendingHealth[e.Position] = e.Health;
        }

        public bool CanClientEdit(NetworkConnectionToClient conn, Vector3Int cell)
        {
            if (!allowClientEdits)
            {
                Debug.LogWarning($"Rejected grid edit from connection {conn.connectionId}: client editing is disabled.", this);
                return false;
            }

            return IsInReach(conn, cell);
        }

        private bool IsInReach(NetworkConnectionToClient conn, Vector3Int cell)
        {
            if (maxReach <= 0f)
                return true;
            if (conn.identity == null)
                return false;

            Vector3 center = transform.TransformPoint(new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f));
            return (conn.identity.transform.position - center).sqrMagnitude <= maxReach * maxReach;
        }

        private void LateUpdate()
        {
            if (!NetworkServer.active || Data == null)
                return;

            for (int start = 0; start < pendingPositions.Count; start += MaxChangesPerMessage)
            {
                int count = Math.Min(MaxChangesPerMessage, pendingPositions.Count - start);
                var positions = new Vector3Int[count];
                var ids = new uint[count];
                pendingPositions.CopyTo(start, positions, 0, count);
                pendingIds.CopyTo(start, ids, 0, count);

                NetworkServer.SendToReady(new TilesChangedMessage { GridNetworkId = netId, Positions = positions, Ids = ids });
            }

            pendingPositions.Clear();
            pendingIds.Clear();

            if (pendingHealth.Count == 0)
                return;

            healthPositions.Clear();
            healthValues.Clear();
            foreach (KeyValuePair<Vector3Int, float> entry in pendingHealth)
            {
                if (!Data.TryGetTileId(entry.Key, out _))
                    continue;

                healthPositions.Add(entry.Key);
                healthValues.Add(entry.Value);
            }

            pendingHealth.Clear();
            for (int start = 0; start < healthPositions.Count; start += MaxChangesPerMessage)
            {
                int count = Math.Min(MaxChangesPerMessage, healthPositions.Count - start);
                var positions = new Vector3Int[count];
                var healths = new float[count];
                healthPositions.CopyTo(start, positions, 0, count);
                healthValues.CopyTo(start, healths, 0, count);
                NetworkServer.SendToReady(new TileHealthMessage { GridNetworkId = netId, Positions = positions, Healths = healths });
            }
        }

        public static bool IsFiniteDamage(float damage)
            => damage > 0f && !float.IsNaN(damage) && !float.IsInfinity(damage);
    }
}
