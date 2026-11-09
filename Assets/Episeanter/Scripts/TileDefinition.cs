using UnityEngine;

namespace Episeanter.Grids
{
    [CreateAssetMenu(fileName = "NewTile", menuName = "Episeanter/Grids/Tile Definition")]
    public sealed class TileDefinition : ScriptableObject
    {
        [SerializeField] private Material material;

        public Material Material => material;
    }
}
