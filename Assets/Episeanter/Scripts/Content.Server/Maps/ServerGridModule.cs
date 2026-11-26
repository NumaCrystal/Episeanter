using Episeanter.Content.Shared.Maps.Grids;
using Episeanter.Content.Shared.Maps.Network;
using Episeanter.Content.Shared.Maps.Tiles;
using Episeanter.Content.Shared.Maps.Util;
using Mirror;
using UnityEngine;

namespace Episeanter.Content.Server.Maps
{
    public sealed class ServerGridModule : GridModule<ServerGridSystem>
    {
        public static ServerGridModule Instance { get; } = new();

        private ServerGridModule() { }

        protected override void RegisterHandlers()
        {
            NetworkServer.ReplaceHandler<GridSnapshotRequest>(OnSnapshotRequest);
            NetworkServer.ReplaceHandler<PlaceTileRequest>(OnPlaceRequest);
            NetworkServer.ReplaceHandler<RemoveTileRequest>(OnRemoveRequest);
            NetworkServer.ReplaceHandler<DamageTileRequest>(OnDamageRequest);
            NetworkServer.ReplaceHandler<CreateGridRequest>(OnCreateGridRequest);
        }

        protected override void UnregisterHandlers()
        {
            NetworkServer.UnregisterHandler<GridSnapshotRequest>();
            NetworkServer.UnregisterHandler<PlaceTileRequest>();
            NetworkServer.UnregisterHandler<RemoveTileRequest>();
            NetworkServer.UnregisterHandler<DamageTileRequest>();
            NetworkServer.UnregisterHandler<CreateGridRequest>();
        }

        private void OnSnapshotRequest(NetworkConnectionToClient conn, GridSnapshotRequest request)
        {
            if (request.RegistryHash != TileRegistry.Instance.Hash)
                return;
            if (!TryGetGrid(request.GridNetworkId, out var grid))
                return;

            grid.SendSnapshot(conn, request);
        }

        private void OnPlaceRequest(NetworkConnectionToClient conn, PlaceTileRequest request)
        {
            if (!TryGetGrid(request.GridNetworkId, out var grid)) return;
            if (!grid.CanClientEdit(conn, request.Position)) return;
            if (!GridRules.CanPlace(Grids.Values, grid, request.Position, request.TileId)) return;

            grid.Data.SetTile(request.Position, request.TileId);
        }

        private void OnRemoveRequest(NetworkConnectionToClient conn, RemoveTileRequest request)
        {
            if (!TryGetGrid(request.GridNetworkId, out var grid)) return;
            if (!grid.CanClientEdit(conn, request.Position)) return;
            if (!GridRules.CanRemove(grid.Data, request.Position)) return;

            grid.Data.RemoveTile(request.Position);
        }

        private void OnDamageRequest(NetworkConnectionToClient conn, DamageTileRequest request)
        {
            if (!TryGetGrid(request.GridNetworkId, out var grid)) return;
            if (!grid.CanClientEdit(conn, request.Position)) return;

            if (!ServerGridSystem.IsFiniteDamage(request.Damage))
                return;

            GridRules.TryApplyDamage(grid.Data, request.Position, request.Damage);
        }

        private void OnCreateGridRequest(NetworkConnectionToClient conn, CreateGridRequest request)
        {
            if(ServerGridSystem.CreateGrid(request.Position, 0, out ServerGridSystem gridSystem))
                gridSystem.Data.SetTile(gridSystem.Data.WorldToCell(request.Position, gridSystem), request.TileId);
        }
    }
}