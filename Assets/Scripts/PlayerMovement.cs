using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerMovement : MonoBehaviour
{
    bool alive = true;

    public float speed = 10;
    [SerializeField] Rigidbody rb;

    float horizontalInput;
    [SerializeField] float horizontalMovementMultiplier = 2;

    public float speedIncreasePerPoint = 0.1f;

    private void FixedUpdate()
    {
        if (!alive) return;

        Vector3 forwardMove = transform.forward * speed * Time.fixedDeltaTime;
        Vector3 horizontalMove = transform.right * horizontalInput * speed * Time.fixedDeltaTime * horizontalMovementMultiplier;
        rb.MovePosition(rb.position + forwardMove + horizontalMove);
    }

    private void Update()
    {
        horizontalInput = Input.GetAxis("Horizontal");

        if (transform.position.y < -5)
        {
            Die();
        }
    }

    public void Die()
    {
        alive = false;

        // Restart after 2s
        Invoke("Restart", 2);
    }

    void Restart()
    {
        // restart game
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
