using Episeanter.Content.Shared.Maps;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Episeanter.Content.Client.Maps
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
        private ClientGridSystem fallbackSystem;
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

            fallbackSystem = FindAnyObjectByType<ClientGridSystem>();
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
            if (!TryRaycastGrid(mouse, out RaycastHit hit, out ClientGridSystem system))
            {
                TryPlaceOnEmptySpace(mouse);
                return;
            }

            if (!TryGetSelectedTileId(out uint tileId))
                return;

            GridData data = system.Data;
            if (data == null)
                return;

            Transform gridTransform = system.transform;
            Vector3 localNormal = GetLocalNormal(gridTransform, hit.normal);
            Vector3Int faceNormal = GetFaceNormal(localNormal);
            Vector3 localHitPoint = gridTransform.InverseTransformPoint(hit.point);
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
                cell.y = data.GetTileId(cell) == 0 ? 0 : 1;
            }

            if (!data.InBounds(cell))
                return;

            system.RequestPlace(cell, tileId);
        }

        /// <summary>
        /// Places a tile on the grid plane where nothing was hit. The grid is one unbounded world,
        /// so a cell next to existing tiles and a cell far away are handled the same way.
        /// </summary>
        private void TryPlaceOnEmptySpace(Mouse mouse)
        {
            if (!TryGetSelectedTileId(out uint tileId))
                return;

            if (fallbackSystem == null)
                fallbackSystem = FindAnyObjectByType<ClientGridSystem>();
            if (fallbackSystem == null || fallbackSystem.Data == null)
                return;

            GridData data = fallbackSystem.Data;
            Transform gridTransform = fallbackSystem.transform;
            Ray ray = cachedCamera.ScreenPointToRay(mouse.position.ReadValue());
            Plane placementPlane = new Plane(gridTransform.up, gridTransform.position);
            if (!placementPlane.Raycast(ray, out float distance) ||
                distance < 0f || distance > maxDistance)
                return;

            Vector3 localPoint = gridTransform.InverseTransformPoint(ray.GetPoint(distance));
            Vector3Int cell = new Vector3Int(
                Mathf.FloorToInt(localPoint.x),
                0,
                Mathf.FloorToInt(localPoint.z));
            cell.y = data.GetTileId(cell) == 0 ? 0 : 1;

            if (!data.InBounds(cell))
                return;

            fallbackSystem.RequestPlace(cell, tileId);
        }

        private void TryDamage(Mouse mouse)
        {
            if (!TryRaycastGrid(mouse, out RaycastHit hit, out ClientGridSystem system))
                return;

            GridData data = system.Data;
            if (data == null)
                return;

            Transform gridTransform = system.transform;
            Vector3 localNormal = GetLocalNormal(gridTransform, hit.normal);
            Vector3Int faceNormal = GetFaceNormal(localNormal);
            Vector3 localHitPoint = gridTransform.InverseTransformPoint(hit.point);
            Vector3Int cell = Vector3Int.FloorToInt(
                localHitPoint - (Vector3)faceNormal * CellOffset);

            if (!data.InBounds(cell))
                return;

            system.RequestDamage(cell, damagePerHit);
        }

        private bool TryRaycastGrid(Mouse mouse, out RaycastHit hit, out ClientGridSystem system)
        {
            hit = default;
            system = null;
            Ray ray = cachedCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(
                    ray,
                    out hit,
                    maxDistance,
                    interactionLayers,
                    QueryTriggerInteraction.Ignore))
                return false;

            system = hit.collider.GetComponentInParent<ClientGridSystem>();
            if (system == null)
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

        private static Vector3 GetLocalNormal(Transform gridTransform, Vector3 worldNormal)
        {
            return gridTransform.localToWorldMatrix.transpose.MultiplyVector(worldNormal).normalized;
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
                $"ЛКМ: нанести {damagePerHit} урона | Колесо: сменить id | пустота: поставить тайл на плоскость грида");
        }

        private void OnValidate()
        {
            maxDistance = Mathf.Max(0.01f, maxDistance);
            damagePerHit = Mathf.Max(0.01f, damagePerHit);
        }
    }
}
