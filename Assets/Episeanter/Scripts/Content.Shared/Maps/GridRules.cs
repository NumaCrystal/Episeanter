using UnityEngine;

namespace Episeanter.Content.Shared.Maps
{
    public static class GridRules
    {
        public static bool CanPlace(GridData data, TileRegistry registry, Vector3Int position, uint tileId)
        {
            if (data == null || registry == null)
                return false;
            if (tileId == 0 || !data.InBounds(position))
                return false;
            if (!registry.TryGetDefinition(tileId, out _))
                return false;

            return !data.TryGetTileId(position, out uint current) || current != tileId;
        }

        public static bool CanRemove(GridData data, Vector3Int position)
            => data != null && data.TryGetTileId(position, out _);

        public static bool CanDamage(
            GridData data,
            TileRegistry registry,
            Vector3Int position,
            float damage,
            out TileDefinition definition)
        {
            definition = null;
            if (data == null || registry == null || !IsFiniteDamage(damage))
                return false;
            if (!data.TryGetTileId(position, out uint tileId))
                return false;
            if (!registry.TryGetDefinition(tileId, out definition))
                return false;

            return damage >= definition.MinimumDamage;
        }

        public static bool TryApplyDamage(
            GridData data,
            TileRegistry registry,
            Vector3Int position,
            float damage)
        {
            if (!CanDamage(data, registry, position, damage, out TileDefinition definition))
                return false;

            float health = data.GetHealth(position, definition.MaxHealth) - damage;
            if (health <= 0f)
                return data.RemoveTile(position);

            return data.SetHealth(position, health);
        }

        private static bool IsFiniteDamage(float damage)
            => damage > 0f && !float.IsNaN(damage) && !float.IsInfinity(damage);
    }
}
