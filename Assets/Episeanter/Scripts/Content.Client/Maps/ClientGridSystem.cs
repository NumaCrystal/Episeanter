using Episeanter.Content.Shared.Maps.Grids;
using Episeanter.Content.Shared.Maps.Tiles;
using Episeanter.Content.Shared.Maps.Network;
using Episeanter.Content.Shared.Maps.Util;
using Mirror;
using UnityEngine;

namespace Episeanter.Content.Client.Maps
{
    [RequireComponent(typeof(GridRenderer))]
    public sealed class ClientGridSystem : GridSystem
    {
        private GridRenderer gridRenderer;

        public override void OnStartClient()
        {
            if (gridRenderer == null)
                gridRenderer = GetComponent<GridRenderer>();

            Initialize();
            gridRenderer.Bind(Data);

            ClientGridModule.Instance.Add(this);

            NetworkClient.Send(new GridSnapshotRequest { GridNetworkId = netId, RegistryHash = TileRegistry.Instance.Hash });
        }

        public override void OnStopClient()
        {
            if (gridRenderer != null)
                gridRenderer.Unbind();

            ClientGridModule.Instance.Remove(this);

            Data = null;
        }

        public static void RequestCreateGrid(Vector3 position, uint tileId)
        {
            if (!NetworkClient.active)
                return;
            if (!GridRules.CanCreate(ClientGridModule.Instance.Grids.Values, position))
                return;

            NetworkClient.Send(new CreateGridRequest { Position = position, TileId = tileId });
        }

        public void RequestPlace(Vector3Int position, uint tileId)
        {
            if (!NetworkClient.active || Data == null)
                return;
            if (!GridRules.CanPlace(ClientGridModule.Instance.Grids.Values, this, position, tileId))
                return;

            NetworkClient.Send(new PlaceTileRequest { GridNetworkId = netId, Position = position, TileId = tileId });
        }

        public void RequestRemove(Vector3Int position)
        {
            if (!NetworkClient.active || Data == null)
                return;
            if (!GridRules.CanRemove(Data, position))
                return;

            NetworkClient.Send(new RemoveTileRequest { GridNetworkId = netId, Position = position });
        }

        public void RequestDamage(Vector3Int position, float damage)
        {
            if (!NetworkClient.active || Data == null)
                return;
            if (!GridRules.CanDamage(Data, position, damage, out _))
                return;

            NetworkClient.Send(new DamageTileRequest { GridNetworkId = netId, Position = position, Damage = damage });
        }
    }
}
