using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles the continuous spawning of ground tiles to create the endless track.
/// Pre-allocates a pool of tile instances at start so no mid-game Instantiate or
/// Destroy of the heavy tile GameObject (mesh + colliders) ever occurs.
/// </summary>
public class GroundSpawner : MonoBehaviour
{
    [Tooltip("The ground tile prefab to be spawned.")]
    [SerializeField] private GameObject groundTile;

    [Tooltip("Extra tiles kept in reserve beyond renderDistance. " +
             "Covers the 2-second recycle delay — increase if pool-exhausted warnings appear.")]
    [SerializeField] private int poolBuffer = 6;

    private Vector3 nextSpawnPoint;
    private Queue<GroundTile> _pool = new Queue<GroundTile>();

    private void Start()
    {
        int startingTiles = GameManager.Instance.renderDistance;
        int poolSize = startingTiles + poolBuffer;

        // Pre-instantiate ALL tiles up front — zero mid-game Instantiate overhead for the
        // tile itself (heaviest object: full mesh + colliders + trigger).
        for (int i = 0; i < poolSize; i++)
        {
            GroundTile tile = CreateTile();
            tile.gameObject.SetActive(false);
            _pool.Enqueue(tile);
        }

        for (int i = 0; i < startingTiles; i++)
        {
            spawnTile(!(i < 3));
        }
    }

    private GroundTile CreateTile()
    {
        // Park far below the scene so it doesn't flash into view before being placed.
        GameObject obj = Instantiate(groundTile, new Vector3(0f, -1000f, 0f), Quaternion.identity);
        GroundTile tile = obj.GetComponent<GroundTile>();
        tile.Init(this);
        return tile;
    }

    /// <summary>
    /// Pulls a tile from the pool, places it at the next spawn point, and spawns its content.
    /// </summary>
    public void spawnTile(bool spawnItems)
    {
        GroundTile tile;

        if (_pool.Count > 0)
        {
            tile = _pool.Dequeue();
            tile.transform.position = nextSpawnPoint;
            tile.gameObject.SetActive(true);
        }
        else
        {
            // Safety fallback — should not normally fire; increase poolBuffer if it does.
            Debug.LogWarning("GroundSpawner: Pool exhausted — creating extra tile. Consider raising Pool Buffer.");
            tile = CreateTile();
            tile.transform.position = nextSpawnPoint;
        }

        Transform spawnTransform = tile.transform.Find("NextSpawnPoint");
        nextSpawnPoint = spawnTransform.position;

        tile.SpawnScenery();
        if (spawnItems)
            tile.spawnObstacle();
    }

    /// <summary>Called by GroundTile once it has cleared its spawned children.</summary>
    public void ReturnToPool(GroundTile tile)
    {
        tile.gameObject.SetActive(false);
        _pool.Enqueue(tile);
    }
}