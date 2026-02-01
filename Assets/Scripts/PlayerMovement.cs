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

    private bool _isGrounded;

    private void Update()
    {
        if (!alive) return;

        // Ground Check: Draw a small sphere at the feet
        if (groundCheck != null)
        {
            _isGrounded = Physics.CheckSphere(groundCheck.position, 0.2f, groundLayer);
        }
        else
        {
            // Fallback: Just check Y position
            _isGrounded = transform.position.y < 1.0f;
        }

        if (transform.position.y < -5)
        {
            Die();
        }
    }

    private void FixedUpdate()
    {
        if (!alive) return;

        // Forward Movement
        Vector3 forwardMove = transform.forward * speed * Time.fixedDeltaTime;

        // Side Movement (From InputManager)
        float targetX = InputManager.Instance.GetTargetX();
        Vector3 newPosition = rb.position + forwardMove;
        newPosition.x = targetX;

        // Apply Position Move
        rb.MovePosition(newPosition);

        // Jump Logic (From InputManager)
        if (InputManager.Instance.GetJumpInput())
        {
            if (_isGrounded)
            {
                // Use AddForce for jumping (Physics)
                rb.AddForce(Vector3.up * jumpForce);
            }

            // Consumes the input to not jump every single frame
            InputManager.Instance.ResetJump();
        }
    }

    public void Die()
    {
        //alive = false;
        //Invoke("Restart", 2);
    }

    void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}