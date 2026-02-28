using UnityEngine;

/// <summary>
/// Follows a target at a fixed offset, locking the X-axis to stay centered.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform player;
    private Vector3 offset;

    private void Start()
    {
        if (player == null)
        {
            Debug.LogError("CameraFollow: Player Transform is not assigned in the Inspector!");
            return;
        }

        // Store initial distance
        offset = transform.position - player.position;
    }

    private void LateUpdate()
    {
        // prevent error spam if player is destroyed mid-game
        if (player == null) return;

        Vector3 targetPos = player.position + offset;
        targetPos.x = 0; // Lock horizontal movement
        transform.position = targetPos;
    }
}