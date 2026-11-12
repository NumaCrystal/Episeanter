using UnityEngine;

namespace Episeanter.Content.Shared.Maps
{
    [CreateAssetMenu(fileName = "NewTile", menuName = "Episeanter/Grids/Tile Definition")]
    public sealed class TileDefinition : ScriptableObject
    {
        [SerializeField]
        private bool solid = true;

        public bool Solid => solid;
    }
}
