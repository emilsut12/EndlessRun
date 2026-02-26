using System.Collections.Generic;
using UnityEngine;

public class GroundTile : MonoBehaviour
{
    GroundSpawner groundSpawner;

    [Header("Obstacle Settings")]
    [SerializeField] List<GameObject> centerObstaclePrefabs;
    [SerializeField] List<GameObject> sideObstaclePrefabs;
    [SerializeField] static int lastSpawnIndex = -1;
    [SerializeField] static int lastObstacleTypeIndex = -1;

    [Header("Scenery Settings")]
    [Tooltip("Add prefabs named SideDecoration_xxx here")]
    [SerializeField] List<GameObject> sideDecorationPrefabs;
    [SerializeField] Transform leftSceneryPoint;
    [SerializeField] Transform rightSceneryPoint;
    [SerializeField] static int lastDecorationIndex = -1;

    private void Start()
    {
        groundSpawner = GameObject.FindAnyObjectByType<GroundSpawner>();
    }

    private void OnTriggerExit(Collider other)
    {
        // Triggers the spawner to create the next tile
        groundSpawner.spawnTile(true);
        Destroy(gameObject, 2);
    }

    // --- SCENERY LOGIC (Fixes the CS1061 error) ---
    public void SpawnScenery()
    {
        if (sideDecorationPrefabs.Count == 0) return;

        int decorationIndex = Random.Range(0, sideDecorationPrefabs.Count);

        if (sideDecorationPrefabs.Count > 1 && decorationIndex == lastDecorationIndex)
        {
            decorationIndex = (decorationIndex + 1) % sideDecorationPrefabs.Count;
        }

        lastDecorationIndex = decorationIndex;
        GameObject prefabToSpawn = sideDecorationPrefabs[decorationIndex];

        SpawnSingleScenery(rightSceneryPoint, prefabToSpawn, false);
        SpawnSingleScenery(leftSceneryPoint, prefabToSpawn, true);
    }

    void SpawnSingleScenery(Transform spawnPoint, GameObject prefabToSpawn, bool isLeft)
    {
        GameObject spawnedScenery = Instantiate(prefabToSpawn, spawnPoint.position, Quaternion.identity, transform);
        if (isLeft)
        {
            spawnedScenery.transform.Rotate(0, 180, 0);
        }
    }

    // --- UPDATED OBSTACLE LOGIC ---
    public void spawnObstacle()
    {
        // 1. Choose a random lane (0: Left, 1: Center, 2: Right)
        int laneIndex = Random.Range(0, 3);

        // Prevent spawning in the same lane twice in a row
        if (laneIndex == lastSpawnIndex)
        {
            if (laneIndex == 0 || laneIndex == 2) laneIndex = 1;
            else laneIndex = (Random.value < 0.5f) ? 0 : 2;
        }
        lastSpawnIndex = laneIndex;

        // 2. Select the pool based on lane
        List<GameObject> currentPool = (laneIndex == 1) ? centerObstaclePrefabs : sideObstaclePrefabs;
        if (currentPool.Count == 0) return;

        // 3. Pick a random obstacle and prevent immediate repetition
        int obstacleTypeIndex = Random.Range(0, currentPool.Count);
        if (currentPool.Count > 1 && obstacleTypeIndex == lastObstacleTypeIndex)
        {
            obstacleTypeIndex = (obstacleTypeIndex + 1) % currentPool.Count;
        }
        lastObstacleTypeIndex = obstacleTypeIndex;

        GameObject prefabToSpawn = currentPool[obstacleTypeIndex];

        // 4. Map lane (0,1,2) to child transform index (2,3,4)
        Transform spawnPoint = transform.GetChild(laneIndex + 2).transform;

        // 5. Instantiate and rotate if on the left lane
        GameObject spawnedObstacle = Instantiate(prefabToSpawn, spawnPoint.position, Quaternion.identity, transform);

        if (laneIndex == 0)
        {
            spawnedObstacle.transform.Rotate(0, 180, 0);
        }
    }
}