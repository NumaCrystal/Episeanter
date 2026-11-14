using System.Collections.Generic;
using UnityEngine;

namespace SailingSandbox.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public class WaterTrailMap : MonoBehaviour
    {
        private const int MaxRipples = 48;

        private static readonly List<WaterTrailMap> ActiveMaps = new();
        private static readonly int RippleCountId = Shader.PropertyToID("_RippleCount");
        private static readonly int RippleDataId = Shader.PropertyToID("_RippleData");
        private static readonly int RippleSettingsId = Shader.PropertyToID("_RippleSettings");

        [SerializeField] private Vector2 worldSize = new(64f, 64f);
        [SerializeField] private Vector2 centerOffset;
        [SerializeField, Min(0.1f)] private float rippleLifetime = 3.5f;
        [SerializeField, Min(0.1f)] private float rippleSpeed = 2.5f;
        [SerializeField, Min(0.1f)] private float rippleFrequency = 12f;
        [SerializeField, Range(0f, 0.55f)] private float rippleStrength = 0.45f;

        private readonly List<Vector4> _ripples = new(MaxRipples);
        private readonly Vector4[] _rippleData = new Vector4[MaxRipples];
        private Renderer _renderer;
        private MaterialPropertyBlock _propertyBlock;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (_renderer == null)
                _renderer = GetComponent<Renderer>();
            if (_propertyBlock == null)
                _propertyBlock = new MaterialPropertyBlock();

            _ripples.Clear();
            ActiveMaps.Add(this);
            UpdateMaterialProperties();
        }

        private void LateUpdate()
        {
            RemoveExpiredRipples();
            UpdateMaterialProperties();
        }

        private void OnDisable()
        {
            ActiveMaps.Remove(this);
            _ripples.Clear();

            if (_renderer != null && _propertyBlock != null)
            {
                _renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetInt(RippleCountId, 0);
                _propertyBlock.SetVectorArray(RippleDataId, _rippleData);
                _renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        public static void AddRipple(Vector3 worldPosition, float strength)
        {
            for (int i = 0; i < ActiveMaps.Count; i++)
                ActiveMaps[i].AddRippleToMap(worldPosition, strength);
        }

        private void AddRippleToMap(Vector3 worldPosition, float strength)
        {
            Vector2 size = GetWorldSize();
            Vector2 center = GetWorldCenter();
            float margin = Mathf.Max(0f, rippleSpeed) * Mathf.Max(0f, rippleLifetime);

            if (worldPosition.x < center.x - size.x * 0.5f - margin
                || worldPosition.x > center.x + size.x * 0.5f + margin
                || worldPosition.z < center.y - size.y * 0.5f - margin
                || worldPosition.z > center.y + size.y * 0.5f + margin)
            {
                return;
            }

            if (_ripples.Count == MaxRipples)
                _ripples.RemoveAt(0);

            _ripples.Add(new Vector4(
                worldPosition.x,
                worldPosition.z,
                Time.time,
                Mathf.Clamp01(strength)));
        }

        private void RemoveExpiredRipples()
        {
            float lifetime = Mathf.Max(0.1f, rippleLifetime);
            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                if (Time.time - _ripples[i].z >= lifetime)
                    _ripples.RemoveAt(i);
            }
        }

        private void UpdateMaterialProperties()
        {
            for (int i = 0; i < MaxRipples; i++)
                _rippleData[i] = i < _ripples.Count ? _ripples[i] : Vector4.zero;

            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetInt(RippleCountId, _ripples.Count);
            _propertyBlock.SetVectorArray(RippleDataId, _rippleData);
            _propertyBlock.SetVector(
                RippleSettingsId,
                new Vector4(
                    Mathf.Max(0.1f, rippleSpeed),
                    Mathf.Max(0.1f, rippleFrequency),
                    Mathf.Max(0.1f, rippleLifetime),
                    Mathf.Clamp(rippleStrength, 0f, 0.55f)));
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        private Vector2 GetWorldSize()
        {
            return new Vector2(Mathf.Max(0.1f, worldSize.x), Mathf.Max(0.1f, worldSize.y));
        }

        private Vector2 GetWorldCenter()
        {
            return new Vector2(
                transform.position.x + centerOffset.x,
                transform.position.z + centerOffset.y);
        }
    }
}
