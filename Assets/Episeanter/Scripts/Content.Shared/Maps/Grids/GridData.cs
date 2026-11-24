using System;
using System.Collections.Generic;
using Episeanter.Content.Shared.Events;
using Episeanter.Content.Shared.Maps.Events;
using Episeanter.Content.Shared.Maps.Util;
using UnityEngine;

namespace Episeanter.Content.Shared.Maps.Grids
{
    public sealed class GridData
    {
        private readonly Dictionary<Vector2Int, Dictionary<Vector3Int, uint>> chunks =
            new Dictionary<Vector2Int, Dictionary<Vector3Int, uint>>();

        private readonly Dictionary<Vector3Int, float> healthByCell = new Dictionary<Vector3Int, float>();

        public GridData(int heightLimit, int chunkSize, uint gridNetId)
        {
            if (heightLimit < 1)
                throw new ArgumentOutOfRangeException(nameof(heightLimit));
            if (chunkSize < 1)
                throw new ArgumentOutOfRangeException(nameof(chunkSize));

            HeightLimit = heightLimit;
            ChunkSize = chunkSize;
            GridNetId = gridNetId;
        }

        public int HeightLimit { get; }
        public int ChunkSize { get; }
        public int ChunkVolume => ChunkSize * ChunkSize * HeightLimit;
        public uint GridNetId { get; }

        public IEnumerable<KeyValuePair<Vector3Int, float>> DamagedTiles => healthByCell;

        public IEnumerable<Vector2Int> ChunkPositions => chunks.Keys;

        public bool InBounds(Vector3Int position)
            => position.y >= 0 && position.y < HeightLimit;

        public uint GetTileId(Vector3Int position)
            => TryGetTileId(position, out uint id) ? id : 0;

        public bool TryGetChunkTiles(Vector2Int chunkPosition, out Dictionary<Vector3Int, uint> tiles)

            => chunks.TryGetValue(chunkPosition, out tiles);


        public bool TryGetTileId(Vector3Int position, out uint tileId)
        {
            tileId = 0;
            return InBounds(position)
                && chunks.TryGetValue(GetChunkPosition(position), out Dictionary<Vector3Int, uint> tiles)
                && tiles.TryGetValue(GetLocalPosition(position), out tileId);
        }

        public bool SetTile(Vector3Int position, uint tileId)
        {
            if (tileId == 0 || !InBounds(position))
                return false;

            Vector2Int chunkPosition = GetChunkPosition(position);
            if (!chunks.TryGetValue(chunkPosition, out Dictionary<Vector3Int, uint> tiles))
            {
                tiles = new Dictionary<Vector3Int, uint>();
                chunks.Add(chunkPosition, tiles);
            }

            Vector3Int local = GetLocalPosition(position);
            if (tiles.TryGetValue(local, out uint current) && current == tileId)
                return false;

            healthByCell.Remove(position);
            tiles[local] = tileId;
            EventBus.Publish(new PlaceTileEvent { GridNetId = GridNetId, Position = position, TileId = tileId });
            return true;
        }

        public bool RemoveTile(Vector3Int position)
        {
            if (!InBounds(position))
                return false;

            Vector2Int chunkPosition = GetChunkPosition(position);
            if (!chunks.TryGetValue(chunkPosition, out Dictionary<Vector3Int, uint> tiles)
                || !tiles.Remove(GetLocalPosition(position)))
                return false;

            if (tiles.Count == 0)
                chunks.Remove(chunkPosition);

            healthByCell.Remove(position);
            EventBus.Publish(new RemoveTileEvent { GridNetId = GridNetId, Position = position });
            return true;
        }

        public bool TryGetHealth(Vector3Int position, out float health)
            => healthByCell.TryGetValue(position, out health);

        public float GetHealth(Vector3Int position, float maxHealth)
        {
            if (!TryGetTileId(position, out _))
                return 0f;

            return healthByCell.TryGetValue(position, out float health) ? health : maxHealth;
        }

        public bool SetHealth(Vector3Int position, float health)
        {
            if (!TryGetTileId(position, out _)
                || float.IsNaN(health)
                || float.IsInfinity(health)
                || health <= 0f)
                return false;

            if (healthByCell.TryGetValue(position, out float current) && Mathf.Approximately(current, health))
                return false;

            healthByCell[position] = health;
            EventBus.Publish(new TileHealthChangedEvent { GridNetId = GridNetId, Position = position, Health = health });
            return true;
        }

        public void CopyDamagedTiles(Vector2Int chunkPosition, List<Vector3Int> positions, List<float> healths)
        {
            positions.Clear();
            healths.Clear();
            foreach (KeyValuePair<Vector3Int, float> entry in healthByCell)
            {
                if (GetChunkPosition(entry.Key) != chunkPosition)
                    continue;

                positions.Add(entry.Key);
                healths.Add(entry.Value);
            }
        }

        public void ReplaceChunkHealth(Vector2Int chunkPosition, Vector3Int[] positions, float[] healths)
        {
            var stale = new List<Vector3Int>();
            foreach (Vector3Int position in healthByCell.Keys)
            {
                if (GetChunkPosition(position) == chunkPosition)
                    stale.Add(position);
            }

            for (int i = 0; i < stale.Count; i++)
            {
                healthByCell.Remove(stale[i]);
                EventBus.Publish(new TileHealthChangedEvent { GridNetId = GridNetId, Position = stale[i], Health = 0f });
            }

            if (positions == null || healths == null)
                return;

            int count = Math.Min(positions.Length, healths.Length);
            for (int i = 0; i < count; i++)
                SetHealth(positions[i], healths[i]);
        }

        public uint[] ExportChunk(Vector2Int chunkPosition)
        {
            var ids = new uint[ChunkVolume];
            if (chunks.TryGetValue(chunkPosition, out Dictionary<Vector3Int, uint> tiles))
            {
                foreach (KeyValuePair<Vector3Int, uint> tile in tiles)
                    ids[ToIndex(tile.Key)] = tile.Value;
            }

            return ids;
        }

        public bool ImportChunk(Vector2Int chunkPosition, uint[] ids)
        {
            if (ids == null || ids.Length != ChunkVolume)
                return false;

            var tiles = new Dictionary<Vector3Int, uint>();
            int layer = ChunkSize * ChunkSize;
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == 0)
                    continue;

                int y = i / layer;
                int z = (i % layer) / ChunkSize;
                int x = i % ChunkSize;
                tiles[new Vector3Int(x, y, z)] = ids[i];
            }

            if (tiles.Count == 0)
                chunks.Remove(chunkPosition);
            else
                chunks[chunkPosition] = tiles;

            EventBus.Publish(new ChunkLoadedEvent { GridNetId = GridNetId, ChunkPosition = chunkPosition });
            return true;
        }

        #region Helpers
        public Vector2Int GetChunkPosition(Vector3Int position)
            => new Vector2Int(FloorDiv(position.x, ChunkSize), FloorDiv(position.z, ChunkSize));

        public Vector3Int GetLocalPosition(Vector3Int position)
            => new Vector3Int(PositiveMod(position.x, ChunkSize), position.y, PositiveMod(position.z, ChunkSize));

        public Vector3Int GetGlobalPosition(Vector2Int chunkPosition, Vector3Int localPosition)
        {
            long x = (long)chunkPosition.x * ChunkSize + localPosition.x;
            long z = (long)chunkPosition.y * ChunkSize + localPosition.z;
            return new Vector3Int((int)x, localPosition.y, (int)z);
        }

        private int ToIndex(Vector3Int local)
            => (local.y * ChunkSize + local.z) * ChunkSize + local.x;

        public static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }

        public static int PositiveMod(int value, int divisor)
        {
            int remainder = value % divisor;
            return remainder < 0 ? remainder + divisor : remainder;
        }

        public bool IsTileConnected(Vector3Int cell)
        {

            if (!InBounds(cell))
                return false;

            return GetTileId(cell) != 0
                || GetTileId(cell + Vector3Int.forward) != 0
                || GetTileId(cell - Vector3Int.forward) != 0
                || GetTileId(cell + Vector3Int.right) != 0
                || GetTileId(cell - Vector3Int.right) != 0
                || GetTileId(cell - Vector3Int.up) != 0;
        }

        public Vector3Int WorldToCell(Vector3 worldPosition, Transform gridTransform)
        {
            if (gridTransform == null)
                throw new ArgumentNullException(nameof(gridTransform));

            Vector3 localPos = gridTransform.InverseTransformPoint(worldPosition);
            return Vector3Int.FloorToInt(localPos);
        }

        public Vector3Int WorldToCell(Vector3 worldPosition, MonoBehaviour monoBehaviour)
        {
            return WorldToCell(worldPosition, monoBehaviour.transform);
        }

        public Vector3 CellToWorld(Vector3Int cellPosition, Transform gridTransform, bool cellCenter = false)
        {
            if (gridTransform == null)
                throw new ArgumentNullException(nameof(gridTransform));

            float offset = cellCenter ? 0.5f : 0f;
            Vector3 localPos = new Vector3(
                cellPosition.x + offset,
                cellPosition.y + offset,
                cellPosition.z + offset);

            return gridTransform.TransformPoint(localPos);
        }
        #endregion
    }
}
