using System;
using System.Collections.Generic;
using UnityEngine;

namespace Episeanter.Content.Shared.Maps
{
    [DisallowMultipleComponent]
    public sealed class TileRegistry : MonoBehaviour
    {
        private static TileRegistry instance;

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<uint, TileDefinition> definitionsById;
        private Dictionary<TileDefinition, uint> idsByDefinition;
        private uint hash;

        public static TileRegistry Instance
        {
            get
            {
                if (instance == null)
                    throw new InvalidOperationException("No TileRegistry singleton exists in the loaded scene.");

                return instance;
            }
        }

        public uint Hash
        {
            get
            {
                EnsureLookup();
                return hash;
            }
        }

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
            }

            definitionsById = newDefinitionsById;
            idsByDefinition = newIdsByDefinition;
            hash = ComputeHash(newDefinitionsById);
        }

        private static uint ComputeHash(Dictionary<uint, TileDefinition> definitions)
        {
            var ids = new List<uint>(definitions.Keys);
            ids.Sort();

            uint h = 2166136261u;
            foreach (uint id in ids)
            {
                h = Mix(h, id);
                string definitionName = definitions[id].name;
                for (int i = 0; i < definitionName.Length; i++)
                    h = Mix(h, definitionName[i]);
            }

            return h;
        }

        private static uint Mix(uint h, uint value)
        {
            for (int i = 0; i < 4; i++)
            {
                h ^= value & 0xFFu;
                h *= 16777619u;
                value >>= 8;
            }

            return h;
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
