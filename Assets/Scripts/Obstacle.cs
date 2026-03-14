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
        // Hit the player
        if (collision.gameObject.name == "Player" || collision.gameObject.CompareTag("Player"))
        {
            // Only deal damage if the player is not a ghost
            if (GameManager.Instance != null && !GameManager.Instance.isGhost)
            {
                // Call TakeHit instead of Die!
                playerMovement.TakeHit();
            }
        }
    }
}