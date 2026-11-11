using System;
using System.Collections.Generic;
using UnityEngine;

namespace Episeanter.Grids
{
    public sealed partial class GridVisual
    {
        private static readonly Vector3Int[] NeighborOffsets =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1)
        };

        private void LateUpdate()
        {
            foreach (Vector2Int chunkPosition in dirtyChunks)
            {
                if (chunks.TryGetValue(chunkPosition, out Chunk chunk))
                    RebuildChunk(chunkPosition, chunk);
            }

            dirtyChunks.Clear();
        }

        private void RebuildChunk(Vector2Int position, Chunk chunk)
        {
            if (chunk.Tiles.Count == 0)
            {
                chunks.Remove(position);
                DestroyUnityObject(chunk.GameObject);
                DestroyUnityObject(chunk.MeshFilter.sharedMesh);
                DestroyUnityObject(chunk.DamageMeshFilter.sharedMesh);
                return;
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

            foreach (KeyValuePair<Vector3Int, uint> tileEntry in chunk.Tiles)
            {
                TileDefinition definition = GetVisualRegistry().GetDefinition(tileEntry.Value);
                for (int face = 0; face < NeighborOffsets.Length; face++)
                {
                    if (face == 3)
                        continue;

                    Vector3Int globalPosition = GetGlobalPosition(position, tileEntry.Key);
                    if (TryGetNeighborPosition(globalPosition, NeighborOffsets[face], out Vector3Int neighbor)
                        && TryReadVisualTileId(neighbor, out _))
                        continue;

                    Material material = face == 2 ? definition.Material : definition.SideMaterial;
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
                        material,
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
            if (meshCollidersEnabled)
                chunk.MeshCollider.sharedMesh = mesh;
            if (!chunk.HasSameMaterials(materials))
            {
                chunk.RendererMaterials = materials.ToArray();
                chunk.MeshRenderer.sharedMaterials = chunk.RendererMaterials;
            }

            RebuildDamageMesh(position, chunk);
        }

        private void RebuildDamageMesh(Vector2Int chunkPosition, Chunk chunk)
        {
            Mesh mesh = chunk.DamageMeshFilter.sharedMesh;
            mesh.Clear();
            chunk.DamageVertices.Clear();
            chunk.DamageUv.Clear();
            chunk.DamageTriangles.Clear();

            if (damageLayerMaterial == null || damageStagesByPosition.Count == 0)
            {
                chunk.DamageMeshRenderer.enabled = false;
                return;
            }

            foreach (KeyValuePair<Vector3Int, uint> tileEntry in chunk.Tiles)
            {
                Vector3Int globalPosition = GetGlobalPosition(chunkPosition, tileEntry.Key);
                if (!damageStagesByPosition.TryGetValue(globalPosition, out int stage))
                    continue;

                for (int face = 0; face < NeighborOffsets.Length; face++)
                {
                    if (face == 3)
                        continue;

                    if (TryGetNeighborPosition(globalPosition, NeighborOffsets[face], out Vector3Int neighbor)
                        && TryReadVisualTileId(neighbor, out _))
                        continue;

                    AddDamageFace(
                        tileEntry.Key,
                        face,
                        stage,
                        chunk.DamageVertices,
                        chunk.DamageUv,
                        chunk.DamageTriangles);
                }
            }

            if (chunk.DamageTriangles.Count == 0)
            {
                chunk.DamageMeshRenderer.enabled = false;
                return;
            }

            mesh.SetVertices(chunk.DamageVertices);
            mesh.SetUVs(0, chunk.DamageUv);
            mesh.SetTriangles(chunk.DamageTriangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            chunk.DamageMeshRenderer.sharedMaterial = damageLayerMaterial;
            chunk.DamageMeshRenderer.enabled = true;
        }

        private void AddDamageFace(
            Vector3Int tilePosition,
            int faceIndex,
            int stage,
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            Vector3[] face = FaceVertices[faceIndex];
            Vector3 offset = (Vector3)NeighborOffsets[faceIndex] * 0.001f;
            GetFaceTangentAxes(faceIndex, out int uAxis, out int vAxis);
            int firstVertex = vertices.Count;

            for (int i = 0; i < face.Length; i++)
            {
                Vector3 corner = face[i];
                vertices.Add(tilePosition + corner + offset);
                float stageStart = (float)stage / damageStageCount;
                float stageWidth = 1f / damageStageCount;
                uv.Add(new Vector2(
                    stageStart + damageLayerHalfTexelU
                        + GetAxisValue(corner, uAxis) * (stageWidth - 2f * damageLayerHalfTexelU),
                    GetAxisValue(corner, vAxis)));
            }

            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 1);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex + 3);
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
            ClearVisualState();
        }

        private sealed class Chunk
        {
            public readonly GameObject GameObject;
            public readonly MeshFilter MeshFilter;
            public readonly MeshRenderer MeshRenderer;
            public readonly MeshCollider MeshCollider;
            public readonly GameObject DamageGameObject;
            public readonly MeshFilter DamageMeshFilter;
            public readonly MeshRenderer DamageMeshRenderer;
            public readonly Dictionary<Vector3Int, uint> Tiles = new Dictionary<Vector3Int, uint>();
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Vector3> DamageVertices = new List<Vector3>();
            public readonly List<Vector2> DamageUv = new List<Vector2>();
            public readonly List<int> DamageTriangles = new List<int>();
            public readonly List<Material> Materials = new List<Material>();
            public readonly Dictionary<Material, int> MaterialIndices = new Dictionary<Material, int>();
            public readonly List<List<int>> TrianglesByMaterial = new List<List<int>>();
            public Material[] RendererMaterials = Array.Empty<Material>();

            public Chunk(GameObject gameObject)
            {
                GameObject = gameObject;
                MeshFilter = gameObject.AddComponent<MeshFilter>();
                MeshRenderer = gameObject.AddComponent<MeshRenderer>();
                MeshCollider = gameObject.AddComponent<MeshCollider>();
                DamageGameObject = new GameObject($"{gameObject.name} Damage Layer");
                DamageGameObject.transform.SetParent(gameObject.transform, false);
                DamageMeshFilter = DamageGameObject.AddComponent<MeshFilter>();
                DamageMeshRenderer = DamageGameObject.AddComponent<MeshRenderer>();
                DamageMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                DamageMeshRenderer.receiveShadows = false;
                DamageMeshFilter.sharedMesh = new Mesh
                {
                    name = $"{gameObject.name} Damage Layer",
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
                };
                DamageMeshFilter.sharedMesh.MarkDynamic();
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
