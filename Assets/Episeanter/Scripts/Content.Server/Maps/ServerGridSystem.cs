using System;
using System.Collections.Generic;
using Episeanter.Content.Shared.Maps;
using Mirror;
using UnityEngine;

namespace Episeanter.Contnet.Server.Maps
{
    public sealed class ServerGridSystem : NetworkBehaviour
    {
        private const int MaxChangesPerMessage = 512;

        [SerializeField] private GridSettings settings;

        [Header("Validation")]
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

        public GridData Data { get; private set; }

        public override void OnStartServer()
        {
            Data = new GridData(settings.HeightLimit, settings.ChunkSize);
            Data.TileChanged += OnTileChanged;

            NetworkServer.RegisterHandler<GridSnapshotRequest>(OnSnapshotRequest);
            NetworkServer.RegisterHandler<PlaceTileRequest>(OnPlaceRequest);
            NetworkServer.RegisterHandler<RemoveTileRequest>(OnRemoveRequest);

            if (generateOnStart)
                Generate();
        }

        public override void OnStopServer()
        {
            NetworkServer.UnregisterHandler<GridSnapshotRequest>();
            NetworkServer.UnregisterHandler<PlaceTileRequest>();
            NetworkServer.UnregisterHandler<RemoveTileRequest>();

            if (Data != null)
                Data.TileChanged -= OnTileChanged;

            Data = null;
            pendingPositions.Clear();
            pendingIds.Clear();
        }

        public bool SetTile(Vector3Int position, uint tileId)
             => Data != null && GridRules.CanPlace(Data, TileRegistry.Instance, position, tileId) && Data.SetTile(position, tileId);


        public bool RemoveTile(Vector3Int position)
            => Data != null && Data.RemoveTile(position);

        private void Generate()
        {
            for (int x = 0; x < generateSize.x; x++)
            {
                for (int z = 0; z < generateSize.y; z++)
                    Data.SetTile(new Vector3Int(x, 0, z), generateTileId);
            }

            pendingPositions.Clear();
            pendingIds.Clear();
        }

        private void OnTileChanged(Vector3Int position, uint tileId)
        {
            pendingPositions.Add(position);
            pendingIds.Add(tileId);
        }

        private void OnSnapshotRequest(NetworkConnectionToClient conn, GridSnapshotRequest request)
        {
            if (request.RegistryHash != TileRegistry.Instance.Hash)
                return;

            foreach (Vector2Int chunkPosition in Data.ChunkPositions)
            {
                conn.Send(new ChunkDataMessage
                {
                    ChunkPosition = chunkPosition,
                    Ids = Data.ExportChunk(chunkPosition)
                });
            }
        }

        private void OnPlaceRequest(NetworkConnectionToClient conn, PlaceTileRequest request)
        {
            if (!IsInReach(conn, request.Position))
                return;
            if (!GridRules.CanPlace(Data, TileRegistry.Instance, request.Position, request.TileId))
                return;

            Data.SetTile(request.Position, request.TileId);
        }

        private void OnRemoveRequest(NetworkConnectionToClient conn, RemoveTileRequest request)
        {
            if (!IsInReach(conn, request.Position))
                return;
            if (!GridRules.CanRemove(Data, request.Position))
                return;

            Data.RemoveTile(request.Position);
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
            if (pendingPositions.Count == 0)
                return;

            for (int start = 0; start < pendingPositions.Count; start += MaxChangesPerMessage)
            {
                int count = Math.Min(MaxChangesPerMessage, pendingPositions.Count - start);
                var positions = new Vector3Int[count];
                var ids = new uint[count];
                pendingPositions.CopyTo(start, positions, 0, count);
                pendingIds.CopyTo(start, ids, 0, count);

                NetworkServer.SendToReady(new TilesChangedMessage { Positions = positions, Ids = ids });
            }

            pendingPositions.Clear();
            pendingIds.Clear();
        }
    }
}
