using UnityEngine;

namespace Episeanter.Content.Shared.Maps
{
    [CreateAssetMenu(fileName = "NewTile", menuName = "Episeanter/Grids/Tile Definition")]
    public sealed class TileDefinition : ScriptableObject
    {
        [SerializeField]
         private float maxHealth = 100f;
        [SerializeField]
        private float minimumDamage = 0.01f;

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
