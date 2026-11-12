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
        [SerializeField] private LayerMask interactionLayers = Physics.DefaultRaycastLayers;
        [SerializeField, Min(1), Tooltip("Tile registry ID used for placement.")]
        private uint tileId = 1;

        private Camera cachedCamera;

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
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            if (mouse.leftButton.wasPressedThisFrame)
                Interact(place: false);
            else if (mouse.rightButton.wasPressedThisFrame)
                Interact(place: true);
        }

        private void Interact(bool place)
        {
            Ray ray = cachedCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, interactionLayers, QueryTriggerInteraction.Ignore))
                return;

            Grid grid = hit.collider.GetComponentInParent<Grid>();
            if (grid == null)
                return;

            Vector3 localNormal = grid.transform.localToWorldMatrix.transpose
                .MultiplyVector(hit.normal)
                .normalized;
            Vector3Int faceNormal = GetFaceNormal(localNormal);
            Vector3 localHitPoint = grid.transform.InverseTransformPoint(hit.point);
            Vector3 offset = (Vector3)faceNormal * (place ? CellOffset : -CellOffset);
            Vector3Int cell = Vector3Int.FloorToInt(localHitPoint + offset);

            if (place)
            {
                if (cell.y >= 0 && cell.y < grid.HeightLimit)
                    grid.SetTile(cell, tileId);
            }
            else
            {
                grid.RemoveTile(cell);
            }
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

        private void OnValidate()
        {
            maxDistance = Mathf.Max(0.01f, maxDistance);
        }
    }
}
