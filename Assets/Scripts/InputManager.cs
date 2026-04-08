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
    [Tooltip("Highest-priority motion provider override. If set and available it wins over all others.")]
    [SerializeField] private MotionInputProvider motionInput;
    [Tooltip("Kinect input provider. Used when motionInput is unset/unavailable.")]
    [SerializeField] private KinectInputProvider kinectInput;
    [Tooltip("Webcam input provider. Used when both motionInput and kinectInput are unavailable.")]
    [SerializeField] private WebcamInputProvider webcamInput;

    [Header("Kinect Real World Mapping")]
    [Tooltip("Furthest left physical point the player can step to. (Meters)")]
    public float minRealWorldX = -1.5f;
    [Tooltip("Furthest right physical point the player can step to. (Meters)")]
    public float maxRealWorldX = 1.5f;

    [Header("Webcam Normalized Boundaries")]
    [Tooltip("Left boundary in normalized screen X (0-1). Set automatically by KinectDirectRenderer boundary boxes.")]
    public float minNormalizedX = 0f;
    [Tooltip("Right boundary in normalized screen X (0-1). Set automatically by KinectDirectRenderer boundary boxes.")]
    public float maxNormalizedX = 1f;

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
                float normalizedPosition;
                if (positionSpace == MotionHorizontalSpace.RealWorldMeters)
                {
                    normalizedPosition = Mathf.InverseLerp(minRealWorldX, maxRealWorldX, horizontalPosition);
                }
                else
                {
                    // Remap the 0-1 centroid through the normalized boundary positions
                    // so that the Display 2 boundary boxes control the webcam play area.
                    normalizedPosition = Mathf.InverseLerp(minNormalizedX, maxNormalizedX, horizontalPosition);
                }

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
        // 1. Explicit override field (highest priority)
        if (motionInput != null && motionInput.IsProviderAvailable)
            return motionInput;

        // 2. Kinect
        if (kinectInput != null && kinectInput.IsProviderAvailable)
            return kinectInput;

        // 3. Webcam
        if (webcamInput != null && webcamInput.IsProviderAvailable)
            return webcamInput;

        return null;
    }
}