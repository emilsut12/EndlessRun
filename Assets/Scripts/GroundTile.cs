using System.Collections.Generic;
using UnityEngine;

public class GroundTile : MonoBehaviour
{
    GroundSpawner groundSpawner;
    [SerializeField] GameObject obstaclePrefab;
    [SerializeField] static int lastSpawn = -1;

    // --- NEW SCENERY SYSTEM ---
    [Header("Scenery Settings")]
    [Tooltip("Add prefabs named SideDecoration_xxx here")]
    [SerializeField] List<GameObject> sideDecorationPrefabs;

    [SerializeField] Transform leftSceneryPoint;
    [SerializeField] Transform rightSceneryPoint;

    private void Start()
    {
        groundSpawner = GameObject.FindAnyObjectByType<GroundSpawner>();
    }

    private void OnTriggerExit(Collider other)
    {
        // When spawning the next tile, we tell the spawner to handle items
        groundSpawner.spawnTile(true);
        Destroy(gameObject, 2);
    }

    public void SpawnScenery()
    {
        // Check how many prefabs are in the list so it works even if you add/remove them
        if (sideDecorationPrefabs.Count == 0) return;

        // Spawn a random one on the Right Side
        SpawnSingleScenery(rightSceneryPoint, false);

        // Spawn a random one on the Left Side
        SpawnSingleScenery(leftSceneryPoint, true);
    }

    void SpawnSingleScenery(Transform spawnPoint, bool isLeft)
    {
        int randomIndex = Random.Range(0, sideDecorationPrefabs.Count);
        GameObject prefabToSpawn = sideDecorationPrefabs[randomIndex];

        // Instantiate and parent to this tile so it gets destroyed automatically
        GameObject spawnedScenery = Instantiate(prefabToSpawn, spawnPoint.position, Quaternion.identity, transform);

        // If it's on the left, rotate it 180 degrees so it faces the path
        if (isLeft)
        {
            spawnedScenery.transform.Rotate(0, 180, 0);
        }
    }
    // --------------------------

    public void spawnObstacle()
    {
        int obstacleSpawnIndex = Random.Range(0, 3);
        if (obstacleSpawnIndex == lastSpawn)
        {
            if (obstacleSpawnIndex == 0 || obstacleSpawnIndex == 2)
            {
                obstacleSpawnIndex = 1;
            }
            else
            {
                obstacleSpawnIndex = (Random.value < 0.5f) ? 0 : 2;
            }
        }

        lastSpawn = obstacleSpawnIndex;
        obstacleSpawnIndex += 2;
        Transform spawnPoint = transform.GetChild(obstacleSpawnIndex).transform;

        Instantiate(obstaclePrefab, spawnPoint.position, Quaternion.identity, transform);
    }
}