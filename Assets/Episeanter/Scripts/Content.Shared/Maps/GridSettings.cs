using UnityEngine;

namespace Episeanter.Content.Shared.Maps
{
    [CreateAssetMenu(fileName = "GridSettings", menuName = "Episeanter/Grids/Grid Settings")]
    public sealed class GridSettings : ScriptableObject
    {
        [SerializeField, Min(1)] private int heightLimit = 2;
        [SerializeField, Min(1)] private int chunkSize = 16;

        public int HeightLimit => heightLimit;
        public int ChunkSize => chunkSize;

        private void OnValidate()
        {
            heightLimit = Mathf.Max(1, heightLimit);
            chunkSize = Mathf.Max(1, chunkSize);
        }
    }
}
