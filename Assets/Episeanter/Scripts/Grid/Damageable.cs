using System;
using System.Collections.Generic;
using UnityEngine;

namespace Episeanter.Grids
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Grid), typeof(GridVisual))]
    public sealed class Damageable : MonoBehaviour
    {
        [SerializeField] private Material damageLayerMaterial;

        private readonly Dictionary<Vector3Int, float> remainingHealth = new Dictionary<Vector3Int, float>();
        private Grid grid;
        private GridVisual gridVisual;
        private NetworkGrid networkGrid;
        private int damageStageCount = 1;

        internal event Action<Vector3Int, float> HealthChanged;

        public int DamageStages => damageStageCount;

        private void Awake()
        {
            grid = GetComponent<Grid>();
            gridVisual = GetComponent<GridVisual>();
            networkGrid = GetComponent<NetworkGrid>();
        }

        private void OnEnable()
        {
            grid = GetComponent<Grid>();
            gridVisual = GetComponent<GridVisual>();
            grid.TileChanged += HandleTileChanged;
            ConfigureVisuals();

            foreach (KeyValuePair<Vector3Int, float> health in remainingHealth)
                UpdateVisualStage(health.Key, health.Value);
        }

        private void OnDisable()
        {
            if (grid != null)
                grid.TileChanged -= HandleTileChanged;
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled && gridVisual != null)
            {
                ConfigureVisuals();
                foreach (KeyValuePair<Vector3Int, float> health in remainingHealth)
                    UpdateVisualStage(health.Key, health.Value);
            }
        }

        /// <summary>Returns the current health of a tile, or 0 for an empty cell.</summary>
        public float GetHealth(Vector3Int position)
        {
            if (!TryGetTileId(position, out uint tileId))
                return 0f;

            TileDefinition definition = TileRegistry.Instance.GetDefinition(tileId);
            return remainingHealth.TryGetValue(position, out float health) ? health : definition.MaxHealth;
        }

        /// <summary>Applies damage locally or requests authoritative damage from the server.</summary>
        public bool DamageTile(Vector3Int position, float damage)
        {
            if (!IsValidDamage(damage))
            {
                Debug.LogWarning($"Rejected invalid tile damage value {damage}.", this);
                return false;
            }

            if (networkGrid != null)
                return networkGrid.RequestDamageTile(position, damage);

            return TryApplyDamage(position, damage);
        }

        internal bool TryApplyDamage(Vector3Int position, float damage)
        {
            if (!IsValidDamage(damage) || !grid.TryGetTileId(position, out uint tileId))
                return false;

            TileDefinition definition = TileRegistry.Instance.GetDefinition(tileId);
            if (damage < definition.MinimumDamage)
                return false;

            float health = remainingHealth.TryGetValue(position, out float currentHealth)
                ? currentHealth
                : definition.MaxHealth;
            float updatedHealth = health - damage;
            if (updatedHealth <= 0f)
            {
                grid.RemoveTile(position);
                return true;
            }

            remainingHealth[position] = updatedHealth;
            UpdateVisualStage(position, updatedHealth);
            HealthChanged?.Invoke(position, updatedHealth);
            return true;
        }

        internal void ApplyRemoteTileChanged(Vector3Int position, uint tileId)
        {
            remainingHealth.Remove(position);
            gridVisual.SetDamageStage(position, -1);
            if (tileId != 0)
                UpdateVisualStage(position, TileRegistry.Instance.GetDefinition(tileId).MaxHealth);
        }

        internal void ApplyRemoteHealth(Vector3Int position, float health)
        {
            if (!gridVisual.TryGetTile(position, out TileDefinition definition))
            {
                Debug.LogWarning($"Received health for an empty visual tile at {position}.", this);
                return;
            }

            if (float.IsNaN(health) || float.IsInfinity(health) || health <= 0f || health > definition.MaxHealth)
            {
                Debug.LogError($"Received invalid health value {health} for tile at {position}.", this);
                return;
            }

            if (health >= definition.MaxHealth)
                remainingHealth.Remove(position);
            else
                remainingHealth[position] = health;

            UpdateVisualStage(position, health);
        }

        internal void ClearRemoteHealth()
        {
            remainingHealth.Clear();
        }

        private void HandleTileChanged(Vector3Int position, uint tileId)
        {
            remainingHealth.Remove(position);
            gridVisual.SetDamageStage(position, -1);
            if (tileId != 0)
                UpdateVisualStage(position, TileRegistry.Instance.GetDefinition(tileId).MaxHealth);
        }

        private void ConfigureVisuals()
        {
            if (gridVisual == null)
                return;

            damageStageCount = gridVisual.ConfigureDamageOverlay(damageLayerMaterial);
        }

        private void UpdateVisualStage(Vector3Int position, float health)
        {
            if (!TryGetTileId(position, out uint tileId))
                return;

            float maxHealth = TileRegistry.Instance.GetDefinition(tileId).MaxHealth;
            if (health >= maxHealth)
            {
                gridVisual.SetDamageStage(position, -1);
                return;
            }

            float damageFraction = 1f - health / maxHealth;
            int stage = Mathf.Clamp(Mathf.FloorToInt(damageFraction * damageStageCount), 0, damageStageCount - 1);
            gridVisual.SetDamageStage(position, stage);
        }

        private bool TryGetTileId(Vector3Int position, out uint tileId)
        {
            tileId = 0;
            if (grid != null && grid.TryGetTileId(position, out tileId))
                return true;

            return gridVisual != null && gridVisual.TryGetVisualTileId(position, out tileId);
        }

        private static bool IsValidDamage(float damage)
        {
            return damage > 0f && !float.IsNaN(damage) && !float.IsInfinity(damage);
        }
    }
}
