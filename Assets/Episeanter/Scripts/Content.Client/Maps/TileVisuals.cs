using System;
using System.Collections.Generic;
using UnityEngine;

namespace Episeanter.Content.Client.Maps
{
    [DisallowMultipleComponent]
    public sealed class TileVisuals : MonoBehaviour
    {

        [SerializeField] private List<Entry> entries = new List<Entry>();
        [SerializeField] private Material damageLayerMaterial;
        private static TileVisuals instance;

        private Dictionary<uint, Entry> lookup;

        public static TileVisuals Instance
        {
            get
            {
                if (instance == null)
                    throw new InvalidOperationException("No TileVisuals singleton exists in the loaded scene.");

                return instance;
            }
        }

        public Material DamageLayerMaterial => damageLayerMaterial;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
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

        public bool TryGetMaterials(uint tileId, out Material top, out Material side)
        {
            EnsureLookup();
            if (lookup.TryGetValue(tileId, out Entry entry))
            {
                top = entry.Material;
                side = entry.SideMaterial;
                return true;
            }

            top = null;
            side = null;
            return false;
        }

        private void OnEnable()
        {
            lookup = null;
        }

        private void OnValidate()
        {
            lookup = null;
            EnsureLookup();
        }

        private void EnsureLookup()
        {
            if (lookup != null)
                return;

            lookup = new Dictionary<uint, Entry>(entries.Count);
            foreach (Entry entry in entries)
            {
                if (entry == null || entry.Id == 0 || entry.Material == null)
                    continue;

                if (!lookup.TryAdd(entry.Id, entry))
                    continue;

                ValidateAtlas(entry.Id, entry.Material);
                ValidateAtlas(entry.Id, entry.SideMaterial);
            }
        }

        private void ValidateAtlas(uint tileId, Material material)
        {
            if (material == null)
                return;

            Texture texture = material.HasProperty("_BaseMap")
                ? material.GetTexture("_BaseMap")
                : material.HasProperty("_MainTex")
                    ? material.GetTexture("_MainTex")
                    : null;

            if (texture != null && (texture.width < 4 || texture.height < 4
                || texture.width % 4 != 0 || texture.height % 4 != 0))
                Debug.LogError(
                    $"Material '{material.name}' for tile ID {tileId} must use a texture with dimensions divisible by 4 for its 4x4 dual-grid atlas.",
                    this);
        }

        [Serializable]
        private sealed class Entry
        {
            [SerializeField, Min(1)]
            private uint id;

            [SerializeField]
            private Material material;

            [SerializeField]
            private Material sideMaterial;

            public uint Id => id;
            public Material Material => material;
            public Material SideMaterial => sideMaterial != null ? sideMaterial : material;
        }
    }
}
