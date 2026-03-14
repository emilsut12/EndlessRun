using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles player forward movement, snappy horizontal shifting, jumping, and ghost states.
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    private bool isAlive = true;

    [Header("Movement Settings")]
    public float baseSpeed = 10f;
    public float jumpForce = 6f;

    [Tooltip("Multiplier to adjust how strongly the SideSpeed animation reacts to input.")]
    public float animationSensitivity = 10f;

    [Header("Physics & Layers")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;

    [Header("Animations")]
    [SerializeField] private Animator animator;

    private float jumpCooldown = 0.5f;
    private float lastJumpTime = 0f;
    private bool grounded;


    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(groundCheck.position, 0.1f);
    }

    private void Start()
    {
        if (rb == null) Debug.LogError("PlayerMovement: Rigidbody is not assigned!");
        if (groundCheck == null) Debug.LogError("PlayerMovement: GroundCheck is not assigned!");
        if (animator == null) Debug.LogWarning("PlayerMovement: Animator is missing!");
    }

    private void FixedUpdate()
    {
        if (!isAlive || GameManager.Instance.CurrentState != GameManager.GameState.Playing)
            return;

        grounded = IsGrounded();

        // Forward Movement
        float currentSpeed = baseSpeed * GameManager.Instance.gameSpeed;
        Vector3 forwardMove = transform.forward * currentSpeed * Time.fixedDeltaTime;

        // Target X from Input Manager
        float targetX = InputManager.Instance.GetFinalX();

        // Clamp horizontal movements
        targetX = Mathf.Clamp(targetX, -InputManager.Instance.horizontalLimit, InputManager.Instance.horizontalLimit);

        // Calculate the distance to the target lane
        float horizontalMove = targetX - rb.position.x;

        // Set a deadzone
        float sideAnimValue = 0f;
        if (Mathf.Abs(horizontalMove) > 0.05f)
        {
            sideAnimValue = Mathf.Sign(horizontalMove);
        }

        Vector3 newPosition = rb.position + forwardMove;
        newPosition.x = targetX;

        rb.MovePosition(newPosition);

        // Trigger animations
        if (animator != null)
        {
            animator.SetFloat("SideSpeed", sideAnimValue, 0.05f, Time.fixedDeltaTime);
        }
    }

    private void Update()
    {
        if (!isAlive || GameManager.Instance.CurrentState != GameManager.GameState.Playing)
            return;

        animator?.SetBool("isGrounded", grounded);

        if (InputManager.Instance.GetJumpInput() && grounded && Time.time > lastJumpTime + jumpCooldown)
        {
            Jump();
        }

        // Ghost Mechanic
        if (GameManager.Instance != null)
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            int obstacleLayer = LayerMask.NameToLayer("Obstacle");

            if (playerLayer != -1 && obstacleLayer != -1)
            {
                Physics.IgnoreLayerCollision(playerLayer, obstacleLayer, GameManager.Instance.isGhost);
            }
        }

        if (transform.position.y < -5f)
        {
            Die();
        }
    }

    private void Jump()
    {
        lastJumpTime = Time.time;

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);

        if (animator != null)
        {
            animator.SetTrigger("Jump");
        }
    }

    private bool IsGrounded()
    {
        float rayDistance = 0.1f;
        int mask = groundLayer & ~LayerMask.GetMask("Player");
        return Physics.Raycast(groundCheck.position, Vector3.down, rayDistance, mask, QueryTriggerInteraction.Ignore);
    }

    public void Die()
    {
        // Add a check to prevent triggering death multiple times
        // Assuming you have 'private bool isAlive = true;' at the top of your script
        if (!isAlive) return;

        if (GameManager.Instance.isGhost) return;

        isAlive = false;

        // Notify the GameManager to display the UI
        GameManager.Instance.TriggerGameOver();

        // 1. Disable the animator so the player stops their running animation
        if (animator != null)
        {
            animator.enabled = false;
        }

        // 2. Tumble backwards (Ragdoll/Physics effect)
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.None; // Unfreeze rotations
            rb.AddForce(Vector3.back * 5f, ForceMode.Impulse);
            rb.AddTorque(new Vector3(Random.Range(-10f, 10f), 0, Random.Range(-10f, 10f)), ForceMode.Impulse);
        }
    }
}