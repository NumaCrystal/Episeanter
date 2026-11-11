using UnityEngine;
using Episeanter.Grids;

public class GridTest : MonoBehaviour
{
    public Episeanter.Grids.Grid grid;

    private void Start()
    {
        this.grid = GetComponent<Episeanter.Grids.Grid>();

        if (grid == null)
        {
            Debug.LogError("Grid is not assigned!");
            return;
        }

        // Example usage of the grid
        Vector3Int cellPosition = new Vector3Int(1, 0, 1);
        Vector3 worldPosition = grid.CellToWorld(cellPosition);
        Debug.Log($"World position of cell {cellPosition}: {worldPosition}");
        for (int x = 0; x < 10; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                grid.SetTile(new Vector3Int(x, 0, y), 1);
                // if (x % 10 == 0) grid.SetTile(new Vector3Int(x, 1, y), 2);
            }
        }
    }
}