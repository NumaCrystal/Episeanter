using System.Collections.Generic;
using Episeanter.Content.Shared.Network;

namespace Episeanter.Content.Shared.Maps.Grids
{
    public abstract class GridModule<TGrid> : INetModule where TGrid : GridSystem
    {
        private readonly Dictionary<uint, TGrid> grids = new();

        public IReadOnlyDictionary<uint, TGrid> Grids => grids;

        public bool TryGetGrid(uint netId, out TGrid grid) => grids.TryGetValue(netId, out grid);

        public void Add(TGrid grid) => grids[grid.netId] = grid;
        public void Remove(TGrid grid) => grids.Remove(grid.netId);

        public void Start() 
        {
            RegisterHandlers();
        }

        public void Stop()
        {
            UnregisterHandlers();
            grids.Clear();
        }

        protected abstract void RegisterHandlers();
        protected abstract void UnregisterHandlers();
    }
}