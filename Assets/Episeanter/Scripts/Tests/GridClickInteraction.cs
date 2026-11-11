using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Episeanter.Grids
{
    [DisallowMultipleComponent]
    public sealed class GridClickInteraction : MonoBehaviour
    {
        private const float CellOffset = 0.001f;

        [SerializeField] private Camera targetCamera;
        [SerializeField, Min(0.01f)] private float maxDistance = 100f;
        [SerializeField, Min(0.01f)] private float damagePerHit = 10f;
        [SerializeField] private LayerMask interactionLayers = Physics.DefaultRaycastLayers;
        [SerializeField, Min(1), Tooltip("Tile registry IDs cycled through with the mouse wheel.")]
        private uint[] tileIdsForTesting = { 1, 2 };

        private Camera cachedCamera;
        private Grid fallbackGrid;
        private int selectedTileIndex;

        private void Awake()
        {
            cachedCamera = targetCamera != null ? targetCamera : GetComponent<Camera>();
            if (cachedCamera == null)
                cachedCamera = Camera.main;

            if (cachedCamera == null)
            {
                Debug.LogError("GridClickInteraction requires a camera on this object or a MainCamera in the scene.", this);
                enabled = false;
            }

            fallbackGrid = FindAnyObjectByType<Grid>();
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            HandleTileSwitch(mouse);

            if (mouse.rightButton.wasPressedThisFrame)
                TryPlace(mouse);
            else if (mouse.leftButton.wasPressedThisFrame)
                TryDamage(mouse);
        }

        private void HandleTileSwitch(Mouse mouse)
        {
            if (tileIdsForTesting == null || tileIdsForTesting.Length == 0)
                return;

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll == 0f)
                return;

            selectedTileIndex = (selectedTileIndex + (scroll > 0f ? 1 : -1) +
                                 tileIdsForTesting.Length) % tileIdsForTesting.Length;
        }

        private void TryPlace(Mouse mouse)
        {
            if (!TryRaycastGrid(mouse, out RaycastHit hit, out Grid grid))
            {
                TryPlaceOnEmptySpace(mouse);
                return;
            }

            if (!TryGetSelectedTileId(out uint tileId))
                return;

            Vector3 localNormal = GetLocalNormal(grid, hit.normal);
            Vector3Int faceNormal = GetFaceNormal(localNormal);
            Vector3 localHitPoint = grid.transform.InverseTransformPoint(hit.point);
            bool clickedSide = new Vector2(localNormal.x, localNormal.z).sqrMagnitude >
                               localNormal.y * localNormal.y;

            Vector3Int cell;
            if (clickedSide)
            {
                cell = Vector3Int.FloorToInt(
                    localHitPoint - (Vector3)faceNormal * CellOffset) + faceNormal;
            }
            else
            {
                cell = new Vector3Int(
                    Mathf.FloorToInt(localHitPoint.x),
                    0,
                    Mathf.FloorToInt(localHitPoint.z));
                if (!TryGetTileId(grid, cell, out uint floorTileId))
                    return;
                cell.y = floorTileId == 0 ? 0 : 1;
            }

            if (cell.y < 0 || cell.y >= grid.HeightLimit)
                return;

            SetTile(grid, cell, tileId);
        }

        private void TryPlaceOnEmptySpace(Mouse mouse)
        {
            if (!TryGetSelectedTileId(out uint tileId))
                return;

            if (fallbackGrid == null)
                fallbackGrid = FindAnyObjectByType<Grid>();
            if (fallbackGrid == null)
                return;

            Ray ray = cachedCamera.ScreenPointToRay(mouse.position.ReadValue());
            Plane placementPlane = new Plane(fallbackGrid.transform.up, fallbackGrid.transform.position);
            if (!placementPlane.Raycast(ray, out float distance) ||
                distance < 0f || distance > maxDistance)
                return;

            Vector3 localPoint = fallbackGrid.transform.InverseTransformPoint(ray.GetPoint(distance));
            Vector3Int targetCell = new Vector3Int(
                Mathf.FloorToInt(localPoint.x),
                0,
                Mathf.FloorToInt(localPoint.z));
            Vector3 gridOrigin = fallbackGrid.transform.TransformPoint(targetCell);

            if (TryFindAdjacentGrid(gridOrigin, out Grid adjacentGrid, out Vector3Int adjacentCell))
            {
                if (!TryGetTileId(adjacentGrid, adjacentCell, out uint floorTileId))
                    return;

                adjacentCell.y = floorTileId == 0 ? 0 : 1;
                if (adjacentCell.y < adjacentGrid.HeightLimit)
                    SetTile(adjacentGrid, adjacentCell, tileId);
                return;
            }

            GridPrefabRegistry.Instance.RequestCreateGrid(
                gridOrigin,
                fallbackGrid.transform.rotation,
                tileId);
        }

        private bool TryFindAdjacentGrid(
            Vector3 worldOrigin,
            out Grid adjacentGrid,
            out Vector3Int targetCell)
        {
            Grid[] grids = FindObjectsByType<Grid>();
            foreach (Grid candidate in grids)
            {
                Vector3 localOrigin = candidate.transform.InverseTransformPoint(worldOrigin);
                int x = Mathf.RoundToInt(localOrigin.x);
                int z = Mathf.RoundToInt(localOrigin.z);
                if (Mathf.Abs(localOrigin.x - x) > CellOffset ||
                    Mathf.Abs(localOrigin.z - z) > CellOffset)
                    continue;

                Vector3Int cell = new Vector3Int(x, 0, z);
                if (IsFloorOccupied(candidate, cell) ||
                    IsFloorOccupied(candidate, cell + Vector3Int.left) ||
                    IsFloorOccupied(candidate, cell + Vector3Int.right) ||
                    IsFloorOccupied(candidate, cell + new Vector3Int(0, 0, -1)) ||
                    IsFloorOccupied(candidate, cell + new Vector3Int(0, 0, 1)))
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

        private static bool IsFloorOccupied(Grid grid, Vector3Int cell)
        {
            return TryGetTileId(grid, cell, out uint tileId) && tileId != 0;
        }

        private void TryDamage(Mouse mouse)
        {
            if (!TryRaycastGrid(mouse, out RaycastHit hit, out Grid grid))
                return;

            Vector3 localNormal = GetLocalNormal(grid, hit.normal);
            Vector3Int faceNormal = GetFaceNormal(localNormal);
            Vector3 localHitPoint = grid.transform.InverseTransformPoint(hit.point);
            Vector3Int cell = Vector3Int.FloorToInt(
                localHitPoint - (Vector3)faceNormal * CellOffset);

            if (cell.y < 0 || cell.y >= grid.HeightLimit)
                return;

            Damageable damageable = grid.GetComponent<Damageable>();
            if (damageable == null)
            {
                Debug.LogWarning("Cannot damage the tile because its grid has no Damageable component.", grid);
                return;
            }

            damageable.DamageTile(cell, damagePerHit);
        }

        private static bool TryGetTileId(Grid grid, Vector3Int cell, out uint tileId)
        {
            if (NetworkClient.active && !NetworkServer.active)
            {
                GridVisual gridVisual = grid.GetComponent<GridVisual>();
                if (gridVisual == null)
                {
                    Debug.LogWarning("Cannot determine occupied grid layers because GridVisual is missing.", grid);
                    tileId = 0;
                    return false;
                }

                tileId = gridVisual.GetVisualTileId(cell);
                return true;
            }

            tileId = grid.GetTileId(cell);
            return true;
        }

        private static void SetTile(Grid grid, Vector3Int cell, uint tileId)
        {
            NetworkGrid networkGrid = grid.GetComponent<NetworkGrid>();
            if (NetworkClient.active)
            {
                if (networkGrid == null)
                {
                    Debug.LogWarning("Cannot edit the grid because it has no NetworkGrid component.", grid);
                    return;
                }

                networkGrid.RequestSetTile(cell, tileId);
            }
            else
            {
                grid.SetTile(cell, tileId);
            }
        }

        private bool TryRaycastGrid(Mouse mouse, out RaycastHit hit, out Grid grid)
        {
            hit = default;
            grid = null;
            Ray ray = cachedCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(
                    ray,
                    out hit,
                    maxDistance,
                    interactionLayers,
                    QueryTriggerInteraction.Ignore))
                return false;

            grid = hit.collider.GetComponentInParent<Grid>();
            if (grid == null)
                return false;

            return true;
        }

        private bool TryGetSelectedTileId(out uint tileId)
        {
            tileId = 0;
            if (tileIdsForTesting == null || tileIdsForTesting.Length == 0)
                return false;

            selectedTileIndex = Mathf.Clamp(selectedTileIndex, 0, tileIdsForTesting.Length - 1);
            tileId = tileIdsForTesting[selectedTileIndex];
            if (tileId != 0)
                return true;

            Debug.LogWarning("Tile ID 0 is reserved for empty cells.", this);
            return false;
        }

        private static Vector3 GetLocalNormal(Grid grid, Vector3 worldNormal)
        {
            return grid.transform.localToWorldMatrix.transpose.MultiplyVector(worldNormal).normalized;
        }

        private static Vector3Int GetFaceNormal(Vector3 normal)
        {
            Vector3 absoluteNormal = new Vector3(
                Mathf.Abs(normal.x),
                Mathf.Abs(normal.y),
                Mathf.Abs(normal.z));

            if (absoluteNormal.x >= absoluteNormal.y && absoluteNormal.x >= absoluteNormal.z)
                return new Vector3Int(normal.x >= 0f ? 1 : -1, 0, 0);
            if (absoluteNormal.y >= absoluteNormal.z)
                return new Vector3Int(0, normal.y >= 0f ? 1 : -1, 0);
            return new Vector3Int(0, 0, normal.z >= 0f ? 1 : -1);
        }

        private void OnGUI()
        {
            if (tileIdsForTesting == null || tileIdsForTesting.Length == 0)
                return;

            selectedTileIndex = Mathf.Clamp(selectedTileIndex, 0, tileIdsForTesting.Length - 1);
            GUI.Label(
                new Rect(10, 10, 500, 20),
                $"ПКМ: поставить (id={tileIdsForTesting[selectedTileIndex]}) | " +
                $"ЛКМ: нанести {damagePerHit} урона | Колесо: сменить id | пустота: новый грид/присоединение");
        }

        private void OnValidate()
        {
            maxDistance = Mathf.Max(0.01f, maxDistance);
            damagePerHit = Mathf.Max(0.01f, damagePerHit);
        }
    }
}
