using System.Collections.Generic;
using UnityEngine;

public class GroundTile : MonoBehaviour
{
    GroundSpawner groundSpawner;
    [SerializeField] GameObject obstaclePrefab;
    [SerializeField] static int lastSpawn = -1;

    private void Start()
    {
        groundSpawner = GameObject.FindAnyObjectByType<GroundSpawner>();
    }

    private void OnTriggerExit(Collider other)
    {
        groundSpawner.spawnTile(true);
        Destroy(gameObject, 2);
    }

    private void Update()
    {
        
    }

    public void spawnObstacle()
    {
        // choose ranbdom point to spawn obstacle
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

        // spawn obstacle at position
        Instantiate(obstaclePrefab, spawnPoint.position, Quaternion.identity, transform);
    }
}
