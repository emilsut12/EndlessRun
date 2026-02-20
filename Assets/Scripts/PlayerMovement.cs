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
            _isGrounded = Physics.CheckSphere(groundCheck.position, 0.2f, groundLayer);
        }
        else
        {
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

        Vector3 forwardMove = transform.forward * speed * Time.fixedDeltaTime;
        float targetX = InputManager.Instance.GetTargetX();

        Vector3 newPosition = rb.position + forwardMove;
        newPosition.x = targetX;

        rb.MovePosition(newPosition);

        // Jump Logic
        if (InputManager.Instance.GetJumpInput())
        {
            if (_isGrounded)
            {
                rb.AddForce(Vector3.up * jumpForce);

                if (animator != null)
                {
                    animator.SetTrigger("Jump");
                }
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