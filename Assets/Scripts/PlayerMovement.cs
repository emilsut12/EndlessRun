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
    public float jumpForce = 500f;
    public float horizontalLimit = 4f;

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

    private void Start()
    {
        if (rb == null) Debug.LogError("PlayerMovement: Rigidbody is not assigned!");
        if (groundCheck == null) Debug.LogError("PlayerMovement: GroundCheck is not assigned!");
        if (animator == null) Debug.LogWarning("PlayerMovement: Animator is missing!");
    }

    private void FixedUpdate()
    {
        if (!isAlive) return;

        // 1. Forward Movement
        float currentSpeed = baseSpeed * GameManager.Instance.gameSpeed;
        Vector3 forwardMove = transform.forward * currentSpeed * Time.fixedDeltaTime;

        // 2. Target X from Input Manager
        float targetX = InputManager.Instance.GetFinalX();

        // Ensure player cannot run infinitely to the sides off the map
        targetX = Mathf.Clamp(targetX, -horizontalLimit, horizontalLimit);

        // 1. Calculate the distance to the target lane
        float horizontalMove = targetX - rb.position.x;

        // 2. Set a deadzone
        float sideAnimValue = 0f;
        if (Mathf.Abs(horizontalMove) > 0.05f)
        {
            sideAnimValue = Mathf.Sign(horizontalMove);
        }

        Vector3 newPosition = rb.position + forwardMove;
        newPosition.x = targetX;

        rb.MovePosition(newPosition);

        // 3. Trigger animations
        if (animator != null)
        {
            animator.SetFloat("SideSpeed", sideAnimValue, 0.05f, Time.fixedDeltaTime);
        }
    }

    private void Update()
    {
        if (!isAlive) return;

        bool grounded = IsGrounded();

        if (animator != null)
        {
            animator.SetBool("IsGrounded", grounded);
        }

        // 3. Jumping Logic
        if (InputManager.Instance.GetJumpInput() && grounded && Time.time > lastJumpTime + jumpCooldown)
        {
            Jump();
        }

        // 4. Ghost Mechanic
        if (GameManager.Instance != null)
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            int obstacleLayer = LayerMask.NameToLayer("Obstacle");

            if (playerLayer != -1 && obstacleLayer != -1)
            {
                Physics.IgnoreLayerCollision(playerLayer, obstacleLayer, GameManager.Instance.isGhost);
            }
            else
            {
                Debug.LogError("PlayerMovement: Ensure your layers are exactly named 'Player' and 'Obstacle' in the top right of the Editor!");
            }
        }

        // Failsafe
        if (transform.position.y < -5f)
        {
            Die();
        }
    }

    private void Jump()
    {
        lastJumpTime = Time.time;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * jumpForce);

        if (animator != null)
        {
            animator.SetTrigger("Jump");
        }
    }

    private bool IsGrounded()
    {
        float checkRadius = 0.2f;
        return Physics.CheckSphere(groundCheck.position, checkRadius, groundLayer);
    }

    public void Die()
    {
        if (!isAlive) return;

        if (GameManager.Instance.isGhost) return;

        isAlive = false;

        if (animator != null)
        {
            animator.SetTrigger("Death");
        }

        Invoke(nameof(RestartGame), 2f);
    }

    private void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}