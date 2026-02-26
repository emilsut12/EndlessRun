using UnityEngine;

public class GroundSpawner : MonoBehaviour
{
    [SerializeField] GameObject groundTile;
    Vector3 nextSpawnPoint;

    public void spawnTile(bool spawnItems)
    {
        GameObject temp = Instantiate(groundTile, nextSpawnPoint, Quaternion.identity);
        GroundTile tileScript = temp.GetComponent<GroundTile>();

        nextSpawnPoint = temp.transform.GetChild(1).transform.position;

        if (spawnItems)
        {
            tileScript.spawnObstacle();
            tileScript.SpawnScenery(); // This call now works again
        }
    }
    private void Start()
    {
        for (int i = 0; i < 15; i++)
        {
            spawnTile(i == 0 ? false : true);
        }
    }
}
