using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Episeanter.Grids
{
    public sealed partial class Grid
    {
        [SerializeField, Min(1)] private int heightLimit = 2;
        [SerializeField, Min(1)] private int chunkSize = 16;

        private static readonly Vector3Int[] NeighborOffsets =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1)
        };

        private static readonly Vector3[][] FaceVertices =
        {
            new[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) },
            new[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) },
            new[] { new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0), new Vector3(0, 1, 0) },
            new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) },
            new[] { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) },
            new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }
        };

        private static readonly Vector2[] FaceUv =
        {
            new Vector2(0, 0),
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(1, 0)
        };

        private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();

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
            if (tileId == 0)
                throw new ArgumentOutOfRangeException(nameof(tileId), "Tile ID 0 is reserved for an empty cell.");

            ValidateHeight(position.y);
            GetTileRegistry().GetDefinition(tileId);

            Vector2Int chunkPosition = GetChunkPosition(position);
            if (!chunks.TryGetValue(chunkPosition, out Chunk chunk))
            {
                chunk = CreateChunk(chunkPosition);
                chunks.Add(chunkPosition, chunk);
            }

            chunk.Tiles[GetLocalPosition(position)] = tileId;
            RebuildAffectedChunks(position);
        }

        private bool RemoveTileAt(Vector3Int position)
        {
            ValidateHeight(position.y);

            Vector2Int chunkPosition = GetChunkPosition(position);
            if (!chunks.TryGetValue(chunkPosition, out Chunk chunk)
                || !chunk.Tiles.Remove(GetLocalPosition(position)))
                return false;

            RebuildAffectedChunks(position);
            return true;
        }

        private bool TryReadTileId(Vector3Int position, out uint tileId)
        {
            tileId = 0;
            if (position.y < 0 || position.y >= heightLimit)
                return false;

            Vector2Int chunkPosition = GetChunkPosition(position);
            if (!chunks.TryGetValue(chunkPosition, out Chunk chunk))
                return false;

            return chunk.Tiles.TryGetValue(GetLocalPosition(position), out tileId);
        }

        private bool TryReadTile(Vector3Int position, out TileDefinition tile)
        {
            tile = null;
            if (!TryReadTileId(position, out uint tileId))
                return false;

            tile = GetTileRegistry().GetDefinition(tileId);
            return true;
        }

        private TileRegistry GetTileRegistry()
        {
            return TileRegistry.Instance;
        }

        private Chunk CreateChunk(Vector2Int position)
        {
            var chunkObject = new GameObject($"Chunk ({position.x}, {position.y})");
            chunkObject.transform.SetParent(transform, false);
            chunkObject.transform.localPosition = new Vector3(
                (float)position.x * chunkSize,
                0,
                (float)position.y * chunkSize);

            var chunk = new Chunk(chunkObject);
            chunk.MeshFilter.sharedMesh = new Mesh
            {
                name = chunkObject.name,
                indexFormat = IndexFormat.UInt32
            };
            chunk.MeshFilter.sharedMesh.MarkDynamic();
            return chunk;
        }

        private void RebuildAffectedChunks(Vector3Int position)
        {
            var affectedChunks = new HashSet<Vector2Int> { GetChunkPosition(position) };
            foreach (Vector3Int offset in NeighborOffsets)
            {
                if (TryGetNeighborPosition(position, offset, out Vector3Int neighbor))
                    affectedChunks.Add(GetChunkPosition(neighbor));
            }

            foreach (Vector2Int chunkPosition in affectedChunks)
            {
                if (chunks.TryGetValue(chunkPosition, out Chunk chunk))
                    RebuildChunk(chunkPosition, chunk);
            }
        }

        private void RebuildChunk(Vector2Int position, Chunk chunk)
        {
            if (chunk.Tiles.Count == 0)
            {
                chunks.Remove(position);
                DestroyUnityObject(chunk.GameObject);
                DestroyUnityObject(chunk.MeshFilter.sharedMesh);
                return;
            }

            List<Vector3> vertices = chunk.Vertices;
            List<Vector2> uv = chunk.Uv;
            List<Material> materials = chunk.Materials;
            Dictionary<Material, int> materialIndices = chunk.MaterialIndices;
            List<List<int>> trianglesByMaterial = chunk.TrianglesByMaterial;
            vertices.Clear();
            uv.Clear();
            materials.Clear();
            materialIndices.Clear();
            for (int i = 0; i < trianglesByMaterial.Count; i++)
                trianglesByMaterial[i].Clear();

            foreach (KeyValuePair<Vector3Int, uint> tileEntry in chunk.Tiles)
            {
                int materialIndex;
                Material material = GetTileRegistry().GetDefinition(tileEntry.Value).Material;
                if (!materialIndices.TryGetValue(material, out materialIndex))
                {
                    materialIndex = materials.Count;
                    materialIndices.Add(material, materialIndex);
                    materials.Add(material);
                    if (materialIndex == trianglesByMaterial.Count)
                        trianglesByMaterial.Add(new List<int>());
                    else
                        trianglesByMaterial[materialIndex].Clear();
                }

                for (int face = 0; face < NeighborOffsets.Length; face++)
                {
                    Vector3Int globalPosition = GetGlobalPosition(position, tileEntry.Key);
                    if (TryGetNeighborPosition(globalPosition, NeighborOffsets[face], out Vector3Int neighbor)
                        && TryReadTileId(neighbor, out _))
                        continue;

                    AddFace(tileEntry.Key, FaceVertices[face], vertices, uv, trianglesByMaterial[materialIndex]);
                }
            }

            Mesh mesh = chunk.MeshFilter.sharedMesh;
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = materials.Count;
            for (int i = 0; i < materials.Count; i++)
                mesh.SetTriangles(trianglesByMaterial[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (!chunk.HasSameMaterials(materials))
            {
                chunk.RendererMaterials = materials.ToArray();
                chunk.MeshRenderer.sharedMaterials = chunk.RendererMaterials;
            }
        }

        private static void AddFace(
            Vector3Int tilePosition,
            Vector3[] face,
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            int firstVertex = vertices.Count;
            for (int i = 0; i < face.Length; i++)
            {
                vertices.Add(tilePosition + face[i]);
                uv.Add(FaceUv[i]);
            }

            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 1);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex + 3);
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

        private Vector3Int GetGlobalPosition(Vector2Int chunkPosition, Vector3Int localPosition)
        {
            long x = (long)chunkPosition.x * chunkSize + localPosition.x;
            long z = (long)chunkPosition.y * chunkSize + localPosition.z;
            return new Vector3Int((int)x, localPosition.y, (int)z);
        }

        private Vector2Int GetChunkPosition(Vector3Int position)
        {
            return new Vector2Int(FloorDiv(position.x, chunkSize), FloorDiv(position.z, chunkSize));
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

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        private void OnDestroy()
        {
            foreach (Chunk chunk in chunks.Values)
            {
                DestroyUnityObject(chunk.MeshFilter.sharedMesh);
                DestroyUnityObject(chunk.GameObject);
            }

            chunks.Clear();
        }

        private sealed class Chunk
        {
            public readonly GameObject GameObject;
            public readonly MeshFilter MeshFilter;
            public readonly MeshRenderer MeshRenderer;
            public readonly Dictionary<Vector3Int, uint> Tiles = new Dictionary<Vector3Int, uint>();
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Material> Materials = new List<Material>();
            public readonly Dictionary<Material, int> MaterialIndices = new Dictionary<Material, int>();
            public readonly List<List<int>> TrianglesByMaterial = new List<List<int>>();
            public Material[] RendererMaterials = Array.Empty<Material>();

            public Chunk(GameObject gameObject)
            {
                GameObject = gameObject;
                MeshFilter = gameObject.AddComponent<MeshFilter>();
                MeshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            public bool HasSameMaterials(List<Material> materials)
            {
                if (RendererMaterials.Length != materials.Count)
                    return false;

                for (int i = 0; i < RendererMaterials.Length; i++)
                {
                    if (RendererMaterials[i] != materials[i])
                        return false;
                }

                return true;
            }
        }
    }
}
