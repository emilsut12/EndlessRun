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
    [Tooltip("Preferred motion provider. Webcam or Kinect providers can both plug in here.")]
    [SerializeField] private MotionInputProvider motionInput;
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
        MotionInputProvider activeMotionProvider = GetActiveMotionProvider();

        if (activeMotionProvider != null && activeMotionProvider.IsProviderAvailable && activeMotionProvider.IsTracked())
        {
            if (activeMotionProvider.TryGetHorizontalPosition(out float horizontalPosition, out MotionHorizontalSpace positionSpace))
            {
                float normalizedPosition = positionSpace == MotionHorizontalSpace.RealWorldMeters
                    ? Mathf.InverseLerp(minRealWorldX, maxRealWorldX, horizontalPosition)
                    : Mathf.Clamp01(horizontalPosition);

                finalX = Mathf.Lerp(-horizontalLimit, horizontalLimit, normalizedPosition);
                return;
            }
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
        MotionInputProvider activeMotionProvider = GetActiveMotionProvider();
        if (activeMotionProvider != null && activeMotionProvider.IsProviderAvailable && activeMotionProvider.IsTracked())
            return activeMotionProvider.GetJumpInput();

        // Priority 2: Keyboard Fallback
        return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow);
    }

    private MotionInputProvider GetActiveMotionProvider()
    {
        if (motionInput == null && kinectInput != null)
            motionInput = kinectInput;

        return motionInput;
    }
}