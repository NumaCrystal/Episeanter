using System;
using System.Collections.Generic;
using Episeanter.Content.Shared.Maps;
using UnityEngine;
using UnityEngine.Rendering;

namespace Episeanter.Content.Client.Maps
{
    [DisallowMultipleComponent]
    public sealed class GridRenderer : MonoBehaviour
    {
        [SerializeField] private TileVisuals visuals;

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

        private const float AtlasCellCount = 4f;
        private const float AtlasCellInset = 1f / 512f;

        private readonly Dictionary<Vector2Int, ChunkView> views = new Dictionary<Vector2Int, ChunkView>();
        private readonly HashSet<Vector2Int> dirtyChunks = new HashSet<Vector2Int>();
        private readonly HashSet<uint> missingVisuals = new HashSet<uint>();

        private GridData data;

        public GridData Data => data;

        public Vector3 CellToWorld(Vector3Int position)
            => transform.TransformPoint(position);

        public void Bind(GridData gridData)
        {
            Unbind();

            data = gridData ?? throw new ArgumentNullException(nameof(gridData));
            data.TileChanged += OnTileChanged;
            data.ChunkLoaded += OnChunkLoaded;

            foreach (Vector2Int chunkPosition in data.ChunkPositions)
                MarkChunkAndNeighborsDirty(chunkPosition);
        }

        public void Unbind()
        {
            if (data != null)
            {
                data.TileChanged -= OnTileChanged;
                data.ChunkLoaded -= OnChunkLoaded;
                data = null;
            }

            foreach (ChunkView view in views.Values)
                DestroyView(view);

            views.Clear();
            dirtyChunks.Clear();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void OnTileChanged(Vector3Int position, uint tileId)
        {
            MarkAffectedChunksDirty(position);
        }

        private void OnChunkLoaded(Vector2Int chunkPosition)
        {
            MarkChunkAndNeighborsDirty(chunkPosition);
        }

        private void MarkChunkAndNeighborsDirty(Vector2Int chunkPosition)
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                    dirtyChunks.Add(new Vector2Int(chunkPosition.x + x, chunkPosition.y + z));
            }
        }

        private void MarkAffectedChunksDirty(Vector3Int position)
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (TryGetNeighborPosition(position, new Vector3Int(x, 0, z), out Vector3Int neighbor))
                        dirtyChunks.Add(data.GetChunkPosition(neighbor));
                }
            }
        }

        private void LateUpdate()
        {
            if (data == null || dirtyChunks.Count == 0)
                return;

            foreach (Vector2Int chunkPosition in dirtyChunks)
                RebuildChunk(chunkPosition);

            dirtyChunks.Clear();
        }

        private void RebuildChunk(Vector2Int position)
        {
            if (!data.TryGetChunkTiles(position, out Dictionary<Vector3Int, uint> tiles) || tiles.Count == 0)
            {
                if (views.TryGetValue(position, out ChunkView oldView))
                {
                    views.Remove(position);
                    DestroyView(oldView);
                }

                return;
            }

            if (!views.TryGetValue(position, out ChunkView chunk))
            {
                chunk = CreateView(position);
                views.Add(position, chunk);
            }

            List<Vector3> vertices = chunk.Vertices;
            List<Vector2> uv = chunk.Uv;
            List<Material> materials = chunk.Materials;
            Dictionary<Material, int> materialIndices = chunk.MaterialIndices;
            List<List<int>> trianglesByMaterial = chunk.TrianglesByMaterial;
            Mesh mesh = chunk.MeshFilter.sharedMesh;
            chunk.MeshCollider.sharedMesh = null;
            vertices.Clear();
            uv.Clear();
            materials.Clear();
            materialIndices.Clear();
            for (int i = 0; i < trianglesByMaterial.Count; i++)
                trianglesByMaterial[i].Clear();

            foreach (KeyValuePair<Vector3Int, uint> tileEntry in tiles)
            {
                if (visuals == null || !visuals.TryGetMaterials(tileEntry.Value, out Material topMaterial, out Material sideMaterial))
                {
                    if (missingVisuals.Add(tileEntry.Value))
                        Debug.LogWarning($"No visuals registered in TileVisuals for tile ID {tileEntry.Value}.", this);
                    continue;
                }

                Vector3Int globalPosition = data.GetGlobalPosition(position, tileEntry.Key);
                for (int face = 0; face < NeighborOffsets.Length; face++)
                {
                    if (face == 3)
                        continue;

                    if (TryGetNeighborPosition(globalPosition, NeighborOffsets[face], out Vector3Int neighbor)
                        && data.TryGetTileId(neighbor, out _))
                        continue;

                    Material material = face == 2 ? topMaterial : sideMaterial;
                    if (!materialIndices.TryGetValue(material, out int materialIndex))
                    {
                        materialIndex = materials.Count;
                        materialIndices.Add(material, materialIndex);
                        materials.Add(material);
                        if (materialIndex == trianglesByMaterial.Count)
                            trianglesByMaterial.Add(new List<int>());
                        else
                            trianglesByMaterial[materialIndex].Clear();
                    }

                    AddDualGridFace(
                        tileEntry.Key,
                        globalPosition,
                        face,
                        tileEntry.Value,
                        vertices,
                        uv,
                        trianglesByMaterial[materialIndex]);
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = materials.Count;
            for (int i = 0; i < materials.Count; i++)
                mesh.SetTriangles(trianglesByMaterial[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            chunk.MeshCollider.sharedMesh = materials.Count > 0 ? mesh : null;
            if (!chunk.HasSameMaterials(materials))
            {
                chunk.RendererMaterials = materials.ToArray();
                chunk.MeshRenderer.sharedMaterials = chunk.RendererMaterials;
            }
        }

        private ChunkView CreateView(Vector2Int position)
        {
            var chunkObject = new GameObject($"Chunk ({position.x}, {position.y})");
            chunkObject.transform.SetParent(transform, false);
            chunkObject.transform.localPosition = new Vector3(
                (float)position.x * data.ChunkSize,
                0,
                (float)position.y * data.ChunkSize);

            var view = new ChunkView(chunkObject);
            view.MeshFilter.sharedMesh = new Mesh
            {
                name = chunkObject.name,
                indexFormat = IndexFormat.UInt32
            };
            view.MeshFilter.sharedMesh.MarkDynamic();
            return view;
        }

        private static void DestroyView(ChunkView view)
        {
            DestroyUnityObject(view.MeshFilter.sharedMesh);
            DestroyUnityObject(view.GameObject);
        }

        private void AddDualGridFace(
            Vector3Int tilePosition,
            Vector3Int globalPosition,
            int faceIndex,
            uint tileId,
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            Vector3[] face = FaceVertices[faceIndex];
            GetFaceTangentAxes(faceIndex, out int uAxis, out int vAxis);
            for (int cornerIndex = 0; cornerIndex < face.Length; cornerIndex++)
            {
                Vector3 corner = face[cornerIndex];
                Vector3 next = face[(cornerIndex + 1) % face.Length];
                Vector3 previous = face[(cornerIndex + face.Length - 1) % face.Length];
                Vector3 center = (corner + next + previous + face[(cornerIndex + 2) % face.Length]) * 0.25f;
                Vector3[] patch =
                {
                    corner,
                    (corner + next) * 0.5f,
                    center,
                    (previous + corner) * 0.5f
                };

                int filledMask = GetDualGridMask(globalPosition, corner, uAxis, vAxis, tileId);
                // DualGridAtlas cells are indexed by empty quadrants (bit 0 BL, 1 BR, 2 TL, 3 TR).
                int mask = filledMask ^ 0xF;
                int atlasColumn = mask & 3;
                int atlasRow = mask >> 2;
                int cornerU = Mathf.RoundToInt(GetAxisValue(corner, uAxis));
                int cornerV = Mathf.RoundToInt(GetAxisValue(corner, vAxis));
                int firstVertex = vertices.Count;
                for (int i = 0; i < patch.Length; i++)
                {
                    vertices.Add(tilePosition + patch[i]);
                    float cellU = InsetAtlasCell(GetAxisValue(patch[i], uAxis) - cornerU + 0.5f);
                    float cellV = InsetAtlasCell(GetAxisValue(patch[i], vAxis) - cornerV + 0.5f);
                    uv.Add(new Vector2(
                        (atlasColumn + cellU) / AtlasCellCount,
                        (atlasRow + cellV) / AtlasCellCount));
                }

                triangles.Add(firstVertex);
                triangles.Add(firstVertex + 1);
                triangles.Add(firstVertex + 2);
                triangles.Add(firstVertex);
                triangles.Add(firstVertex + 2);
                triangles.Add(firstVertex + 3);
            }
        }

        private int GetDualGridMask(
            Vector3Int globalPosition,
            Vector3 corner,
            int uAxis,
            int vAxis,
            uint tileId)
        {
            int mask = 0;
            int cornerU = Mathf.RoundToInt(GetAxisValue(corner, uAxis));
            int cornerV = Mathf.RoundToInt(GetAxisValue(corner, vAxis));
            for (int bit = 0; bit < 4; bit++)
            {
                int directionU = (bit & 1) == 0 ? -1 : 1;
                int directionV = (bit & 2) == 0 ? -1 : 1;
                var offset = new Vector3Int();
                SetAxisValue(ref offset, uAxis, GetCornerOffset(cornerU, directionU));
                SetAxisValue(ref offset, vAxis, GetCornerOffset(cornerV, directionV));

                if (TryGetNeighborPosition(globalPosition, offset, out Vector3Int neighbor)
                    && data.TryGetTileId(neighbor, out uint neighborTileId)
                    && neighborTileId == tileId)
                {
                    mask |= 1 << bit;
                }
            }

            return mask;
        }

        private static float InsetAtlasCell(float value)
            => Mathf.Lerp(AtlasCellInset, 1f - AtlasCellInset, value);

        private static int GetCornerOffset(int cornerCoordinate, int direction)
        {
            if (cornerCoordinate == 0)
                return direction < 0 ? -1 : 0;

            return direction < 0 ? 0 : 1;
        }

        private static void GetFaceTangentAxes(int faceIndex, out int uAxis, out int vAxis)
        {
            if (faceIndex < 2)
            {
                uAxis = 1;
                vAxis = 2;
            }
            else if (faceIndex < 4)
            {
                uAxis = 0;
                vAxis = 2;
            }
            else
            {
                uAxis = 0;
                vAxis = 1;
            }
        }

        private static float GetAxisValue(Vector3 value, int axis)
        {
            switch (axis)
            {
                case 0: return value.x;
                case 1: return value.y;
                default: return value.z;
            }
        }

        private static void SetAxisValue(ref Vector3Int value, int axis, int component)
        {
            switch (axis)
            {
                case 0: value.x = component; break;
                case 1: value.y = component; break;
                default: value.z = component; break;
            }
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

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        private sealed class ChunkView
        {
            public readonly GameObject GameObject;
            public readonly MeshFilter MeshFilter;
            public readonly MeshRenderer MeshRenderer;
            public readonly MeshCollider MeshCollider;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Material> Materials = new List<Material>();
            public readonly Dictionary<Material, int> MaterialIndices = new Dictionary<Material, int>();
            public readonly List<List<int>> TrianglesByMaterial = new List<List<int>>();
            public Material[] RendererMaterials = Array.Empty<Material>();

            public ChunkView(GameObject gameObject)
            {
                GameObject = gameObject;
                MeshFilter = gameObject.AddComponent<MeshFilter>();
                MeshRenderer = gameObject.AddComponent<MeshRenderer>();
                MeshCollider = gameObject.AddComponent<MeshCollider>();
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
