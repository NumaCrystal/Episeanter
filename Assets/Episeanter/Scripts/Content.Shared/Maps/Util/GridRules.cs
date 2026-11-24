using UnityEngine;
using Episeanter.Content.Shared.Maps.Grids;
using Episeanter.Content.Shared.Maps.Tiles;
using System.Collections.Generic;

namespace Episeanter.Content.Shared.Maps.Util
{
    public static class GridRules
    {
        public static bool CanCreate<TGrid>(IEnumerable<TGrid> grids, Vector3 position, uint tileId) where TGrid : GridSystem
        {
            if (tileId == 0 || position.y != GridSettings.Instance.GridY)
                return false;
            if (!TileRegistry.Instance.TryGetDefinition(tileId, out _))
                return false;
            if (GridSystem.FindGridAt<TGrid>(grids, position, out _))
                return false;

            return true;
        }

        public static bool CanPlace(IEnumerable<GridSystem> grids, GridSystem grid, Vector3Int position, uint tileId)
        {
            if (grid == null)
                return false;
            if (tileId == 0 || !grid.Data.InBounds(position))
                return false;
            if (!TileRegistry.Instance.TryGetDefinition(tileId, out _))
                return false;
            if (grid.Data.GetTileId(new Vector3Int(position.x, position.y - 1, position.z)) == 0
                && position.y > 0)
                return false;
            if (!grid.Data.IsTileConnected(position))
                return false;
            if (GridSystem.GetGridsAt<GridSystem>(grids, grid.Data.CellToWorld(position, grid.transform, true)).Count > 1)
                return false;

            return !grid.Data.TryGetTileId(position, out uint current) || current != tileId;
        }

        public static bool CanRemove(GridData data, Vector3Int position){
            if (data == null)
                return false;
            if (data.GetTileId(new Vector3Int(position.x, position.y + 1, position.z)) == 0)
                return false;

            return data.TryGetTileId(position, out _);
        }

        public static bool CanDamage(
            GridData data,
            Vector3Int position,
            float damage,
            out TileDefinition definition)
        {
            definition = null;
            if (data == null || !IsFiniteDamage(damage))
                return false;
            if (!data.TryGetTileId(position, out uint tileId))
                return false;
            if (data.GetTileId(new Vector3Int(position.x, position.y + 1, position.z)) != 0)
                return false;
            if (!TileRegistry.Instance.TryGetDefinition(tileId, out definition))
                return false;

            return damage >= definition.MinimumDamage;
        }

        public static bool TryApplyDamage(
            GridData data,
            Vector3Int position,
            float damage)
        {
            if (!CanDamage(data, position, damage, out TileDefinition definition))
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
