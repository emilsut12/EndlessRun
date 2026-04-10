using UnityEngine;

public class Obstacle : MonoBehaviour
{
    private PlayerMovement playerMovement;

    void Start()
    {
        // Resolve through GameManager's cached player reference — avoids a full
        // scene scene scan (FindFirstObjectByType) on every obstacle spawn.
        if (GameManager.Instance != null && GameManager.Instance.playerTransform != null)
            playerMovement = GameManager.Instance.playerTransform.GetComponent<PlayerMovement>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.name == "Player" || collision.gameObject.CompareTag("Player"))
        {
            if (GameManager.Instance != null && !GameManager.Instance.isGhost)
            {
                // Lazy fallback in case Start() fired before GameManager was ready.
                if (playerMovement == null)
                    playerMovement = collision.gameObject.GetComponent<PlayerMovement>();

                if (playerMovement != null)
                    playerMovement.TakeHit();
            }
        }
    }
}