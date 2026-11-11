using System.Collections.Generic;
using UnityEngine;

namespace Episeanter.Grids
{
    public sealed partial class GridVisual
    {
        private static readonly Vector3[][] FaceVertices =
        {
            new[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) },
            new[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) },
            new[] { new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0), new Vector3(0, 1, 0) },
            new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) },
            new[] { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) },
            new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }
        };

        private void AddDualGridFace(
            Vector3Int tilePosition,
            Vector3Int globalPosition,
            int faceIndex,
            uint tileId,
            Material material,
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            Vector3[] face = FaceVertices[faceIndex];
            GetFaceTangentAxes(faceIndex, out int uAxis, out int vAxis);
            Texture atlas = GetMaterialAtlas(material);
            Vector2 halfTexel = atlas != null
                ? new Vector2(0.5f / atlas.width, 0.5f / atlas.height)
                : Vector2.zero;
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

                int mask = GetDualGridMask(globalPosition, corner, uAxis, vAxis, tileId);
                int atlasColumn = mask % 4;
                int atlasRow = mask / 4;
                float cornerU = GetAxisValue(corner, uAxis);
                float cornerV = GetAxisValue(corner, vAxis);
                float sourceMinU = (atlasColumn + (cornerU >= 0.5f ? 0.5f : 0f)) * 0.25f;
                float sourceMinV = (atlasRow + (cornerV >= 0.5f ? 0.5f : 0f)) * 0.25f;
                int firstVertex = vertices.Count;
                for (int i = 0; i < patch.Length; i++)
                {
                    vertices.Add(tilePosition + patch[i]);
                    uv.Add(new Vector2(
                        Mathf.Clamp(
                            (atlasColumn + GetAxisValue(patch[i], uAxis)) * 0.25f,
                            sourceMinU + halfTexel.x,
                            sourceMinU + 0.125f - halfTexel.x),
                        Mathf.Clamp(
                            (atlasRow + GetAxisValue(patch[i], vAxis)) * 0.25f,
                            sourceMinV + halfTexel.y,
                            sourceMinV + 0.125f - halfTexel.y)));
                }

                triangles.Add(firstVertex);
                triangles.Add(firstVertex + 1);
                triangles.Add(firstVertex + 2);
                triangles.Add(firstVertex);
                triangles.Add(firstVertex + 2);
                triangles.Add(firstVertex + 3);
            }
        }

        private static Texture GetMaterialAtlas(Material material)
        {
            if (material.HasProperty("_BaseMap"))
                return material.GetTexture("_BaseMap");

            return material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
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
                    && TryReadVisualTileId(neighbor, out uint neighborTileId)
                    && neighborTileId == tileId)
                {
                    mask |= 1 << bit;
                }
            }

            return mask;
        }

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
                uAxis = 2;
                vAxis = 1;
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
    }
}
