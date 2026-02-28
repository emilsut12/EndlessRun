using UnityEngine;

/// <summary>
/// Central hub for game inputs. Automatically prioritizes connected and tracked devices.
/// </summary>
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    [Header("Keyboard Settings")]
    public float keyboardSpeed = 10.0f;

    [Header("Kinect Settings")]
    public float kinectMovementScale = 4.0f;

    [Header("Optional Providers")]
    [Tooltip("Link the Kinect script here")]
    [SerializeField] private KinectInputProvider kinectInput;

    private float finalX = 0f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        // Priority 1: Kinect
        if (kinectInput != null && kinectInput.IsTracked())
        {
            finalX = kinectInput.GetLeanValue() * kinectMovementScale;
            return;
        }

        // Priority 2: Webcam (Future implementation)

        // Priority 3: Standard Keyboard
        float rawInput = Input.GetAxis("Horizontal");
        finalX += rawInput * keyboardSpeed * Time.deltaTime;
    }

    /// <summary>
    /// Returns the calculated horizontal position for the player to use.
    /// </summary>
    public float GetFinalX()
    {
        return finalX;
    }

    /// <summary>
    /// Returns true if the active priority device registers a jump.
    /// </summary>
    public bool GetJumpInput()
    {
        // Priority 1: Kinect
        if (kinectInput != null && kinectInput.IsTracked())
        {
            return kinectInput.IsJumping();
        }

        // Priority 2: Keyboard Fallback
        return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow);
    }
}