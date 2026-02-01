using UnityEngine;

// Abstraction layer for handling different input types
public class InputManager : MonoBehaviour
{
    public static InputManager Instance;

    [Header("Settings")]
    public float movementScale = 4.0f;  // Shared scale for all inputs
    public float keyboardSpeed = 10.0f;

    public float _finalX = 0f;  // Result x for the player

    // Track if Kinect is currently driving
    private bool _kinectIsActive = false;

    private bool _isJumping = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public float GetTargetX()
    {
        return _finalX;
    }
    public bool GetJumpInput()
    {
        return _isJumping;
    }

    public void ResetJump()
    {
        _isJumping = false;
    }

    void Update()
    {
        // Fallback: Keyboard Input
        // Only run this if Kinect is NOT driving
        if (!_kinectIsActive)
        {
            float input = Input.GetAxis("Horizontal"); // A/D or Left/Right
            _finalX += input * keyboardSpeed * Time.deltaTime;
        }

        // Keyboard Jump (Spacebar)
        // Use OR logic: If Kinect set jumping to true, keep it true. 
        // If not, check spacebar.
        if (Input.GetKeyDown(KeyCode.Space))
        {
            _isJumping = true;
        }

        // Clamp to ensure keyboard doesn't fly off screen
        _finalX = Mathf.Clamp(_finalX, -3.5f, 3.5f);
    }

    // Kinect handling
    // Kinect script calls this to take controll
    public void SetKinectInput(float kinectX)
    {
        _kinectIsActive = true;
        _finalX = kinectX * movementScale;
    }

    // Kinect Calls this for Jumping
    public void SetKinectJump(bool jumpState)
    {
        if (jumpState)
        {
            _isJumping = true;
        }
    }

    // Kinect script calls this when it loses controll
    public void SetKinectInactive()
    {
        _kinectIsActive = false;
    }
}
