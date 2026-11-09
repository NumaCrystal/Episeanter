using UnityEngine;

namespace Episeanter.Grids
{
    [DisallowMultipleComponent]
    public sealed partial class Grid : MonoBehaviour
    {
        public int HeightLimit => heightLimit;
        public int ChunkSize => chunkSize;
        public TileRegistry Registry => TileRegistry.Instance;

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

        /// <summary>Returns the tile at a grid-local coordinate, or null when the cell is empty.</summary>
        public TileDefinition GetTile(Vector3Int position)
        {
            ValidateHeight(position.y);
            return TryGetTile(position, out TileDefinition tile) ? tile : null;
        }

        /// <summary>Returns false for an empty cell or a coordinate outside the configured height.</summary>
        public bool TryGetTile(Vector3Int position, out TileDefinition tile)
        {
            return TryReadTile(position, out tile);
        }
    }
}
