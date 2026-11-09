using System;
using System.Collections.Generic;
using UnityEngine;

namespace Episeanter.Grids
{
    /// <summary>Maps persistent, non-zero tile IDs to definitions; keep IDs stable across saves and network peers.</summary>
    [DisallowMultipleComponent]
    public sealed class TileRegistry : MonoBehaviour
    {
        private static TileRegistry instance;

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<uint, TileDefinition> definitionsById;
        private Dictionary<TileDefinition, uint> idsByDefinition;

        public static TileRegistry Instance
        {
            get
            {
                if (instance == null)
                    throw new InvalidOperationException("No TileRegistry singleton exists in the loaded scene.");

                return instance;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogError($"Duplicate tile registry '{name}' found. Destroying the duplicate.", this);
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

        public uint GetId(TileDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            EnsureLookup();
            if (!idsByDefinition.TryGetValue(definition, out uint id))
                throw new KeyNotFoundException($"Tile '{definition.name}' is not registered in '{name}'.");

            return id;
        }

        public TileDefinition GetDefinition(uint id)
        {
            EnsureLookup();
            if (id == 0 || !definitionsById.TryGetValue(id, out TileDefinition definition))
                throw new KeyNotFoundException($"Tile ID {id} is not registered in '{name}'.");

            return definition;
        }

        public bool TryGetDefinition(uint id, out TileDefinition definition)
        {
            EnsureLookup();
            if (id == 0)
            {
                definition = null;
                return false;
            }

            return definitionsById.TryGetValue(id, out definition);
        }

        private void OnValidate()
        {
            InvalidateLookup();
        }

        private void InvalidateLookup()
        {
            definitionsById = null;
            idsByDefinition = null;
        }

        private void EnsureLookup()
        {
            if (definitionsById != null)
                return;
            if (entries == null)
                throw new InvalidOperationException($"Tile registry '{name}' has no entries list.");

            var newDefinitionsById = new Dictionary<uint, TileDefinition>(entries.Count);
            var newIdsByDefinition = new Dictionary<TileDefinition, uint>(entries.Count);
            foreach (Entry entry in entries)
            {
                if (entry == null || entry.Definition == null)
                    throw new InvalidOperationException($"Tile registry '{name}' contains an empty entry.");
                if (entry.Id == 0)
                    throw new InvalidOperationException($"Tile registry '{name}' uses reserved tile ID 0.");
                if (!newDefinitionsById.TryAdd(entry.Id, entry.Definition))
                    throw new InvalidOperationException($"Tile registry '{name}' contains duplicate tile ID {entry.Id}.");
                if (!newIdsByDefinition.TryAdd(entry.Definition, entry.Id))
                    throw new InvalidOperationException($"Tile '{entry.Definition.name}' appears more than once in registry '{name}'.");
                if (entry.Definition.Material == null)
                    throw new InvalidOperationException($"Tile '{entry.Definition.name}' in registry '{name}' has no material.");

                entry.Definition.ValidateTextureAtlases();
            }

            definitionsById = newDefinitionsById;
            idsByDefinition = newIdsByDefinition;
        }

        [Serializable]
        private sealed class Entry
        {
            [SerializeField, Min(1)] private uint id;
            [SerializeField] private TileDefinition definition;

            public uint Id => id;
            public TileDefinition Definition => definition;
        }
    }
}
