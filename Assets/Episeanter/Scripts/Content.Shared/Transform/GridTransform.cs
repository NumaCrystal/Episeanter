using Episeanter.Content.Shared.Events;
using UnityEngine;

namespace Episeanter.Content.Shared.Transforms
{
    [DisallowMultipleComponent]
    public class GridTransform : MonoBehaviour
    {
        [SerializeField] private Transform grid;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Quaternion localRotation = Quaternion.identity;

        private bool initialized;

        public Transform Grid => grid;

        public bool IsInWorld => grid == null;

        public Vector3 LocalPosition => localPosition;

        public Quaternion LocalRotation => localRotation;

        public Vector3 WorldPosition
            => grid != null ? grid.TransformPoint(localPosition) : localPosition;

        public Quaternion WorldRotation
            => grid != null ? grid.rotation * localRotation : localRotation;

        public Vector3Int Cell => Vector3Int.FloorToInt(localPosition);

        private void Awake()
            => Initialize();

        private void OnEnable()
            => Initialize();

        private void LateUpdate()
        {
            if (grid != null)
                Apply();
        }

        public void SetLocal(Vector3 position, Quaternion rotation)
        {
            if (!IsValid(position) || !IsValid(rotation))
                return;

            localPosition = position;
            localRotation = rotation;
            Apply();
            PublishChanged();
        }

        public void SetWorld(Vector3 position, Quaternion rotation)
        {
            if (!IsValid(position) || !IsValid(rotation))
                return;

            SetLocalFromWorld(position, rotation);
            Apply();
            PublishChanged();
        }

        public void SetGrid(Transform newGrid, bool keepWorld = true)
        {
            if (newGrid == grid)
                return;

            Vector3 worldPosition = WorldPosition;
            Quaternion worldRotation = WorldRotation;
            Transform oldGrid = grid;
            grid = newGrid;

            if (keepWorld)
                SetLocalFromWorld(worldPosition, worldRotation);

            Apply();
            EventBus.Publish(new GridTransformGridChangedEvent(this, oldGrid, newGrid));
            PublishChanged();
        }

        private void Initialize()
        {
            if (initialized)
                return;

            initialized = true;
            if (grid == null && localPosition == Vector3.zero && localRotation == Quaternion.identity)
            {
                localPosition = transform.position;
                localRotation = transform.rotation;
            }

            Apply();
        }

        private void SetLocalFromWorld(Vector3 position, Quaternion rotation)
        {
            if (grid != null)
            {
                localPosition = grid.InverseTransformPoint(position);
                localRotation = Quaternion.Inverse(grid.rotation) * rotation;
            }
            else
            {
                localPosition = position;
                localRotation = rotation;
            }
        }

        public void SetLocalPosition(Vector3 position)
            => SetLocal(position, localRotation);

        public void SetLocalRotation(Quaternion rotation)
            => SetLocal(localPosition, rotation);

        public void Translate(Vector3 localDelta)
            => SetLocal(localPosition + localDelta, localRotation);

        public void SetWorldPosition(Vector3 position)
            => SetWorld(position, WorldRotation);

        private void PublishChanged()
            => EventBus.Publish(new GridTransformChangedEvent(this));

        private void Apply()
            => transform.SetPositionAndRotation(WorldPosition, WorldRotation);

        private static bool IsValid(Vector3 value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsValid(Quaternion value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}