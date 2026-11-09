using UnityEngine;

namespace Episeanter.Grids
{
    /// <summary>
    /// Surface material textures are 4x4 dual-grid atlases indexed by mask (column = mask % 4,
    /// row from the bottom = mask / 4). Mask bits 0-3 represent the negative/negative,
    /// positive/negative, negative/positive, and positive/positive neighboring cells.
    /// </summary>
    [CreateAssetMenu(fileName = "NewTile", menuName = "Episeanter/Grids/Tile Definition")]
    public sealed class TileDefinition : ScriptableObject
    {
        [SerializeField] private Material material;
        [SerializeField, Tooltip("Optional material for side surfaces. Uses the default material when empty.")]
        private Material sideMaterial;

        public Material Material => material;
        public Material SideMaterial => sideMaterial != null ? sideMaterial : material;

        internal void ValidateTextureAtlases()
        {
            ValidateTextureAtlas(material);
            ValidateTextureAtlas(SideMaterial);
        }

        private void ValidateTextureAtlas(Material surfaceMaterial)
        {
            Texture texture = surfaceMaterial.HasProperty("_BaseMap")
                ? surfaceMaterial.GetTexture("_BaseMap")
                : surfaceMaterial.HasProperty("_MainTex")
                    ? surfaceMaterial.GetTexture("_MainTex")
                    : null;

            if (texture != null && (texture.width < 4 || texture.height < 4
                || texture.width % 4 != 0 || texture.height % 4 != 0))
                throw new System.InvalidOperationException(
                    $"Material '{surfaceMaterial.name}' on tile '{name}' must use a texture with dimensions divisible by 4 for its 4x4 dual-grid atlas.");
        }
    }
}
