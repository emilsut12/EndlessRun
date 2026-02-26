using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerMovement : MonoBehaviour
{
    bool alive = true;
    public float speed = 10;
    public float jumpForce = 500f;

    [SerializeField] Rigidbody rb;
    [SerializeField] LayerMask groundLayer;
    [SerializeField] Transform groundCheck;

    [SerializeField] Animator animator;

    private bool _isGrounded;

    private void Update()
    {
        if (!alive) return;

        if (groundCheck != null)
        {
            // Use a smaller radius (0.1f) to avoid overlapping with side walls or the player
            _isGrounded = Physics.CheckSphere(groundCheck.position, 0.1f, groundLayer);
        }
        else
        {
            _isGrounded = transform.position.y < 1.05f;
        }

        // You MUST send this to the animator every frame for the Falling transition to work
        if (animator != null)
        {
            animator.SetBool("isGrounded", _isGrounded);
        }

        if (transform.position.y < -5)
        {
            Die();
        }
    }

    private void FixedUpdate()
    {
        if (!alive) return;

        Vector3 forwardMove = transform.forward * speed * Time.fixedDeltaTime;
        float targetX = InputManager.Instance.GetTargetX();

        // 1. Calculate the distance to the target lane
        float horizontalMove = targetX - rb.position.x;

        // 2. Set a deadzone so animations stop when you are "close enough" to the target lane
        float sideAnimValue = 0f;
        if (Mathf.Abs(horizontalMove) > 0.05f)
        {
            // Use Mathf.Sign to get 1 for Right and -1 for Left
            sideAnimValue = Mathf.Sign(horizontalMove);
        }

        Vector3 newPosition = rb.position + forwardMove;
        newPosition.x = targetX;

        rb.MovePosition(newPosition);

        // 3. Trigger animations
        if (animator != null)
        {
            // SideSpeed will be 1 (Right), -1 (Left), or 0 (Straight)
            animator.SetFloat("SideSpeed", sideAnimValue, 0.05f, Time.fixedDeltaTime);
        }

        // Existing Jump Logic...
        if (InputManager.Instance.GetJumpInput())
        {
            if (_isGrounded)
            {
                rb.AddForce(Vector3.up * jumpForce);
                if (animator != null) animator.SetTrigger("Jump");
            }
            InputManager.Instance.ResetJump();
        }
    }

    public void Die()
    {
        // alive = false;
        // Invoke("Restart", 2);
    }

    void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}