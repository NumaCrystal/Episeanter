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
    }
}
