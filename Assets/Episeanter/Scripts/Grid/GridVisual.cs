using System.Collections.Generic;
using UnityEngine;

namespace Episeanter.Grids
{
    /// <summary>Stores a visual-only tile map and builds local chunk meshes from it.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Grid))]
    public sealed partial class GridVisual : MonoBehaviour
    {
        private Grid grid;
        private bool meshCollidersEnabled = true;
        private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        private readonly HashSet<Vector2Int> dirtyChunks = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector3Int, int> damageStagesByPosition = new Dictionary<Vector3Int, int>();
        private Material damageLayerMaterial;
        private int damageStageCount = 1;
        private float damageLayerHalfTexelU;

        internal event System.Action VisualTilesCleared;
        internal event System.Action<Vector3Int, uint> VisualTileChanged;

        internal void SetMeshCollidersEnabled(bool value)
        {
            meshCollidersEnabled = value;
            foreach (Chunk chunk in chunks.Values)
            {
                chunk.MeshCollider.enabled = value;
                chunk.MeshCollider.sharedMesh = value ? chunk.MeshFilter.sharedMesh : null;
            }
        }

        public int HeightLimit => DataGrid.HeightLimit;
        public int ChunkSize => DataGrid.ChunkSize;
        public TileRegistry Registry => TileRegistry.Instance;
        private Grid DataGrid => grid != null ? grid : (grid = GetComponent<Grid>());

        internal int ConfigureDamageOverlay(Material material)
        {
            int stageCount = 1;

            if (material != null)
            {
                Texture texture = material.HasProperty("_BaseMap")
                    ? material.GetTexture("_BaseMap")
                    : null;
                if (texture == null && material.HasProperty("_MainTex"))
                    texture = material.GetTexture("_MainTex");

                if (texture == null)
                    throw new System.InvalidOperationException(
                        $"Damage layer material '{material.name}' must have a texture assigned to _BaseMap or _MainTex.");

                if (texture.height <= 0 || texture.width % texture.height != 0)
                    throw new System.InvalidOperationException(
                        $"Damage layer material '{material.name}' has a {texture.width}x{texture.height} texture. " +
                        "Its horizontal damage frames must be square and fill the texture height.");

                stageCount = texture.width / texture.height;
                damageLayerHalfTexelU = 0.5f / texture.width;
            }
            else
                damageLayerHalfTexelU = 0f;

            damageLayerMaterial = material;
            damageStageCount = stageCount;
            foreach (Vector2Int chunkPosition in chunks.Keys)
                dirtyChunks.Add(chunkPosition);

            return stageCount;
        }

        internal void SetDamageStage(Vector3Int position, int stage)
        {
            if (stage < 0)
                damageStagesByPosition.Remove(position);
            else
            {
                if (stage >= damageStageCount)
                    throw new System.ArgumentOutOfRangeException(
                        nameof(stage), stage, $"Damage stage must be less than {damageStageCount}.");

                damageStagesByPosition[position] = stage;
            }

            if (TryReadVisualTileId(position, out _))
                MarkAffectedChunksDirty(position);
        }

        private void OnEnable()
        {
            grid = GetComponent<Grid>();
            ClearVisualState();
            grid.TileChanged += HandleTileChanged;

            foreach (var tile in grid.GetStoredTiles())
                ApplyVisualTile(tile.Key, tile.Value);
        }

        private void OnDisable()
        {
            if (grid != null)
                grid.TileChanged -= HandleTileChanged;

            VisualTilesCleared?.Invoke();
            ClearVisualState();
        }

        /// <summary>Applies visual-only tile data received from the authoritative grid.</summary>
        public void ApplyVisualTile(Vector3Int position, uint visualTileId)
        {
            if (visualTileId == 0)
                throw new System.ArgumentOutOfRangeException(
                    nameof(visualTileId), "Visual tile ID 0 is reserved for an empty cell.");

            ValidateHeight(position.y);
            GetVisualRegistry().GetDefinition(visualTileId);

            Vector2Int chunkPosition = GetChunkPosition(position);
            if (!chunks.TryGetValue(chunkPosition, out Chunk chunk))
            {
                chunk = CreateChunk(chunkPosition);
                chunks.Add(chunkPosition, chunk);
            }

            Vector3Int localPosition = GetLocalPosition(position);
            if (chunk.Tiles.TryGetValue(localPosition, out uint currentTileId)
                && currentTileId == visualTileId)
                return;

            chunk.Tiles[localPosition] = visualTileId;
            MarkAffectedChunksDirty(position);
            VisualTileChanged?.Invoke(position, visualTileId);
        }

        /// <summary>Removes visual-only tile data. Returns false if the cell was already empty.</summary>
        public bool RemoveVisualTile(Vector3Int position)
        {
            ValidateHeight(position.y);

            Vector2Int chunkPosition = GetChunkPosition(position);
            if (!chunks.TryGetValue(chunkPosition, out Chunk chunk)
                || !chunk.Tiles.Remove(GetLocalPosition(position)))
                return false;

            damageStagesByPosition.Remove(position);
            MarkAffectedChunksDirty(position);
            VisualTileChanged?.Invoke(position, 0);
            return true;
        }

        /// <summary>Removes all locally stored visual tiles and generated chunk meshes.</summary>
        public void ClearVisualTiles()
        {
            VisualTilesCleared?.Invoke();
            damageStagesByPosition.Clear();
            ClearVisualState();
        }

        internal IEnumerable<KeyValuePair<Vector3Int, uint>> GetStoredVisualTiles()
        {
            foreach (KeyValuePair<Vector2Int, Chunk> chunkEntry in chunks)
            {
                foreach (KeyValuePair<Vector3Int, uint> tileEntry in chunkEntry.Value.Tiles)
                {
                    yield return new KeyValuePair<Vector3Int, uint>(
                        GetGlobalPosition(chunkEntry.Key, tileEntry.Key),
                        tileEntry.Value);
                }
            }
        }

        /// <summary>Returns the visual tile ID at a cell, or 0 when it is empty.</summary>
        public uint GetVisualTileId(Vector3Int position)
        {
            ValidateHeight(position.y);
            return TryReadVisualTileId(position, out uint tileId) ? tileId : 0;
        }

        /// <summary>Returns false for an empty cell or a coordinate outside the configured height.</summary>
        public bool TryGetVisualTileId(Vector3Int position, out uint visualTileId)
        {
            return TryReadVisualTileId(position, out visualTileId);
        }

        /// <summary>Returns the local visual definition, or null when the cell is empty.</summary>
        public TileDefinition GetTile(Vector3Int position)
        {
            ValidateHeight(position.y);
            return TryReadVisualTileId(position, out uint tileId)
                ? GetVisualRegistry().GetDefinition(tileId)
                : null;
        }

        /// <summary>Returns false for an empty cell or a coordinate outside the configured height.</summary>
        public bool TryGetTile(Vector3Int position, out TileDefinition tile)
        {
            tile = null;
            if (!TryReadVisualTileId(position, out uint visualTileId))
                return false;

            tile = GetVisualRegistry().GetDefinition(visualTileId);
            return true;
        }

        private void HandleTileChanged(Vector3Int position, uint tileId)
        {
            if (tileId == 0)
                RemoveVisualTile(position);
            else
                ApplyVisualTile(position, tileId);
        }

        private TileRegistry GetVisualRegistry()
        {
            return TileRegistry.Instance;
        }

        private void ClearVisualState()
        {
            foreach (Chunk chunk in chunks.Values)
            {
                DestroyUnityObject(chunk.MeshFilter.sharedMesh);
                DestroyUnityObject(chunk.DamageMeshFilter.sharedMesh);
                DestroyUnityObject(chunk.GameObject);
            }

            chunks.Clear();
            dirtyChunks.Clear();
        }
        private Chunk CreateChunk(Vector2Int position)
        {
            var chunkObject = new GameObject($"Visual Chunk ({position.x}, {position.y})");
            chunkObject.transform.SetParent(transform, false);
            chunkObject.transform.localPosition = new Vector3(
                (float)position.x * ChunkSize,
                0,
                (float)position.y * ChunkSize);

            var chunk = new Chunk(chunkObject);
            chunk.MeshCollider.enabled = meshCollidersEnabled;
            chunk.MeshFilter.sharedMesh = new Mesh
            {
                name = chunkObject.name,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            chunk.MeshFilter.sharedMesh.MarkDynamic();
            return chunk;
        }

        private void MarkAffectedChunksDirty(Vector3Int position)
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (TryGetNeighborPosition(position, new Vector3Int(x, 0, z), out Vector3Int neighbor))
                        dirtyChunks.Add(GetChunkPosition(neighbor));
                }
            }
        }

        private bool TryReadVisualTileId(Vector3Int position, out uint tileId)
        {
            tileId = 0;
            if (position.y < 0 || position.y >= HeightLimit)
                return false;

            Vector2Int chunkPosition = GetChunkPosition(position);
            return chunks.TryGetValue(chunkPosition, out Chunk chunk)
                && chunk.Tiles.TryGetValue(GetLocalPosition(position), out tileId);
        }

        private Vector3Int GetGlobalPosition(Vector2Int chunkPosition, Vector3Int localPosition)
        {
            long x = (long)chunkPosition.x * ChunkSize + localPosition.x;
            long z = (long)chunkPosition.y * ChunkSize + localPosition.z;
            return new Vector3Int((int)x, localPosition.y, (int)z);
        }

        private Vector2Int GetChunkPosition(Vector3Int position)
        {
            return new Vector2Int(FloorDiv(position.x, ChunkSize), FloorDiv(position.z, ChunkSize));
        }

        private Vector3Int GetLocalPosition(Vector3Int position)
        {
            return new Vector3Int(PositiveMod(position.x, ChunkSize), position.y, PositiveMod(position.z, ChunkSize));
        }

        private static bool TryGetNeighborPosition(
            Vector3Int position,
            Vector3Int offset,
            out Vector3Int neighbor)
        {
            long x = (long)position.x + offset.x;
            long y = (long)position.y + offset.y;
            long z = (long)position.z + offset.z;
            if (x < int.MinValue || x > int.MaxValue
                || y < 0 || y > int.MaxValue
                || z < int.MinValue || z > int.MaxValue)
            {
                neighbor = default;
                return false;
            }

            neighbor = new Vector3Int((int)x, (int)y, (int)z);
            return true;
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
            if (y < 0 || y >= HeightLimit)
                throw new System.ArgumentOutOfRangeException(
                    nameof(y), y, $"Tile height must be between 0 and {HeightLimit - 1}.");
        }
    }
}
