using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles obstacle/scenery spawning and triggers the generation of the next tile.
/// Works with GroundSpawner's pool — never destroys itself; instead clears its
/// spawned children one-per-frame then returns to the pool.
/// </summary>
public class GroundTile : MonoBehaviour
{
    private GroundSpawner groundSpawner;

    [Header("Obstacle Settings")]
    [SerializeField] private List<GameObject> centerObstaclePrefabs;
    [SerializeField] private List<GameObject> sideObstaclePrefabs;

    [Tooltip("Assign the spawn points for obstacles (0: Left, 1: Center, 2: Right).")]
    [SerializeField] private Transform[] obstacleSpawnPoints;

    private static int lastSpawnIndex = -1;
    private static int lastObstacleTypeIndex = -1;

    [Header("Scenery Settings")]
    [Tooltip("Add SideDecoration Prefabs Here")]
    [SerializeField] private List<GameObject> sideDecorationPrefabs;
    [SerializeField] private Transform leftSceneryPoint;
    [SerializeField] private Transform rightSceneryPoint;

    private static int lastDecorationIndex = -1;

    // Tracks runtime-spawned children (obstacle + scenery) so they can be
    // destroyed one-per-frame during recycling without a single-frame GC spike.
    private readonly List<GameObject> _spawnedContent = new List<GameObject>(4);

    private void Start()
    {
        // groundSpawner is set via Init() before Start() fires.
        // This fallback only triggers for tiles that somehow miss Init(). 
        if (groundSpawner == null)
            groundSpawner = Object.FindFirstObjectByType<GroundSpawner>();
    }

    /// <summary>Called by GroundSpawner when this tile is pulled from the pool.</summary>
    public void Init(GroundSpawner spawner)
    {
        groundSpawner = spawner;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            groundSpawner.spawnTile(true);
            StartCoroutine(RecycleDeferred());
        }
    }

    /// <summary>
    /// Waits the same 2 s as the old Destroy delay, then destroys each spawned
    /// child on its own frame so GC pressure is spread rather than spiked.
    /// Finally returns the tile itself to the pool (no Destroy — just SetActive(false)).
    /// </summary>
    private IEnumerator RecycleDeferred()
    {
        yield return new WaitForSeconds(2f);

        for (int i = _spawnedContent.Count - 1; i >= 0; i--)
        {
            if (_spawnedContent[i] != null)
                Destroy(_spawnedContent[i]);
            yield return null; // one child destroyed per frame
        }
        _spawnedContent.Clear();

        groundSpawner.ReturnToPool(this);
    }

    public void SpawnScenery()
    {
        int decorationIndex = Random.Range(0, sideDecorationPrefabs.Count);

        if (sideDecorationPrefabs.Count > 1 && decorationIndex == lastDecorationIndex)
            decorationIndex = (decorationIndex + 1) % sideDecorationPrefabs.Count;

        lastDecorationIndex = decorationIndex;
        GameObject prefabToSpawn = sideDecorationPrefabs[decorationIndex];

        SpawnSingleScenery(rightSceneryPoint, prefabToSpawn, false);
        SpawnSingleScenery(leftSceneryPoint, prefabToSpawn, true);
    }

    private void SpawnSingleScenery(Transform spawnPoint, GameObject prefabToSpawn, bool isLeft)
    {
        GameObject spawned = Instantiate(prefabToSpawn, spawnPoint.position, Quaternion.identity, transform);
        if (isLeft)
            spawned.transform.Rotate(0, 180, 0);
        _spawnedContent.Add(spawned);
    }

    public void spawnObstacle()
    {
        int laneIndex = Random.Range(0, 3);

        if (laneIndex == lastSpawnIndex)
        {
            if (laneIndex == 0 || laneIndex == 2) laneIndex = 1;
            else laneIndex = (Random.value < 0.5f) ? 0 : 2;
        }
        lastSpawnIndex = laneIndex;

        List<GameObject> currentPool = (laneIndex == 1) ? centerObstaclePrefabs : sideObstaclePrefabs;

        int obstacleTypeIndex = Random.Range(0, currentPool.Count);

        if (currentPool.Count > 1 && obstacleTypeIndex == lastObstacleTypeIndex)
            obstacleTypeIndex = (obstacleTypeIndex + 1) % currentPool.Count;

        lastObstacleTypeIndex = obstacleTypeIndex;

        GameObject prefabToSpawn = currentPool[obstacleTypeIndex];
        Transform spawnPoint = obstacleSpawnPoints[laneIndex];

        GameObject spawnedObstacle = Instantiate(prefabToSpawn, spawnPoint.position, Quaternion.identity, transform);
        if (laneIndex == 0)
            spawnedObstacle.transform.Rotate(0, 180, 0);
        _spawnedContent.Add(spawnedObstacle);
    }
}