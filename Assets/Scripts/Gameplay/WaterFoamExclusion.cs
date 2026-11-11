using UnityEngine;

namespace SailingSandbox.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class WaterFoamExclusion : MonoBehaviour
    {
        private const string MaskShaderName = "Hidden/Water Foam Exclusion";
        private const string MaskObjectName = "Water Foam Exclusion Mask";

        private GameObject _maskObject;

        private void OnEnable()
        {
            CreateMask();
        }

        private void OnDisable()
        {
            DestroyMask();
        }

        private void OnDestroy()
        {
            DestroyMask();
        }

        private void CreateMask()
        {
            if (_maskObject != null)
                return;

            MeshFilter sourceFilter = GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = GetComponent<MeshRenderer>();
            Shader maskShader = Shader.Find(MaskShaderName);

            if (sourceFilter.sharedMesh == null || sourceRenderer == null || maskShader == null)
                return;

            _maskObject = new GameObject(MaskObjectName);
            _maskObject.transform.SetParent(transform, false);
            _maskObject.hideFlags = HideFlags.HideAndDontSave;

            MeshFilter maskFilter = _maskObject.AddComponent<MeshFilter>();
            maskFilter.sharedMesh = sourceFilter.sharedMesh;

            MeshRenderer maskRenderer = _maskObject.AddComponent<MeshRenderer>();
            maskRenderer.sharedMaterial = new Material(maskShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            maskRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            maskRenderer.receiveShadows = false;
            maskRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            maskRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private void DestroyMask()
        {
            if (_maskObject == null)
                return;

            MeshRenderer maskRenderer = _maskObject.GetComponent<MeshRenderer>();
            if (maskRenderer != null && maskRenderer.sharedMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(maskRenderer.sharedMaterial);
                else
                    DestroyImmediate(maskRenderer.sharedMaterial);
            }

            if (Application.isPlaying)
                Destroy(_maskObject);
            else
                DestroyImmediate(_maskObject);

            _maskObject = null;
        }
    }
}
