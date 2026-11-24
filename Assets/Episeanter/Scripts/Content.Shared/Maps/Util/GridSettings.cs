using System;
using UnityEngine;

namespace Episeanter.Content.Shared.Maps.Util
{
    [DisallowMultipleComponent]
    public sealed class GridSettings : MonoBehaviour
    {
        private static GridSettings instance;

        [SerializeField, Min(1)] private int heightLimit = 2;
        [SerializeField, Min(1)] private int chunkSize = 16;
        [SerializeField, Min(0)] private int gridY = 0;

        [SerializeField] private GameObject gridPrefab;

        public static GridSettings Instance
        {
            get
            {
                if (instance == null)
                    throw new InvalidOperationException("No GridSettings singleton exists in the loaded scene.");

                return instance;
            }
        }

        public GameObject GridPrefab => gridPrefab;
        public int HeightLimit => heightLimit;
        public int ChunkSize => chunkSize;
        public int GridY => gridY;

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
    }
}
