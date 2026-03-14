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

    [Header("Kinect Real World Mapping")]
    [Tooltip("Furthest left physical point the player can step to. (Meters)")]
    public float minRealWorldX = -1.5f;
    [Tooltip("Furthest right physical point the player can step to. (Meters)")]
    public float maxRealWorldX = 1.5f;

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
        // Priority 1: Kinect (Now strictly checks if it's initialized successfully)
        if (kinectInput != null && kinectInput.IsKinectInitialized && kinectInput.IsTracked())
        {
            // 1. Get physical room position in meters
            float realWorldX = kinectInput.GetPlayerRealWorldX();

            // 2. Find percentage (0.0 to 1.0). e.g., If standing dead center, this returns 0.5.
            // InverseLerp automatically clamps, so moving out of bounds won't break it.
            float normalizedPosition = Mathf.InverseLerp(minRealWorldX, maxRealWorldX, realWorldX);

            // 3. Map that percentage directly to the game's left/right bounds
            finalX = Mathf.Lerp(-horizontalLimit, horizontalLimit, normalizedPosition);

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
        if (kinectInput != null && kinectInput.IsKinectInitialized && kinectInput.IsTracked())
        {
            return kinectInput.IsJumping();
        }

        // Priority 2: Keyboard Fallback
        return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow);
    }
}