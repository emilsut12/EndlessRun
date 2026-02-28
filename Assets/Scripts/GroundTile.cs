using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles obstacle/scenery spawning and triggers the generation of the next tile.
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

    private void Start()
    {
        groundSpawner = Object.FindFirstObjectByType<GroundSpawner>();
    }

    private void OnTriggerExit(Collider other)
    {
        // Only spawn the next tile if the player is the one exiting the trigger
        if (other.CompareTag("Player"))
        {
            groundSpawner.spawnTile(true);
            Destroy(gameObject, 2);
        }
    }

    public void SpawnScenery()
    {
        int decorationIndex = Random.Range(0, sideDecorationPrefabs.Count);

        // Prevent back-to-back identical scenery
        if (sideDecorationPrefabs.Count > 1 && decorationIndex == lastDecorationIndex)
        {
            decorationIndex = (decorationIndex + 1) % sideDecorationPrefabs.Count;
        }

        lastDecorationIndex = decorationIndex;
        GameObject prefabToSpawn = sideDecorationPrefabs[decorationIndex];

        SpawnSingleScenery(rightSceneryPoint, prefabToSpawn, false);
        SpawnSingleScenery(leftSceneryPoint, prefabToSpawn, true);
    }

    private void SpawnSingleScenery(Transform spawnPoint, GameObject prefabToSpawn, bool isLeft)
    {
        GameObject spawnedScenery = Instantiate(prefabToSpawn, spawnPoint.position, Quaternion.identity, transform);

        // Face scenery inwards depending on which side it spawns
        if (isLeft)
        {
            spawnedScenery.transform.Rotate(0, 180, 0);
        }
    }

    // OBSTACLE LOGIC
    public void spawnObstacle()
    {
        int laneIndex = Random.Range(0, 3);

        // Prevent spawning in the exact same lane twice in a row
        if (laneIndex == lastSpawnIndex)
        {
            if (laneIndex == 0 || laneIndex == 2) laneIndex = 1;
            else laneIndex = (Random.value < 0.5f) ? 0 : 2;
        }
        lastSpawnIndex = laneIndex;

        List<GameObject> currentPool = (laneIndex == 1) ? centerObstaclePrefabs : sideObstaclePrefabs;

        int obstacleTypeIndex = Random.Range(0, currentPool.Count);

        // Prevent back-to-back identical obstacles
        if (currentPool.Count > 1 && obstacleTypeIndex == lastObstacleTypeIndex)
        {
            obstacleTypeIndex = (obstacleTypeIndex + 1) % currentPool.Count;
        }
        lastObstacleTypeIndex = obstacleTypeIndex;

        GameObject prefabToSpawn = currentPool[obstacleTypeIndex];

        // Explicit array lookup
        Transform spawnPoint = obstacleSpawnPoints[laneIndex];

        GameObject spawnedObstacle = Instantiate(prefabToSpawn, spawnPoint.position, Quaternion.identity, transform);

        // Rotate obstacle if spawned on the left lane
        if (laneIndex == 0)
        {
            spawnedObstacle.transform.Rotate(0, 180, 0);
        }
    }
}