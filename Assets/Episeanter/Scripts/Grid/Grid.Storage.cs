using System;
using System.Collections.Generic;
using UnityEngine;

namespace Episeanter.Grids
{
    public sealed partial class Grid
    {
        [SerializeField, Min(1)] private int heightLimit = 2;
        [SerializeField, Min(1)] private int chunkSize = 16;

        private readonly Dictionary<Vector2Int, DataChunk> chunks = new Dictionary<Vector2Int, DataChunk>();

        private void OnValidate()
        {
            heightLimit = Mathf.Max(1, heightLimit);
            chunkSize = Mathf.Max(1, chunkSize);
        }

        private void PlaceTile(Vector3Int position, TileDefinition tile)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));

            PlaceTile(position, GetTileRegistry().GetId(tile));
        }

        private void PlaceTile(Vector3Int position, uint tileId)
        {
            EnsureWritable();
            if (tileId == 0)
                throw new ArgumentOutOfRangeException(nameof(tileId), "Tile ID 0 is reserved for an empty cell.");

            ValidateHeight(position.y);
            GetTileRegistry().GetDefinition(tileId);

            Vector2Int chunkPosition = GetChunkPosition(position);
            if (!chunks.TryGetValue(chunkPosition, out DataChunk chunk))
            {
                chunk = new DataChunk();
                chunks.Add(chunkPosition, chunk);
                NotifyChunkCountChanged();
            }

            Vector3Int localPosition = GetLocalPosition(position);
            if (chunk.Tiles.TryGetValue(localPosition, out uint currentTileId)
                && currentTileId == tileId)
                return;

            chunk.Tiles[localPosition] = tileId;
            TileChanged?.Invoke(position, tileId);
        }

        private bool RemoveTileAt(Vector3Int position)
        {
            EnsureWritable();
            ValidateHeight(position.y);

            Vector2Int chunkPosition = GetChunkPosition(position);
            if (!chunks.TryGetValue(chunkPosition, out DataChunk chunk)
                || !chunk.Tiles.Remove(GetLocalPosition(position)))
                return false;

            bool removedChunk = chunk.Tiles.Count == 0;
            if (removedChunk)
                chunks.Remove(chunkPosition);

            TileChanged?.Invoke(position, 0);
            if (removedChunk)
                NotifyChunkCountChanged();
            return true;
        }

        private bool TryReadTileId(Vector3Int position, out uint tileId)
        {
            tileId = 0;
            if (position.y < 0 || position.y >= heightLimit)
                return false;

            Vector2Int chunkPosition = GetChunkPosition(position);
            if (!chunks.TryGetValue(chunkPosition, out DataChunk chunk))
                return false;

            return chunk.Tiles.TryGetValue(GetLocalPosition(position), out tileId);
        }

        internal IEnumerable<KeyValuePair<Vector3Int, uint>> GetStoredTiles()
        {
            foreach (KeyValuePair<Vector2Int, DataChunk> chunkEntry in chunks)
            {
                foreach (KeyValuePair<Vector3Int, uint> tileEntry in chunkEntry.Value.Tiles)
                {
                    yield return new KeyValuePair<Vector3Int, uint>(
                        GetGlobalPosition(chunkEntry.Key, tileEntry.Key),
                        tileEntry.Value);
                }
            }
        }

        internal void ClearDataForClient()
        {
            chunks.Clear();
        }

        private TileRegistry GetTileRegistry()
        {
            return TileRegistry.Instance;
        }

        private Vector2Int GetChunkPosition(Vector3Int position)
        {
            return new Vector2Int(FloorDiv(position.x, chunkSize), FloorDiv(position.z, chunkSize));
        }

        private Vector3Int GetGlobalPosition(Vector2Int chunkPosition, Vector3Int localPosition)
        {
            long x = (long)chunkPosition.x * chunkSize + localPosition.x;
            long z = (long)chunkPosition.y * chunkSize + localPosition.z;
            return new Vector3Int((int)x, localPosition.y, (int)z);
        }

        private Vector3Int GetLocalPosition(Vector3Int position)
        {
            return new Vector3Int(PositiveMod(position.x, chunkSize), position.y, PositiveMod(position.z, chunkSize));
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }

        private static int PositiveMod(int value, int divisor)
        {
            int remainder = value % divisor;
            return remainder < 0 ? remainder + divisor : remainder;
        }

        private void ValidateHeight(int y)
        {
            if (y < 0 || y >= heightLimit)
                throw new ArgumentOutOfRangeException(nameof(y), y, $"Tile height must be between 0 and {heightLimit - 1}.");
        }

        private void EnsureWritable()
        {
            if (isClientReplica)
                throw new InvalidOperationException("A client cannot modify the authoritative grid data.");
        }

        private sealed class DataChunk
        {
            public readonly Dictionary<Vector3Int, uint> Tiles = new Dictionary<Vector3Int, uint>();
        }
    }
}
