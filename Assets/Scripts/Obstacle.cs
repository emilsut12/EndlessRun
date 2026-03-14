using UnityEngine;

public class Obstacle : MonoBehaviour
{
    private PlayerMovement playerMovement;

    void Start()
    {
        playerMovement = Object.FindFirstObjectByType<PlayerMovement>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        // kill player
        if (collision.gameObject.name == "Player")
        {
            // Only kill if the player is not a ghost
            if (GameManager.Instance != null && !GameManager.Instance.isGhost)
            {
                playerMovement.Die();
            }
        }
    }
}