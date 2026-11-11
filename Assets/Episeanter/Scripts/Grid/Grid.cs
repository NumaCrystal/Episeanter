using UnityEngine;

namespace Episeanter.Grids
{
    /// <summary>Stores authoritative logical tile IDs without creating visual Unity objects.</summary>
    [DisallowMultipleComponent]
    public sealed partial class Grid : MonoBehaviour
    {
        public int HeightLimit => heightLimit;
        public int ChunkSize => chunkSize;
        public int ChunkCount => chunks.Count;
        public event System.Action<Vector3Int, uint> TileChanged;
        public event System.Action<int> ChunkCountChanged;
        private bool isClientReplica;

        /// <summary>Converts a grid-local cell coordinate to its world-space origin.</summary>
        public Vector3 CellToWorld(Vector3Int position)
        {
            return transform.TransformPoint(position);
        }

        /// <summary>Places or replaces a tile at a grid-local integer coordinate.</summary>
        public void SetTile(Vector3Int position, TileDefinition tile)
        {
            PlaceTile(position, tile);
        }

        /// <summary>Places a tile using its stable registry ID. ID 0 is reserved for an empty cell.</summary>
        public void SetTile(Vector3Int position, uint tileId)
        {
            PlaceTile(position, tileId);
        }

        /// <summary>Removes the tile at a grid-local coordinate. Returns false if it was empty.</summary>
        public bool RemoveTile(Vector3Int position)
        {
            return RemoveTileAt(position);
        }

        internal void NotifyChunkCountChanged()
        {
            ChunkCountChanged?.Invoke(chunks.Count);
        }

        internal void SetClientReplica(bool value)
        {
            isClientReplica = value;
        }

        /// <summary>Returns the stable registry ID at a cell, or 0 when the cell is empty.</summary>
        public uint GetTileId(Vector3Int position)
        {
            ValidateHeight(position.y);
            return TryReadTileId(position, out uint tileId) ? tileId : 0;
        }

        /// <summary>Returns false for an empty cell or a coordinate outside the configured height.</summary>
        public bool TryGetTileId(Vector3Int position, out uint tileId)
        {
            return TryReadTileId(position, out tileId);
        }
    }
}
