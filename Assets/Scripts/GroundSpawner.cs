using UnityEngine;

/// <summary>
/// Handles the continuous spawning of ground tiles to create the endless track.
/// </summary>
public class GroundSpawner : MonoBehaviour
{
    [Tooltip("The ground tile prefab to be spawned.")]
    [SerializeField] private GameObject groundTile;

    private Vector3 nextSpawnPoint;

    private void Start()
    {
        // Fetch the render distance from GameManager, defaulting to 15 if missing
        int startingTiles = GameManager.Instance.renderDistance;

        // Spawn the initial batch of tiles based on the setting
        for (int i = 0; i < startingTiles; i++)
        {
            // The very first tile shouldn't have obstacles
            spawnTile(i != 0);
        }
    }

    /// <summary>
    /// Spawns a single ground tile at the next available spawn point.
    /// </summary>
    /// <param name="spawnItems">If true, obstacles will be generated on this tile.</param>
    public void spawnTile(bool spawnItems)
    {
        GameObject temp = Instantiate(groundTile, nextSpawnPoint, Quaternion.identity);

        // Find the specific child object
        Transform spawnTransform = temp.transform.Find("NextSpawnPoint");
        nextSpawnPoint = spawnTransform.position;

        temp.GetComponent<GroundTile>().SpawnScenery();

        if (spawnItems)
        {
            temp.GetComponent<GroundTile>().spawnObstacle();
        }
    }
}