using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections; // Needed for Coroutines

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

    [Header("Lives & Invulnerability Settings")]
    public float invulnerabilityDuration = 2f;
    public float fadeSpeed = 5f; // How fast the player pulses in and out
    public float targetOpacity = 0.5f; // Drops to ~50%
    private bool isInvulnerable = false;
    private Renderer[] playerRenderers;

    [Header("Physics & Layers")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;

    [Header("Animations")]
    [SerializeField] private Animator animator;

    [Header("Horizontal Smoothing")]
    [Tooltip("SmoothDamp time for lateral movement. Eliminates pose-tracking jitter without adding noticeable lag. 0.03-0.08 recommended.")]
    public float horizontalSmoothTime = 0.05f;

    private float _xVelocity = 0f;
    private float jumpCooldown = 0.5f;
    private float lastJumpTime = 0f;
    private bool grounded;

    [Header("Materials")]
    public Material opaqueMaterial;
    public Material fadeMaterial;

    private float defaultGravity;

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;

        Gizmos.color = Color.green;
        // Draw the exact same sphere we use in IsGrounded
        Gizmos.DrawSphere(groundCheck.position, 0.2f);
    }

    private void Start()
    {
        if (rb == null) Debug.LogError("PlayerMovement: Rigidbody is not assigned!");
        if (groundCheck == null) Debug.LogError("PlayerMovement: GroundCheck is not assigned!");
        if (animator == null) Debug.LogWarning("PlayerMovement: Animator is missing!");

        // Grab all renderers attached to the player (in case the model has multiple parts)
        playerRenderers = GetComponentsInChildren<Renderer>();

        // Store the default gravity from Unity's physics settings
        defaultGravity = Physics.gravity.y;
    }

    private void OnDestroy()
    {
        // Reset gravity so it doesn't stay permanently modified in the editor after exiting play mode
        Physics.gravity = new Vector3(0, defaultGravity, 0);
    }

    private void FixedUpdate()
    {
        if (!isAlive || GameManager.Instance.CurrentState != GameManager.GameState.Playing)
            return;

        grounded = IsGrounded();

        // 1. Calculate dynamic speed multiplier
        float speedMultiplier = GameManager.Instance.currentSpeed / GameManager.Instance.baseSpeed;

        // 2. Scale Gravity dynamically to keep jump arcs tight at higher speeds
        float scaledGravity = defaultGravity * (speedMultiplier * speedMultiplier);
        Physics.gravity = new Vector3(0, scaledGravity, 0);

        // 3. Forward Movement (scaled by dynamic speed and global gameSpeed)
        float moveSpeed = baseSpeed * speedMultiplier * GameManager.Instance.gameSpeed;
        Vector3 forwardMove = transform.forward * moveSpeed * Time.fixedDeltaTime;

        // Target X from Input Manager
        float targetX = InputManager.Instance.GetFinalX();

        // Clamp horizontal movements
        targetX = Mathf.Clamp(targetX, -InputManager.Instance.horizontalLimit, InputManager.Instance.horizontalLimit);

        // Smooth lateral movement with SmoothDamp so small pose oscillations don't
        // produce physical jitter. The velocity output is already smoothed and scaled,
        // making it a much better animation signal than Mathf.Sign(diff).
        float smoothedX = Mathf.SmoothDamp(
            rb.position.x, targetX, ref _xVelocity,
            horizontalSmoothTime, float.MaxValue, Time.fixedDeltaTime);

        Vector3 newPosition = rb.position + forwardMove;
        newPosition.x = smoothedX;

        rb.MovePosition(newPosition);

        // Drive SideSpeed proportionally from velocity — gives a continuous blend
        // instead of hard -1/0/+1 snapping that caused stop-start animation jitter.
        if (animator != null)
        {
            float normVelocity = Mathf.Clamp(_xVelocity / (InputManager.Instance.horizontalLimit * 2f), -1f, 1f);
            animator.SetFloat("SideSpeed", normVelocity, 0.05f, Time.fixedDeltaTime);
        }
    }

    private void Update()
    {
        if (!isAlive || GameManager.Instance == null || GameManager.Instance.CurrentState != GameManager.GameState.Playing)
            return;

        if (animator != null)
        {
            float speedMultiplier = GameManager.Instance.currentSpeed / GameManager.Instance.baseSpeed;
            animator.speed = speedMultiplier;
        }

        animator?.SetBool("isGrounded", grounded);

        if (InputManager.Instance.GetJumpInput() && grounded && Time.time > lastJumpTime + jumpCooldown)
        {
            Jump();
        }

        // Ghost & Invulnerability Mechanic
        if (GameManager.Instance != null)
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            int obstacleLayer = LayerMask.NameToLayer("Obstacle");

            if (playerLayer != -1 && obstacleLayer != -1)
            {
                // We now ignore collisions if the player is a Ghost OR currently Invulnerable
                Physics.IgnoreLayerCollision(playerLayer, obstacleLayer, GameManager.Instance.isGhost || isInvulnerable);
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

        // Scale jump force relative to the game's current speed multiplier
        float speedMultiplier = GameManager.Instance.currentSpeed / GameManager.Instance.baseSpeed;
        float scaledJumpForce = jumpForce * speedMultiplier;

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * scaledJumpForce, ForceMode.Impulse);

        if (animator != null)
        {
            animator.SetTrigger("Jump");
        }
    }

    private bool IsGrounded()
    {
        // A CheckSphere is much more reliable at high speeds than a thin Raycast line
        float checkRadius = 0.2f;
        int mask = groundLayer & ~LayerMask.GetMask("Player");

        // Checks if there are any ground colliders intersecting a small sphere at the groundCheck position
        return Physics.CheckSphere(groundCheck.position, checkRadius, mask, QueryTriggerInteraction.Ignore);
    }

    // New Take Hit Method
    public void TakeHit()
    {
        if (!isAlive || isInvulnerable || GameManager.Instance.isGhost) return;

        GameManager.Instance.LoseLife();

        if (GameManager.Instance.CurrentLives > 0)
        {
            // Player survives, become invulnerable
            StartCoroutine(InvulnerabilityRoutine());
        }
        else
        {
            // Out of lives
            Die();
        }
    }

    private IEnumerator InvulnerabilityRoutine()
    {
        isInvulnerable = true;
        float elapsed = 0f;

        // 1. Swap to the Fade material
        SetPlayerMaterial(fadeMaterial);

        while (elapsed < invulnerabilityDuration)
        {
            elapsed += Time.deltaTime;

            // PingPong moves between 0 and 1. Lerp pulses smoothly between 1f and targetOpacity
            float pingPongValue = Mathf.PingPong(Time.time * fadeSpeed, 1f);
            float currentAlpha = Mathf.Lerp(1f, targetOpacity, pingPongValue);

            SetPlayerOpacity(currentAlpha);

            yield return null;
        }

        // 2. Time is up, reset back to the solid Opaque material
        SetPlayerMaterial(opaqueMaterial);
        isInvulnerable = false;
    }

    private void SetPlayerMaterial(Material newMat)
    {
        if (playerRenderers == null || newMat == null) return;

        foreach (Renderer r in playerRenderers)
        {
            // This replaces the material on the mesh with the one we passed in
            r.material = newMat;
        }
    }

    private void SetPlayerOpacity(float alpha)
    {
        if (playerRenderers == null) return;

        foreach (Renderer r in playerRenderers)
        {
            // r.material gets the instance of the material currently on the mesh
            Material mat = r.material;

            if (mat.HasProperty("_Color"))
            {
                Color c = mat.color;
                c.a = alpha;
                mat.color = c;
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                Color c = mat.GetColor("_BaseColor");
                c.a = alpha;
                mat.SetColor("_BaseColor", c);
            }
        }
    }

    public void Die()
    {
        if (!isAlive) return;

        if (GameManager.Instance.isGhost) return;

        isAlive = false;

        // Disable the animator so the player stops their running animation
        if (animator != null)
        {
            animator.SetTrigger("Death");
        }

        // Notify the GameManager to display the UI
        GameManager.Instance.TriggerGameOver();
    }
}