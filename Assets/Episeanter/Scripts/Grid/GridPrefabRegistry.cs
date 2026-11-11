using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Episeanter.Grids
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class GridPrefabRegistry : NetworkBehaviour
    {
        private static GridPrefabRegistry instance;

        [SerializeField, Tooltip("Network-spawnable prefab used when creating a disconnected grid.")]
        private GameObject newGridPrefab;
        [SerializeField] private bool allowClientGridCreation = true;
        [SerializeField, Min(0.1f)] private float maxGridCreationDistance = 8f;

        public static GridPrefabRegistry Instance
        {
            get
            {
                if (instance == null)
                    throw new InvalidOperationException("No GridPrefabRegistry singleton exists in the loaded scene.");

                return instance;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogError($"Duplicate grid prefab registry '{name}' found. Destroying the duplicate object.", this);
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSingleton()
        {
            instance = null;
        }

        public bool TryGetNewGridPrefab(out GameObject prefab)
        {
            prefab = newGridPrefab;
            if (prefab == null)
            {
                Debug.LogError("Cannot create a grid because New Grid Prefab is not assigned.", this);
                return false;
            }

            NetworkIdentity prefabIdentity = prefab.GetComponent<NetworkIdentity>();
            if (prefabIdentity == null || prefab.GetComponent<Grid>() == null)
            {
                Debug.LogError("New Grid Prefab must contain NetworkIdentity and Grid components.", prefab);
                prefab = null;
                return false;
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("Cannot create a grid because there is no NetworkManager.", this);
                prefab = null;
                return false;
            }

            foreach (GameObject registeredPrefab in manager.spawnPrefabs)
            {
                NetworkIdentity registeredIdentity = registeredPrefab != null
                    ? registeredPrefab.GetComponent<NetworkIdentity>()
                    : null;
                if (registeredIdentity != null &&
                    registeredIdentity.assetId == prefabIdentity.assetId)
                    return true;
            }

            Debug.LogError(
                $"New Grid Prefab '{prefab.name}' is not registered in NetworkManager's Spawnable Prefabs list.",
                this);
            prefab = null;
            return false;
        }

        public bool RequestCreateGrid(Vector3 worldOrigin, Quaternion rotation, uint tileId)
        {
            if (NetworkServer.active)
                return TryCreateGrid(worldOrigin, rotation, tileId, null);

            if (!NetworkClient.active)
                return TryCreateGrid(worldOrigin, rotation, tileId, null);

            if (netId == 0)
            {
                Debug.LogWarning("Cannot create a grid because the grid prefab registry is not spawned on this client.", this);
                return false;
            }

            CmdRequestCreateGrid(worldOrigin, rotation, tileId);
            return true;
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestCreateGrid(
            Vector3 worldOrigin,
            Quaternion rotation,
            uint tileId,
            NetworkConnectionToClient sender = null)
        {
            TryCreateGrid(worldOrigin, rotation, tileId, sender);
        }

        private bool TryCreateGrid(
            Vector3 worldOrigin,
            Quaternion rotation,
            uint tileId,
            NetworkConnectionToClient sender)
        {
            if (sender != null)
            {
                if (!allowClientGridCreation)
                {
                    Debug.LogWarning($"Rejected grid creation from connection {sender.connectionId}: client grid creation is disabled.", this);
                    return false;
                }

                if (sender.identity == null)
                {
                    Debug.LogWarning($"Rejected grid creation from connection {sender.connectionId}: no player object is assigned.", this);
                    return false;
                }

                Vector3 tileCenter = worldOrigin + rotation * Vector3.one * 0.5f;
                if ((sender.identity.transform.position - tileCenter).sqrMagnitude >
                    maxGridCreationDistance * maxGridCreationDistance)
                {
                    Debug.LogWarning($"Rejected grid creation outside interaction range: {worldOrigin}.", this);
                    return false;
                }
            }

            if (!TileRegistry.Instance.TryGetDefinition(tileId, out _))
            {
                Debug.LogWarning($"Rejected grid creation with unregistered tile ID {tileId}.", this);
                return false;
            }

            if (TryFindAdjacentGrid(worldOrigin, out Grid adjacentGrid, out Vector3Int adjacentCell))
            {
                adjacentCell.y = adjacentGrid.GetTileId(adjacentCell) == 0 ? 0 : 1;
                if (adjacentCell.y >= adjacentGrid.HeightLimit)
                    return false;

                NetworkGrid adjacentNetworkGrid = adjacentGrid.GetComponent<NetworkGrid>();
                if (adjacentNetworkGrid == null)
                {
                    Debug.LogWarning("Cannot place beside an existing grid because it has no NetworkGrid component.", adjacentGrid);
                    return false;
                }

                return adjacentNetworkGrid.TrySetTile(adjacentCell, tileId, sender);
            }

            if (IsWorldCellOccupied(worldOrigin, rotation))
            {
                Debug.LogWarning($"Rejected grid creation because the target cell is already occupied: {worldOrigin}.", this);
                return false;
            }

            if (!TryGetNewGridPrefab(out GameObject prefab))
                return false;

            GameObject gridObject = Instantiate(prefab, worldOrigin, rotation);
            Grid newGrid = gridObject.GetComponent<Grid>();
            if (newGrid == null)
            {
                Debug.LogError($"Registered grid prefab '{prefab.name}' does not contain a Grid component.", prefab);
                Destroy(gridObject);
                return false;
            }

            newGrid.SetTile(Vector3Int.zero, tileId);
            if (NetworkServer.active)
                NetworkServer.Spawn(gridObject);
            return true;
        }

        private static bool TryFindAdjacentGrid(
            Vector3 worldOrigin,
            out Grid adjacentGrid,
            out Vector3Int targetCell)
        {
            foreach (Grid candidate in FindObjectsByType<Grid>())
            {
                Vector3 localOrigin = candidate.transform.InverseTransformPoint(worldOrigin);
                int x = Mathf.RoundToInt(localOrigin.x);
                int z = Mathf.RoundToInt(localOrigin.z);
                if (Mathf.Abs(localOrigin.x - x) > 0.001f ||
                    Mathf.Abs(localOrigin.z - z) > 0.001f)
                    continue;

                Vector3Int cell = new Vector3Int(x, 0, z);
                if (HasFloor(candidate, cell) ||
                    HasFloor(candidate, cell + Vector3Int.left) ||
                    HasFloor(candidate, cell + Vector3Int.right) ||
                    HasFloor(candidate, cell + new Vector3Int(0, 0, -1)) ||
                    HasFloor(candidate, cell + new Vector3Int(0, 0, 1)))
                {
                    adjacentGrid = candidate;
                    targetCell = cell;
                    return true;
                }
            }

            adjacentGrid = null;
            targetCell = default;
            return false;
        }

        private static bool HasFloor(Grid candidate, Vector3Int cell)
        {
            return candidate.TryGetTileId(cell, out uint tileId) && tileId != 0;
        }

        private static bool IsWorldCellOccupied(Vector3 worldOrigin, Quaternion rotation)
        {
            Vector3 targetCenter = worldOrigin + rotation * Vector3.one * 0.5f;
            foreach (Grid existingGrid in FindObjectsByType<Grid>())
            {
                foreach (KeyValuePair<Vector3Int, uint> tile in existingGrid.GetStoredTiles())
                {
                    if (tile.Key.y != 0)
                        continue;

                    Vector3 tileCenter = existingGrid.CellToWorld(tile.Key) +
                                         existingGrid.transform.TransformVector(Vector3.one * 0.5f);
                    if ((tileCenter - targetCenter).sqrMagnitude < 0.0001f)
                        return true;
                }
            }

            return false;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            maxGridCreationDistance = Mathf.Max(0.1f, maxGridCreationDistance);
        }
    }
}
