using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Episeanter.Grids
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity), typeof(Grid), typeof(GridVisual))]
    [RequireComponent(typeof(Damageable))]
    public sealed class NetworkGrid : NetworkBehaviour
    {
        private const int SnapshotBatchSize = 512;

        [SerializeField, Tooltip("Allows connected players to edit any cell within the configured distance.")]
        private bool allowClientEdits = true;
        [SerializeField, Min(0.1f)] private float maxEditDistance = 8f;
        [SerializeField] private Grid grid;
        [SerializeField] private GridVisual gridVisual;
        [SerializeField] private Damageable damageable;

        private void Awake()
        {
            if (grid == null)
                grid = GetComponent<Grid>();
            if (gridVisual == null)
                gridVisual = GetComponent<GridVisual>();
            if (damageable == null)
                damageable = GetComponent<Damageable>();

            grid.ChunkCountChanged += HandleChunkCountChanged;
        }

        public override void OnStartServer()
        {
            grid.SetClientReplica(false);
            grid.TileChanged += HandleServerTileChanged;
            damageable.HealthChanged += HandleServerHealthChanged;

            if (isServerOnly)
                gridVisual.enabled = false;
        }

        public override void OnStopServer()
        {
            grid.TileChanged -= HandleServerTileChanged;
            damageable.HealthChanged -= HandleServerHealthChanged;
        }

        private void OnDestroy()
        {
            if (grid != null)
                grid.ChunkCountChanged -= HandleChunkCountChanged;
        }

        public override void OnStartClient()
        {
            grid.SetClientReplica(!isServer);
            if (!isServer)
            {
                gridVisual.SetMeshCollidersEnabled(false);
                grid.ClearDataForClient();
                gridVisual.ClearVisualTiles();
                damageable.ClearRemoteHealth();
                CmdRequestSnapshot();
            }

            gridVisual.enabled = true;
        }

        public override void OnStopClient()
        {
            gridVisual.ClearVisualTiles();
            if (!isServer)
                grid.SetClientReplica(false);
        }

        public bool RequestSetTile(Vector3Int position, uint tileId)
        {
            if (isServer)
                return TrySetTile(position, tileId, null);

            if (!isClient || netId == 0)
            {
                Debug.LogWarning("Cannot edit the grid because its network object is not spawned on this client.", this);
                return false;
            }

            CmdRequestSetTile(position, tileId);
            return true;
        }

        public bool RequestRemoveTile(Vector3Int position)
        {
            if (isServer)
                return TryRemoveTile(position, null);

            if (!isClient || netId == 0)
            {
                Debug.LogWarning("Cannot edit the grid because its network object is not spawned on this client.", this);
                return false;
            }

            CmdRequestRemoveTile(position);
            return true;
        }

        public bool RequestDamageTile(Vector3Int position, float damage)
        {
            if (isServer)
                return TryDamageTile(position, damage, null);

            if (!isClient || netId == 0)
            {
                Debug.LogWarning("Cannot damage the grid because its network object is not spawned on this client.", this);
                return false;
            }

            CmdRequestDamageTile(position, damage);
            return true;
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestSetTile(
            Vector3Int position,
            uint tileId,
            NetworkConnectionToClient sender = null)
        {
            TrySetTile(position, tileId, sender);
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestRemoveTile(
            Vector3Int position,
            NetworkConnectionToClient sender = null)
        {
            TryRemoveTile(position, sender);
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestDamageTile(
            Vector3Int position,
            float damage,
            NetworkConnectionToClient sender = null)
        {
            TryDamageTile(position, damage, sender);
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestSnapshot(NetworkConnectionToClient sender = null)
        {
            if (sender == null)
                return;

            var positions = new List<Vector3Int>(SnapshotBatchSize);
            var tileIds = new List<uint>(SnapshotBatchSize);
            var healths = new List<float>(SnapshotBatchSize);
            bool clearBeforeBatch = true;
            foreach (KeyValuePair<Vector3Int, uint> tile in grid.GetStoredTiles())
            {
                positions.Add(tile.Key);
                tileIds.Add(tile.Value);
                healths.Add(damageable.GetHealth(tile.Key));
                if (positions.Count == SnapshotBatchSize)
                {
                    TargetApplySnapshot(sender, positions.ToArray(), tileIds.ToArray(), healths.ToArray(), clearBeforeBatch);
                    clearBeforeBatch = false;
                    positions.Clear();
                    tileIds.Clear();
                    healths.Clear();
                }
            }

            TargetApplySnapshot(sender, positions.ToArray(), tileIds.ToArray(), healths.ToArray(), clearBeforeBatch);
        }

        [TargetRpc]
        private void TargetApplySnapshot(
            NetworkConnectionToClient target,
            Vector3Int[] positions,
            uint[] tileIds,
            float[] healths,
            bool clearBeforeBatch)
        {
            if (positions.Length != tileIds.Length || positions.Length != healths.Length)
            {
                Debug.LogError("Received a grid snapshot batch with mismatched tile data counts.", this);
                return;
            }

            if (clearBeforeBatch)
            {
                gridVisual.ClearVisualTiles();
                damageable.ClearRemoteHealth();
            }

            for (int i = 0; i < positions.Length; i++)
            {
                if (tileIds[i] == 0)
                {
                    Debug.LogError($"Received reserved empty tile ID in grid snapshot at {positions[i]}.", this);
                    continue;
                }

                float maxHealth = TileRegistry.Instance.GetDefinition(tileIds[i]).MaxHealth;
                if (float.IsNaN(healths[i]) || float.IsInfinity(healths[i])
                    || healths[i] <= 0f || healths[i] > maxHealth)
                {
                    Debug.LogError($"Received invalid tile health {healths[i]} at {positions[i]}.", this);
                    continue;
                }

                gridVisual.ApplyVisualTile(positions[i], tileIds[i]);
                damageable.ApplyRemoteTileChanged(positions[i], tileIds[i]);
                if (healths[i] < maxHealth)
                    damageable.ApplyRemoteHealth(positions[i], healths[i]);
            }
        }

        [ClientRpc]
        private void RpcApplyTile(Vector3Int position, uint tileId)
        {
            if (isServer)
                return;

            if (tileId == 0)
                gridVisual.RemoveVisualTile(position);
            else
                gridVisual.ApplyVisualTile(position, tileId);

            damageable.ApplyRemoteTileChanged(position, tileId);
        }

        [ClientRpc]
        private void RpcApplyHealth(Vector3Int position, float health)
        {
            if (!isServer)
                damageable.ApplyRemoteHealth(position, health);
        }

        internal bool TrySetTile(Vector3Int position, uint tileId, NetworkConnectionToClient sender)
        {
            if (!CanEdit(position, sender))
                return false;

            if (tileId == 0 || !TileRegistry.Instance.TryGetDefinition(tileId, out _))
            {
                Debug.LogWarning($"Rejected grid edit with unregistered tile ID {tileId}.", this);
                return false;
            }

            grid.SetTile(position, tileId);
            return true;
        }

        private bool TryRemoveTile(Vector3Int position, NetworkConnectionToClient sender)
        {
            if (!CanEdit(position, sender))
                return false;

            grid.RemoveTile(position);
            return true;
        }

        private bool TryDamageTile(Vector3Int position, float damage, NetworkConnectionToClient sender)
        {
            if (!CanEdit(position, sender))
                return false;

            if (float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f)
            {
                Debug.LogWarning($"Rejected invalid tile damage value {damage}.", this);
                return false;
            }

            return damageable.TryApplyDamage(position, damage);
        }

        private bool CanEdit(Vector3Int position, NetworkConnectionToClient sender)
        {
            if (position.y < 0 || position.y >= grid.HeightLimit)
            {
                Debug.LogWarning($"Rejected grid edit outside height limits: {position}.", this);
                return false;
            }

            if (sender == null)
                return true;

            if (!allowClientEdits)
            {
                Debug.LogWarning($"Rejected grid edit from connection {sender.connectionId}: client editing is disabled.", this);
                return false;
            }

            if (sender.identity == null)
            {
                Debug.LogWarning($"Rejected grid edit from connection {sender.connectionId}: no player object is assigned.", this);
                return false;
            }

            Vector3 cellCenter = grid.CellToWorld(position) + Vector3.one * 0.5f;
            float maxDistanceSquared = maxEditDistance * maxEditDistance;
            if ((sender.identity.transform.position - cellCenter).sqrMagnitude > maxDistanceSquared)
            {
                Debug.LogWarning($"Rejected grid edit outside interaction range: {position}.", this);
                return false;
            }

            return true;
        }

        private void HandleServerTileChanged(Vector3Int position, uint tileId)
        {
            if (isServer)
                RpcApplyTile(position, tileId);
        }

        private void HandleServerHealthChanged(Vector3Int position, float health)
        {
            if (isServer)
                RpcApplyHealth(position, health);
        }

        private void HandleChunkCountChanged(int chunkCount)
        {
            if (chunkCount != 0)
                return;

            if (NetworkServer.active)
            {
                if (netId != 0)
                    NetworkServer.Destroy(gameObject);
                else
                    Destroy(gameObject);
            }
            else if (!NetworkClient.active)
            {
                Destroy(gameObject);
            }
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            maxEditDistance = Mathf.Max(0.1f, maxEditDistance);
        }
    }
}
