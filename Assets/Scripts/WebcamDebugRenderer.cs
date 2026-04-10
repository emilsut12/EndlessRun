using UnityEngine;

/// <summary>
/// Renders the WebcamInputProvider raw feed onto Display 2 using the exact same mechanism
/// as KinectDirectRenderer: a helper MonoBehaviour is added to the Camera GameObject at
/// runtime so Unity''s OnPostRender() message fires correctly (it only fires when the
/// script is physically ON the camera object).
/// </summary>
public class WebcamDebugRenderer : MonoBehaviour
{
    [Tooltip("The WebcamInputProvider whose feed will be drawn.")]
    public WebcamInputProvider webcamInput;

    [Tooltip("Camera that targets Display 2. Leave empty to auto-find.")]
    public Camera targetCamera;

    private WebcamPostRenderHelper _helper;

    private void Awake()
    {
        // Redundant display activation in case KinectDirectRenderer failed to
        // initialize (e.g. Kinect SDK not installed). Without this call the
        // secondary monitor stays black even though the camera renders.
        if (Display.displays.Length > 1 && !Display.displays[1].active)
            Display.displays[1].Activate();
    }

    private void Start()
    {
        // Auto-find Display-2 camera if not set in inspector
        if (targetCamera == null)
        {
            foreach (Camera c in Camera.allCameras)
            {
                if (c.targetDisplay == 1) { targetCamera = c; break; }
            }
        }

        if (targetCamera == null)
        {
            Debug.LogWarning("WebcamDebugRenderer: Could not find Display-2 camera.");
            return;
        }

        // Add our helper directly onto the Camera's GameObject.
        _helper = targetCamera.gameObject.AddComponent<WebcamPostRenderHelper>();
        _helper.Init(webcamInput);
    }

    private void OnDestroy()
    {
        if (_helper != null)
            Destroy(_helper);
    }
}

/// <summary>
/// Added to the Display-2 Camera GameObject at runtime by WebcamDebugRenderer.
/// Uses OnPostRender() (MonoBehaviour message) to draw the webcam feed fullscreen.
/// </summary>
[AddComponentMenu("")]
public class WebcamPostRenderHelper : MonoBehaviour
{
    private WebcamInputProvider _webcamInput;
    private Material _lineMat;

    public void Init(WebcamInputProvider input)
    {
        _webcamInput = input;
    }

    private void OnDestroy()
    {
        if (_lineMat != null) DestroyImmediate(_lineMat);
    }

    // OnPostRender fires because this script IS on the Camera GameObject.
    // Only draws the webcam feed — centroid tracking has been removed.
    void OnPostRender()
    {
    }
}
