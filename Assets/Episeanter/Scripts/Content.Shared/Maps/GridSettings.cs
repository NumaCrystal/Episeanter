using System;
using UnityEngine;

namespace Episeanter.Content.Shared.Maps
{
    [DisallowMultipleComponent]
    public sealed class GridSettings : MonoBehaviour
    {
        private static GridSettings instance;

        [SerializeField, Min(1)] private int heightLimit = 2;
        [SerializeField, Min(1)] private int chunkSize = 16;

        public static GridSettings Instance
        {
            get
            {
                if (instance == null)
                    throw new InvalidOperationException("No GridSettings singleton exists in the loaded scene.");

                return instance;
            }
        }

        public int HeightLimit => heightLimit;
        public int ChunkSize => chunkSize;

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

        private void OnValidate()
        {
            heightLimit = Mathf.Max(1, heightLimit);
            chunkSize = Mathf.Max(1, chunkSize);
        }
    }
}
