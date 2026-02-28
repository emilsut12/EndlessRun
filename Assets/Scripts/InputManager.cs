using UnityEngine;

/// <summary>
/// Central hub for game inputs. Automatically prioritizes connected and tracked devices.
/// </summary>
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    [Tooltip("Clamps the keyboard tracking so you don't accumulate infinite distance when holding A/D.")]
    public float horizontalLimit = 4f;

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
            // Clamp kinect movement just in case the player steps completely out of bounds
            finalX = Mathf.Clamp(finalX, -horizontalLimit, horizontalLimit);
            return;
        }

        // Priority 2: Standard Keyboard (Automatic Fallback)
        float rawInput = Input.GetAxis("Horizontal");
        finalX += rawInput * keyboardSpeed * Time.deltaTime;

        // FIX: Clamp the internal tracking so it doesn't get stuck infinitely accumulating!
        finalX = Mathf.Clamp(finalX, -horizontalLimit, horizontalLimit);
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