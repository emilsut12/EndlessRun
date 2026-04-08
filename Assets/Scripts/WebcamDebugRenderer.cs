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

    [Tooltip("Draw a centroid cross-hair showing where the tracker thinks the player is.")]
    public bool showCentroid = true;

    [Tooltip("Colour of the centroid cross-hair lines.")]
    public Color centroidColor = new Color(1f, 0.8f, 0f, 1f);

    [Range(0.01f, 0.15f)]
    public float centroidSize = 0.04f;

    [Range(0.001f, 0.02f)]
    public float centroidThickness = 0.004f;

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

        // Add our helper directly onto the Camera''s GameObject.
        // This is the same pattern as KinectDirectRenderer: OnPostRender() only
        // fires as a MonoBehaviour message when the script IS ON the camera object.
        _helper = targetCamera.gameObject.AddComponent<WebcamPostRenderHelper>();
        _helper.Init(webcamInput, showCentroid, centroidColor, centroidSize, centroidThickness);
    }

    private void OnDestroy()
    {
        if (_helper != null)
            Destroy(_helper);
    }
}

/// <summary>
/// Added to the Display-2 Camera GameObject at runtime by WebcamDebugRenderer.
/// Uses OnPostRender() (MonoBehaviour message) to draw the webcam feed fullscreen,
/// then overlays the centroid crosshair — identical to how KinectDirectRenderer works.
/// </summary>
[AddComponentMenu("")]
public class WebcamPostRenderHelper : MonoBehaviour
{
    private WebcamInputProvider _webcamInput;
    private bool   _showCentroid;
    private Color  _centroidColor;
    private float  _centroidSize;
    private float  _centroidThickness;

    private Material _lineMat;

    public void Init(WebcamInputProvider input, bool showCentroid,
                     Color centroidColor, float centroidSize, float centroidThickness)
    {
        _webcamInput       = input;
        _showCentroid      = showCentroid;
        _centroidColor     = centroidColor;
        _centroidSize      = centroidSize;
        _centroidThickness = centroidThickness;

        Shader lineShader = Shader.Find("Hidden/Internal-Colored");
        if (lineShader != null)
        {
            _lineMat = new Material(lineShader) { hideFlags = HideFlags.HideAndDontSave };
            _lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _lineMat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
            _lineMat.SetInt("_ZWrite",   0);
        }
    }

    private void OnDestroy()
    {
        if (_lineMat != null) DestroyImmediate(_lineMat);
    }

    // OnPostRender fires because this script IS on the Camera GameObject.
    // Only draws the centroid crosshair — the webcam feed is already drawn by
    // KinectDirectRenderer.OnPostRender() on this same camera. Drawing it twice
    // was covering the boundary boxes.
    void OnPostRender()
    {
        if (_webcamInput == null) return;

        // Draw centroid crosshair on top
        if (_showCentroid && _webcamInput.IsTracked() && _lineMat != null)
        {
            Vector2 c = _webcamInput.GetCentroid();
            float h = _centroidThickness * 0.5f;

            _lineMat.SetPass(0);
            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.QUADS);
            GL.Color(_centroidColor);

            // Horizontal bar
            GL.Vertex3(c.x - _centroidSize, c.y - h, 0f);
            GL.Vertex3(c.x + _centroidSize, c.y - h, 0f);
            GL.Vertex3(c.x + _centroidSize, c.y + h, 0f);
            GL.Vertex3(c.x - _centroidSize, c.y + h, 0f);

            // Vertical bar
            GL.Vertex3(c.x - h, c.y - _centroidSize, 0f);
            GL.Vertex3(c.x + h, c.y - _centroidSize, 0f);
            GL.Vertex3(c.x + h, c.y + _centroidSize, 0f);
            GL.Vertex3(c.x - h, c.y + _centroidSize, 0f);

            GL.End();
            GL.PopMatrix();
        }
    }
}
