using System.Collections;
using UnityEngine;

namespace Episeanter.Content.Server.Maps
{
    public class GridTest : MonoBehaviour
    {
        private ServerGridSystem gridSystem;

        private IEnumerator Start()
        {
            gridSystem = GetComponent<ServerGridSystem>();

            if (gridSystem == null)
            {
                Debug.LogError("ServerGridSystem is not assigned!");
                yield break;
            }

            yield return new WaitUntil(() => gridSystem.Data != null);

            Vector3Int cellPosition = new Vector3Int(1, 0, 1);
            Vector3 worldPosition = transform.TransformPoint(cellPosition);

            for (int x = 0; x < 10; x++)
                for (int y = 0; y < 10; y++)
                    gridSystem.SetTile(new Vector3Int(x, 0, y), 1);
        }
    }
}
