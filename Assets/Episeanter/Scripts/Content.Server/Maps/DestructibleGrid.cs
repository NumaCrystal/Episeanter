using System.Collections.Generic;
using Episeanter.Content.Shared.Events;
using Episeanter.Content.Shared.Maps.Events;
using Episeanter.Content.Shared.Maps.Grids;
using Mirror;
using UnityEngine;

namespace Episeanter.Content.Server.Maps
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ServerGridSystem))]
    public class DestructibleGrid : NetworkBehaviour
    {
        [Tooltip("Префаб грида для отколовшихся кусков. generateOnStart на нём должен быть выключен, DestructibleGrid должен присутствовать.")]
        [SerializeField] private ServerGridSystem gridPrefab;

        [Tooltip("Куски меньше этого размера просто удаляются, а не становятся гридом.")]
        [SerializeField, Min(1)] private int minPieceSize = 1;

        private static readonly Vector3Int[] Dirs =
        {
            Vector3Int.right, Vector3Int.left,
            Vector3Int.up, Vector3Int.down,
            Vector3Int.forward, Vector3Int.back
        };

        private ServerGridSystem gridSystem;
        private bool isSplitting;

        private readonly HashSet<Vector3Int> dirty = new HashSet<Vector3Int>();

        // scratch-буферы (переиспользуются, без аллокаций в горячем пути)
        private readonly Dictionary<Vector3Int, int> owner = new Dictionary<Vector3Int, int>();
        private readonly List<Queue<Vector3Int>> queues = new List<Queue<Vector3Int>>();
        private readonly List<Vector3Int> seeds = new List<Vector3Int>();
        private readonly List<int> parent = new List<int>();
        private readonly List<int> pending = new List<int>();
        private readonly List<int> size = new List<int>();

        public override void OnStartServer()
        {
            gridSystem = GetComponent<ServerGridSystem>();
            EventBus.Subscribe<RemoveTileEvent>(OnTileRemoved);
        }

        public override void OnStopServer()
        {
            EventBus.Unsubscribe<RemoveTileEvent>(OnTileRemoved);
            dirty.Clear();
        }

        private void OnTileRemoved(RemoveTileEvent e)
        {
            if (isSplitting || e.GridNetId != netId)
                return;

            dirty.Add(e.Position);
        }

        // Update, а не LateUpdate: расколы уйдут клиентам в том же кадре
        // в LateUpdate у ServerGridSystem.
        private void Update()
        {
            if (!NetworkServer.active || dirty.Count == 0)
                return;

            DestructBFS();
        }

        public void DestructBFS()
        {
            GridData data = gridSystem.Data;
            if (data == null)
            {
                dirty.Clear();
                return;
            }

            ResetScratch();

            // 1. Сиды: существующие соседи удалённых тайлов, каждый — своя волна.
            foreach (Vector3Int removed in dirty)
            {
                for (int i = 0; i < Dirs.Length; i++)
                {
                    Vector3Int n = removed + Dirs[i];
                    if (owner.ContainsKey(n) || !data.TryGetTileId(n, out _))
                        continue;

                    int w = parent.Count;
                    owner[n] = w;
                    parent.Add(w);
                    size.Add(1);
                    pending.Add(1);
                    seeds.Add(n);
                    GetQueue(w).Enqueue(n);
                }
            }
            dirty.Clear();

            int waves = parent.Count;
            if (waves < 2)
                return;

            // Соседние сиды заведомо связаны.
            for (int w = 0; w < waves; w++)
            {
                for (int i = 0; i < Dirs.Length; i++)
                {
                    if (owner.TryGetValue(seeds[w] + Dirs[i], out int o))
                        Union(w, o);
                }
            }

            // 2. Чередующийся BFS: по одному тайлу от каждой волны за раунд.
            // Идём, пока не останется ≤1 незавершённой компоненты.
            while (CountActiveRoots(waves) > 1)
            {
                for (int w = 0; w < waves; w++)
                {
                    Queue<Vector3Int> q = queues[w];
                    if (q.Count == 0)
                        continue;

                    Vector3Int cur = q.Dequeue();
                    pending[Find(w)]--;

                    for (int i = 0; i < Dirs.Length; i++)
                    {
                        Vector3Int n = cur + Dirs[i];
                        if (!data.TryGetTileId(n, out _))
                            continue;

                        if (owner.TryGetValue(n, out int o))
                        {
                            Union(w, o);
                            continue;
                        }

                        owner[n] = w;
                        int r = Find(w);
                        size[r]++;
                        pending[r]++;
                        q.Enqueue(n);
                    }
                }
            }

            // 3. Кто остаётся в исходном гриде: незавершённая компонента (она "остальной грид"),
            // а если все завершились — самая большая.
            int keep = -1, rootCount = 0, biggest = -1;
            for (int w = 0; w < waves; w++)
            {
                if (Find(w) != w)
                    continue;

                rootCount++;
                if (pending[w] > 0)
                    keep = w;
                if (biggest < 0 || size[w] > size[biggest])
                    biggest = w;
            }

            if (rootCount < 2)
                return;
            if (keep < 0)
                keep = biggest;

            // 4. Группируем тайлы отколовшихся кусков.
            var pieces = new Dictionary<int, List<Vector3Int>>();
            foreach (KeyValuePair<Vector3Int, int> kv in owner)
            {
                int r = Find(kv.Value);
                if (r == keep)
                    continue;

                if (!pieces.TryGetValue(r, out List<Vector3Int> list))
                    pieces[r] = list = new List<Vector3Int>(size[r]);
                list.Add(kv.Key);
            }

            isSplitting = true;
            try
            {
                foreach (List<Vector3Int> piece in pieces.Values)
                    SplitOff(data, piece);
            }
            finally
            {
                isSplitting = false;
            }
        }

        private void SplitOff(GridData data, List<Vector3Int> tiles)
        {
            if (tiles.Count < minPieceSize
                || !ServerGridSystem.CreateGrid(transform.position, transform.eulerAngles.y, out ServerGridSystem piece, false))
            {
                for (int i = 0; i < tiles.Count; i++)
                    data.RemoveTile(tiles[i]);
                return;
            }

            piece.transform.localScale = transform.localScale;

            // Тот же трансформ => те же координаты тайлов.
            GridData newData = piece.Data;
            for (int i = 0; i < tiles.Count; i++)
            {
                Vector3Int pos = tiles[i];
                newData.SetTile(pos, data.GetTileId(pos));
                if (data.TryGetHealth(pos, out float hp))
                    newData.SetHealth(pos, hp);
            }

            for (int i = 0; i < tiles.Count; i++)
                data.RemoveTile(tiles[i]);

            if (TryGetComponent(out Rigidbody src) && piece.TryGetComponent(out Rigidbody dst))
            {
                Vector3 com = piece.transform.TransformPoint(CenterOf(tiles));
                dst.linearVelocity = src.GetPointVelocity(com); // Unity 5/2022: velocity
                dst.angularVelocity = src.angularVelocity;
            }
        }

        private static Vector3 CenterOf(List<Vector3Int> tiles)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < tiles.Count; i++)
                sum += new Vector3(tiles[i].x + 0.5f, tiles[i].y + 0.5f, tiles[i].z + 0.5f);
            return sum / tiles.Count;
        }

        #region Union-Find / scratch
        private int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        private void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a == b)
                return;

            parent[a] = b;
            pending[b] += pending[a];
            size[b] += size[a];
        }

        private int CountActiveRoots(int waves)
        {
            int count = 0;
            for (int w = 0; w < waves; w++)
            {
                if (parent[w] == w && pending[w] > 0)
                    count++;
            }
            return count;
        }

        private Queue<Vector3Int> GetQueue(int w)
        {
            while (queues.Count <= w)
                queues.Add(new Queue<Vector3Int>());
            return queues[w];
        }

        private void ResetScratch()
        {
            owner.Clear();
            seeds.Clear();
            parent.Clear();
            pending.Clear();
            size.Clear();
            for (int i = 0; i < queues.Count; i++)
                queues[i].Clear();
        }
        #endregion
    }
}