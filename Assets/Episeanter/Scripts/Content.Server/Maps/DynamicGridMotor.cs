using System.Collections.Generic;
using Episeanter.Content.Shared.Maps;
using Mirror;
using UnityEngine;

namespace Episeanter.Content.Server.Maps
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ServerGridSystem), typeof(Rigidbody))]
    public sealed class DynamicGridMotor : NetworkBehaviour
    {
        [SerializeField] private float rotationX;
        [SerializeField] private float rotationZ;
        [SerializeField]
        private float positionY;

        private ServerGridSystem gridSystem;
        private Rigidbody body;
        private GridData data;
        private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        private readonly HashSet<Vector2Int> dirtyChunks = new HashSet<Vector2Int>();
        private bool driving;
        private bool originalUseGravity;
        private float rotationY;

        private void Awake()
        {
            gridSystem = GetComponent<ServerGridSystem>();
            body = GetComponent<Rigidbody>();
        }

        public override void OnStartServer()
        {
            driving = true;
            originalUseGravity = body.useGravity;
            rotationY = transform.localEulerAngles.y;
            body.useGravity = false;
        }

        public override void OnStopServer()
        {
            driving = false;
            if (body != null)
                body.useGravity = originalUseGravity;

            Unbind();
        }

        [Server]
        public void SetRotation(float x, float z)
        {
            rotationX = x;
            rotationZ = z;
        }

        private void LateUpdate()
        {
            if (!driving)
                return;

            if (data == null)
            {
                if (gridSystem.Data == null)
                    return;

                Bind(gridSystem.Data);
            }

            foreach (Vector2Int position in dirtyChunks)
            {
                if (!chunks.TryGetValue(position, out Chunk chunk))
                {
                    chunk = new Chunk();
                    chunks.Add(position, chunk);
                }

                RebuildChunk(position, chunk);
            }

            dirtyChunks.Clear();
        }

        private void FixedUpdate()
        {
            if (!driving || body == null)
                return;

            Vector3 position = body.position;
            position.y = positionY;
            body.position = position;

            Quaternion localRotation = Quaternion.Euler(rotationX, rotationY, rotationZ);
            body.rotation = transform.parent != null
                ? transform.parent.rotation * localRotation
                : localRotation;
        }

        private void Bind(GridData gridData)
        {
            data = gridData;
            data.TileChanged += OnTileChanged;
            foreach (Vector2Int position in data.ChunkPositions)
                dirtyChunks.Add(position);
        }

        private void Unbind()
        {
            if (data != null)
                data.TileChanged -= OnTileChanged;

            data = null;
            foreach (Chunk chunk in chunks.Values)
                DestroyUnityObject(chunk.GameObject);

            chunks.Clear();
            dirtyChunks.Clear();
        }

        private void OnTileChanged(Vector3Int position, uint tileId)
        {
            dirtyChunks.Add(data.GetChunkPosition(position));
        }

        private void RebuildChunk(Vector2Int position, Chunk chunk)
        {
            if (!data.TryGetChunkTiles(position, out Dictionary<Vector3Int, uint> tiles) || tiles.Count == 0)
            {
                chunks.Remove(position);
                DestroyUnityObject(chunk.GameObject);
                return;
            }

            int chunkSize = data.ChunkSize;
            int heightLimit = data.HeightLimit;
            bool[,,] occupied = new bool[chunkSize, heightLimit, chunkSize];
            foreach (Vector3Int tile in tiles.Keys)
                occupied[tile.x, tile.y, tile.z] = true;

            if (chunk.GameObject == null)
            {
                chunk.GameObject = new GameObject($"Dynamic Grid Chunk ({position.x}, {position.y})");
                chunk.GameObject.transform.SetParent(transform, false);
                chunk.GameObject.transform.localPosition = new Vector3(
                    (float)position.x * chunkSize,
                    0,
                    (float)position.y * chunkSize);
                chunks[position] = chunk;
            }

            int colliderIndex = 0;
            for (int y = 0; y < heightLimit; y++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    for (int x = 0; x < chunkSize; x++)
                    {
                        if (!occupied[x, y, z])
                            continue;

                        int width = 1;
                        while (x + width < chunkSize && occupied[x + width, y, z])
                            width++;

                        int depth = 1;
                        while (z + depth < chunkSize && CanExpandDepth(occupied, x, y, z + depth, width))
                            depth++;

                        int height = 1;
                        while (y + height < heightLimit
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

        private static bool CanExpandHeight(bool[,,] occupied, int x, int y, int z, int width, int depth)
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

        private static void SetCollider(Chunk chunk, int index, Vector3 center, Vector3 size)
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

        private void OnDestroy()
        {
            Unbind();
        }

        private void Reset()
        {
            positionY = transform.position.y;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            if (transform == null)
                return;

            Vector3 current = transform.localEulerAngles;
            transform.localRotation = Quaternion.Euler(rotationX, current.y, rotationZ);
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
            public readonly List<BoxCollider> Colliders = new List<BoxCollider>();
            public GameObject GameObject;
        }
    }
}
