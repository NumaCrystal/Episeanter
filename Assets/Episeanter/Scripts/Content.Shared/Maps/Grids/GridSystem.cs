using System.Collections.Generic;
using System.Data.Common;
using Episeanter.Content.Shared.Maps.Tiles;
using Episeanter.Content.Shared.Maps.Util;
using Mirror;
using UnityEngine;

namespace Episeanter.Content.Shared.Maps.Grids
{
    public abstract class GridSystem : NetworkBehaviour
    {
        public GridData Data { get; protected set; }
        protected static TileRegistry Registry => TileRegistry.Instance;

        protected void Initialize()
        {
            GridSettings s = GridSettings.Instance;
            Data = new GridData(s.HeightLimit, s.ChunkSize, netId);
        }


        

        public static bool FindGridAt<TGrid>(IEnumerable<TGrid> grids, Vector3 point, out TGrid found) where TGrid : GridSystem
        {
            found = null;

            foreach (var g in grids)
            {
                if (g == null || g.Data == null)
                    continue;

                if (g.Data.IsTileConnected(g.Data.WorldToCell(point, g)))
                {
                    found = g;
                    return true;
                }
            }
            return false;
        }

        public static List<TGrid> GetGridsAt<TGrid>(IEnumerable<TGrid> grids, Vector3 point) where TGrid : GridSystem
        {
            List<TGrid> gridList = new();

            foreach (var g in grids)
            {
                if (g == null || g.Data == null)
                    continue;

                if (g.Data.IsTileConnected(g.Data.WorldToCell(point, g)))
                {
                    gridList.Add(g);
                }
            }
            return gridList;
        }

        public static bool IsGridEqual(MonoBehaviour gameObject, uint netId, out GridSystem grid)
        {
            grid = default;

            if (!gameObject.TryGetComponent<GridSystem>(out grid))
                return false;

            if (grid.netId != netId)
            {
                grid = default;
                return false;
            }

            return true;
        }
    }
}
