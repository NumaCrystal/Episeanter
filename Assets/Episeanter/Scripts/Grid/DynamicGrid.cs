using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Episeanter.Grids
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Grid), typeof(GridVisual), typeof(Rigidbody))]
    public sealed class DynamicGrid : NetworkBehaviour
    {
        [SerializeField] private float rotationX;
        [SerializeField] private float rotationZ;
        [SerializeField, Tooltip("World-space Y position maintained by the server.")]
        private float positionY;

        private Grid grid;
        private GridVisual gridVisual;
        private Rigidbody body;
        private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        private readonly HashSet<Vector2Int> dirtyChunks = new HashSet<Vector2Int>();
        private bool isServerActive;
        private bool originalUseGravity;
        private float rotationY;

        private int ChunkSize => grid.ChunkSize;

        private void OnEnable()
        {
            grid = GetComponent<Grid>();
            gridVisual = GetComponent<GridVisual>();
            body = GetComponent<Rigidbody>();

            if (isServer)
                ActivateServer();
        }

        public override void OnStartServer()
        {
            ActivateServer();
        }

        public override void OnStopServer()
        {
            DeactivateServer();
        }

        private void Reset()
        {
            positionY = transform.position.y;
        }

        private void ActivateServer()
        {
            if (isServerActive || !isActiveAndEnabled)
                return;

            isServerActive = true;
            originalUseGravity = body.useGravity;
            rotationY = transform.localEulerAngles.y;
            body.useGravity = false;
            gridVisual.SetMeshCollidersEnabled(false);
            grid.TileChanged += HandleTileChanged;
            gridVisual.VisualTileChanged += HandleTileChanged;
            gridVisual.VisualTilesCleared += HandleVisualTilesCleared;

            foreach (KeyValuePair<Vector3Int, uint> tile in grid.GetStoredTiles())
                AddTile(tile.Key);
            foreach (KeyValuePair<Vector3Int, uint> tile in gridVisual.GetStoredVisualTiles())
                AddTile(tile.Key);

            foreach (Vector2Int position in chunks.Keys)
                dirtyChunks.Add(position);

            ApplyRotation();
        }

        private void OnDisable()
        {
            DeactivateServer();
        }

        private void DeactivateServer()
        {
            if (!isServerActive)
                return;

            isServerActive = false;
            body.useGravity = originalUseGravity;
            if (grid != null)
                grid.TileChanged -= HandleTileChanged;

            if (gridVisual != null)
            {
                gridVisual.VisualTileChanged -= HandleTileChanged;
                gridVisual.VisualTilesCleared -= HandleVisualTilesCleared;
                gridVisual.SetMeshCollidersEnabled(true);
            }

            ClearColliders();
        }

        private void LateUpdate()
        {
            foreach (Vector2Int position in dirtyChunks)
            {
                if (chunks.TryGetValue(position, out Chunk chunk))
                    RebuildChunk(position, chunk);
            }

            dirtyChunks.Clear();
        }

        private void FixedUpdate()
        {
            if (!isServerActive)
                return;

            Vector3 position = body.position;
            position.y = positionY;
            body.position = position;

            Quaternion localRotation = Quaternion.Euler(rotationX, rotationY, rotationZ);
            body.rotation = transform.parent != null
                ? transform.parent.rotation * localRotation
                : localRotation;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            ApplyRotation();
        }

        [Server]
        public void SetRotation(float x, float z)
        {
            rotationX = x;
            rotationZ = z;
        }

        private void HandleTileChanged(Vector3Int position, uint tileId)
        {
            if (tileId == 0)
            {
                Vector2Int chunkPosition = GetChunkPosition(position);
                if (chunks.TryGetValue(chunkPosition, out Chunk chunk))
                {
                    chunk.Tiles.Remove(GetLocalPosition(position));
                    dirtyChunks.Add(chunkPosition);
                }

                return;
            }

            AddTile(position);
        }

        private void HandleVisualTilesCleared()
        {
            ClearColliders();

            foreach (KeyValuePair<Vector3Int, uint> tile in grid.GetStoredTiles())
                AddTile(tile.Key);
        }

        private void AddTile(Vector3Int position)
        {
            Vector2Int chunkPosition = GetChunkPosition(position);
            if (!chunks.TryGetValue(chunkPosition, out Chunk chunk))
            {
                chunk = new Chunk();
                chunks.Add(chunkPosition, chunk);
            }

            chunk.Tiles.Add(GetLocalPosition(position));
            dirtyChunks.Add(chunkPosition);
        }

        private void RebuildChunk(Vector2Int position, Chunk chunk)
        {
            if (chunk.Tiles.Count == 0)
            {
                chunks.Remove(position);
                DestroyUnityObject(chunk.GameObject);
                return;
            }

            bool[,,] occupied = new bool[ChunkSize, grid.HeightLimit, ChunkSize];
            foreach (Vector3Int tile in chunk.Tiles)
                occupied[tile.x, tile.y, tile.z] = true;

            if (chunk.GameObject == null)
            {
                chunk.GameObject = new GameObject($"Dynamic Grid Chunk ({position.x}, {position.y})");
                chunk.GameObject.transform.SetParent(transform, false);
                chunk.GameObject.transform.localPosition = new Vector3(
                    (float)position.x * ChunkSize,
                    0,
                    (float)position.y * ChunkSize);
            }

            int colliderIndex = 0;
            for (int y = 0; y < grid.HeightLimit; y++)
            {
                for (int z = 0; z < ChunkSize; z++)
                {
                    for (int x = 0; x < ChunkSize; x++)
                    {
                        if (!occupied[x, y, z])
                            continue;

                        int width = 1;
                        while (x + width < ChunkSize && occupied[x + width, y, z])
                            width++;

                        int depth = 1;
                        while (z + depth < ChunkSize && CanExpandDepth(occupied, x, y, z + depth, width))
                            depth++;

                        int height = 1;
                        while (y + height < grid.HeightLimit
                            && CanExpandHeight(occupied, x, y + height, z, width, depth))
                        {
                            height++;
                        }

                        SetCollider(
                            chunk,
                            colliderIndex++,
                            new Vector3(x + width * 0.5f, y + height * 0.5f, z + depth * 0.5f),
                            new Vector3(width, height, depth));

                        for (int coveredY = y; coveredY < y + height; coveredY++)
                        {
                            for (int coveredZ = z; coveredZ < z + depth; coveredZ++)
                            {
                                for (int coveredX = x; coveredX < x + width; coveredX++)
                                    occupied[coveredX, coveredY, coveredZ] = false;
                            }
                        }
                    }
                }
            }

            while (chunk.Colliders.Count > colliderIndex)
            {
                int lastIndex = chunk.Colliders.Count - 1;
                DestroyUnityObject(chunk.Colliders[lastIndex]);
                chunk.Colliders.RemoveAt(lastIndex);
            }
        }

        private static bool CanExpandDepth(bool[,,] occupied, int x, int y, int z, int width)
        {
            for (int offsetX = 0; offsetX < width; offsetX++)
            {
                if (!occupied[x + offsetX, y, z])
                    return false;
            }

            return true;
        }

        private static bool CanExpandHeight(
            bool[,,] occupied,
            int x,
            int y,
            int z,
            int width,
            int depth)
        {
            for (int offsetZ = 0; offsetZ < depth; offsetZ++)
            {
                for (int offsetX = 0; offsetX < width; offsetX++)
                {
                    if (!occupied[x + offsetX, y, z + offsetZ])
                        return false;
                }
            }

            return true;
        }

        private void SetCollider(Chunk chunk, int index, Vector3 center, Vector3 size)
        {
            BoxCollider collider;
            if (index == chunk.Colliders.Count)
            {
                collider = chunk.GameObject.AddComponent<BoxCollider>();
                chunk.Colliders.Add(collider);
            }
            else
            {
                collider = chunk.Colliders[index];
            }

            collider.center = center;
            collider.size = size;
        }

        private void ClearColliders()
        {
            foreach (Chunk chunk in chunks.Values)
                DestroyUnityObject(chunk.GameObject);

            chunks.Clear();
            dirtyChunks.Clear();
        }

        private void ApplyRotation()
        {
            if (transform == null)
                return;

            Vector3 current = transform.localEulerAngles;
            transform.localRotation = Quaternion.Euler(rotationX, current.y, rotationZ);
        }

        private Vector2Int GetChunkPosition(Vector3Int position)
        {
            return new Vector2Int(FloorDiv(position.x, ChunkSize), FloorDiv(position.z, ChunkSize));
        }

        private Vector3Int GetLocalPosition(Vector3Int position)
        {
            return new Vector3Int(PositiveMod(position.x, ChunkSize), position.y, PositiveMod(position.z, ChunkSize));
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

        private static void DestroyUnityObject(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        private sealed class Chunk
        {
            public readonly HashSet<Vector3Int> Tiles = new HashSet<Vector3Int>();
            public readonly List<BoxCollider> Colliders = new List<BoxCollider>();
            public GameObject GameObject;
        }
    }
}
