using UnityEngine;

namespace Episeanter.Content.Shared.Maps
{
    [CreateAssetMenu(fileName = "NewTile", menuName = "Episeanter/Grids/Tile Definition")]
    public sealed class TileDefinition : ScriptableObject
    {
        [SerializeField, Min(0.01f)] 
        private float maxHealth = 100f;
        [SerializeField, Min(0f)]
        private float minimumDamage;

        public float MaxHealth => maxHealth;
        public float MinimumDamage => minimumDamage;

        private void OnValidate()
        {
            maxHealth = float.IsNaN(maxHealth) || float.IsInfinity(maxHealth)
                ? 100f
                : Mathf.Max(0.01f, maxHealth);
            minimumDamage = float.IsNaN(minimumDamage) || float.IsInfinity(minimumDamage)
                ? 0f
                : Mathf.Max(0f, minimumDamage);
        }
    }
}
