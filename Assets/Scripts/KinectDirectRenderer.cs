using UnityEngine;
using System.Collections.Generic;
using Kinect = Windows.Kinect;

// Renders the Kinect v2 skeleton on Display 2 (secondary monitor) using GL lines.
// Also draws adjustable left/right boundary boxes that control how far the player
// must physically move to reach the in-game lane extremes. The boundary edges can
// be dragged with the mouse or nudged with keyboard shortcuts, and changes are
// synced live to InputManager.minRealWorldX / maxRealWorldX.
public class KinectDirectRenderer : MonoBehaviour
{
    [Header("Kinect References")]
    public GameObject BodySourceManager;
    public Camera KinectCamera;
    public Material LineMaterial;

    [Header("View Options")]
    public bool mirrorView = false;
    public bool flipY = true;

    [Header("Boundary Boxes")]
    public bool showBoundaryBoxes = true;
    public Color leftBoxColor  = new Color(1f, 0.2f, 0.2f, 0.2f);
    public Color rightBoxColor = new Color(0.2f, 0.2f, 1f, 0.2f);
    public Color edgeColor      = Color.yellow;
    public Color edgeHoverColor = Color.white;

    [Tooltip("Half-width of the draggable edge line in normalized screen units (0-1).")]
    public float edgeThickness = 0.004f;

    [Tooltip("Mouse proximity required to grab an edge, in normalized screen units.")]
    public float edgeGrabDistance = 0.02f;

    [Tooltip("Speed for keyboard edge adjustment. Hold 1+Arrows (left edge) or 2+Arrows (right edge).")]
    public float keyboardAdjustSpeed = 0.3f;

    // Kinect internals
    private BodySourceManager _bodyManager;
    private Kinect.CoordinateMapper _mapper;
    private Kinect.Body[] _bodies;

    // Boundary edge positions in normalized screen X (0 = left of screen, 1 = right)
    private float _leftEdgeNormX;
    private float _rightEdgeNormX;

    // Mouse interaction state
    private bool _draggingLeft;
    private bool _draggingRight;
    private bool _hoveringLeft;
    private bool _hoveringRight;

    private bool _boundariesInitialized;

    // Player distance from sensor — used for real-world ↔ screen coordinate conversion
    private float _trackedPlayerZ = 2.5f;

    // Separate alpha-blended material for the transparent overlay quads
    private Material _boxMaterial;

    // Full Kinect v2 skeleton bone connectivity (child → parent)
    private static readonly Dictionary<Kinect.JointType, Kinect.JointType> BoneMap =
        new Dictionary<Kinect.JointType, Kinect.JointType>()
    {
        // Left leg
        { Kinect.JointType.FootLeft,      Kinect.JointType.AnkleLeft },
        { Kinect.JointType.AnkleLeft,     Kinect.JointType.KneeLeft },
        { Kinect.JointType.KneeLeft,      Kinect.JointType.HipLeft },
        { Kinect.JointType.HipLeft,       Kinect.JointType.SpineBase },

        // Right leg
        { Kinect.JointType.FootRight,     Kinect.JointType.AnkleRight },
        { Kinect.JointType.AnkleRight,    Kinect.JointType.KneeRight },
        { Kinect.JointType.KneeRight,     Kinect.JointType.HipRight },
        { Kinect.JointType.HipRight,      Kinect.JointType.SpineBase },

        // Left arm
        { Kinect.JointType.HandTipLeft,   Kinect.JointType.HandLeft },
        { Kinect.JointType.ThumbLeft,     Kinect.JointType.HandLeft },
        { Kinect.JointType.HandLeft,      Kinect.JointType.WristLeft },
        { Kinect.JointType.WristLeft,     Kinect.JointType.ElbowLeft },
        { Kinect.JointType.ElbowLeft,     Kinect.JointType.ShoulderLeft },
        { Kinect.JointType.ShoulderLeft,  Kinect.JointType.SpineShoulder },

        // Right arm
        { Kinect.JointType.HandTipRight,  Kinect.JointType.HandRight },
        { Kinect.JointType.ThumbRight,    Kinect.JointType.HandRight },
        { Kinect.JointType.HandRight,     Kinect.JointType.WristRight },
        { Kinect.JointType.WristRight,    Kinect.JointType.ElbowRight },
        { Kinect.JointType.ElbowRight,    Kinect.JointType.ShoulderRight },
        { Kinect.JointType.ShoulderRight, Kinect.JointType.SpineShoulder },

        // Spine
        { Kinect.JointType.SpineBase,     Kinect.JointType.SpineMid },
        { Kinect.JointType.SpineMid,      Kinect.JointType.SpineShoulder },
        { Kinect.JointType.SpineShoulder, Kinect.JointType.Neck },
        { Kinect.JointType.Neck,          Kinect.JointType.Head },
    };

    // ────────────────────────────────────────────────────────────────
    //  Lifecycle
    // ────────────────────────────────────────────────────────────────

    void Awake()
    {
        // Activate the second physical display if one is connected
        if (Display.displays.Length > 1)
            Display.displays[1].Activate();
    }

    void Start()
    {
        var sensor = Kinect.KinectSensor.GetDefault();
        if (sensor != null)
            _mapper = sensor.CoordinateMapper;

        if (BodySourceManager != null)
            _bodyManager = BodySourceManager.GetComponent<BodySourceManager>();

        // Route the KinectCamera to Display 2 (index 1)
        if (KinectCamera != null)
            KinectCamera.targetDisplay = 1;

        CreateBoxMaterial();
    }

    void Update()
    {
        if (_bodyManager != null)
            _bodies = _bodyManager.GetData();

        UpdatePlayerDepth();
        InitBoundariesOnce();

        if (showBoundaryBoxes)
        {
            HandleBoundaryMouseDrag();
            HandleBoundaryKeyboard();
        }
    }

    void OnDestroy()
    {
        if (_boxMaterial != null)
            DestroyImmediate(_boxMaterial);
    }

    // ────────────────────────────────────────────────────────────────
    //  Box material setup
    // ────────────────────────────────────────────────────────────────

    private void CreateBoxMaterial()
    {
        // Built-in colored shader with alpha blending for the transparent overlay quads
        _boxMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
        _boxMaterial.hideFlags = HideFlags.HideAndDontSave;
        _boxMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _boxMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _boxMaterial.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
        _boxMaterial.SetInt("_ZWrite",   0);
    }

    // ────────────────────────────────────────────────────────────────
    //  Player depth tracking
    // ────────────────────────────────────────────────────────────────

    private void UpdatePlayerDepth()
    {
        if (_bodies == null) return;

        foreach (var body in _bodies)
        {
            if (body == null || !body.IsTracked) continue;

            var spineBase = body.Joints[Kinect.JointType.SpineBase];
            if (spineBase.TrackingState != Kinect.TrackingState.NotTracked && spineBase.Position.Z > 0.5f)
                _trackedPlayerZ = spineBase.Position.Z;

            break; // Only use the first tracked body
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  Boundary initialization (once per scene load)
    //  Checks PlayerPrefs first so boundaries survive scene reloads.
    //  Falls back to InputManager inspector defaults on first-ever run.
    // ────────────────────────────────────────────────────────────────

    private const string PrefKeyLeftEdge  = "BoundaryLeftNormX";
    private const string PrefKeyRightEdge = "BoundaryRightNormX";

    private void InitBoundariesOnce()
    {
        if (_boundariesInitialized || InputManager.Instance == null) return;

        if (PlayerPrefs.HasKey(PrefKeyLeftEdge) && PlayerPrefs.HasKey(PrefKeyRightEdge))
        {
            // Restore from saved values (persists across scene reloads and game sessions)
            _leftEdgeNormX  = PlayerPrefs.GetFloat(PrefKeyLeftEdge);
            _rightEdgeNormX = PlayerPrefs.GetFloat(PrefKeyRightEdge);
        }
        else
        {
            // First-ever run — convert InputManager's default meter values to screen space
            _leftEdgeNormX  = RealWorldXToNormScreenX(InputManager.Instance.minRealWorldX);
            _rightEdgeNormX = RealWorldXToNormScreenX(InputManager.Instance.maxRealWorldX);
        }

        // Guarantee left < right on screen regardless of mirror setting
        if (_leftEdgeNormX > _rightEdgeNormX)
        {
            float tmp = _leftEdgeNormX;
            _leftEdgeNormX  = _rightEdgeNormX;
            _rightEdgeNormX = tmp;
        }

        // Push the restored positions into InputManager right away
        SyncBoundsToInputManager();

        _boundariesInitialized = true;
    }

    // ────────────────────────────────────────────────────────────────
    //  Mouse drag interaction
    // ────────────────────────────────────────────────────────────────

    private void HandleBoundaryMouseDrag()
    {
        float normX;
        if (!TryGetMouseNormX(out normX))
        {
            _hoveringLeft  = false;
            _hoveringRight = false;
            return;
        }

        float distToLeft  = Mathf.Abs(normX - _leftEdgeNormX);
        float distToRight = Mathf.Abs(normX - _rightEdgeNormX);

        // Hover highlight (only one edge at a time, whichever is closer)
        _hoveringLeft  = !_draggingRight && distToLeft  < edgeGrabDistance && distToLeft <= distToRight;
        _hoveringRight = !_draggingLeft  && distToRight < edgeGrabDistance && distToRight < distToLeft;

        // Begin drag
        if (Input.GetMouseButtonDown(0))
        {
            if (_hoveringLeft)
                _draggingLeft = true;
            else if (_hoveringRight)
                _draggingRight = true;
        }

        // End drag
        if (Input.GetMouseButtonUp(0))
        {
            if (_draggingLeft || _draggingRight)
                LogCurrentBounds();

            _draggingLeft  = false;
            _draggingRight = false;
        }

        // Move whichever edge is being dragged (clamped so they can't cross)
        if (_draggingLeft)
        {
            _leftEdgeNormX = Mathf.Clamp(normX, 0.02f, _rightEdgeNormX - 0.03f);
            SyncBoundsToInputManager();
        }
        else if (_draggingRight)
        {
            _rightEdgeNormX = Mathf.Clamp(normX, _leftEdgeNormX + 0.03f, 0.98f);
            SyncBoundsToInputManager();
        }
    }

    // Returns the mouse X position in normalized 0-1 screen space for the Kinect display.
    //
    // Display.RelativeMouseAt only works in standalone builds — it always returns
    // Vector3.zero in the Editor. So we fall back to Input.mousePosition relative to
    // the KinectCamera's pixel rect, which works when the Editor Game View is set to
    // Display 2 and is focused.
    private bool TryGetMouseNormX(out float normX)
    {
        normX = 0f;

        // --- Attempt 1: Display.RelativeMouseAt (works in standalone multi-display builds) ---
        Vector3 relMouse = Display.RelativeMouseAt(Input.mousePosition);

        bool relativeMouseWorked = (relMouse != Vector3.zero);
        if (relativeMouseWorked)
        {
            int displayIndex = (int)relMouse.z;
            if (displayIndex != 1)
                return false; // Mouse is on the wrong display

            float displayWidth = Display.displays[1].renderingWidth;
            if (displayWidth <= 0f)
                return false;

            normX = relMouse.x / displayWidth;
            return true;
        }

        // --- Attempt 2: Editor / single-window fallback ---
        // When the Game View is set to Display 2, Input.mousePosition gives coordinates
        // relative to that Game View window. Use the camera's pixel dimensions to normalize.
        if (KinectCamera != null && KinectCamera.pixelWidth > 0)
        {
            float mx = Input.mousePosition.x;
            float my = Input.mousePosition.y;

            // Reject if clearly outside the camera viewport
            if (mx < 0 || mx > KinectCamera.pixelWidth || my < 0 || my > KinectCamera.pixelHeight)
                return false;

            normX = mx / KinectCamera.pixelWidth;
            return true;
        }

        return false;
    }

    // ────────────────────────────────────────────────────────────────
    //  Keyboard controls (always work, no mouse/display issues)
    // ────────────────────────────────────────────────────────────────

    private void HandleBoundaryKeyboard()
    {
        // unscaledDeltaTime so edges can be adjusted even when Time.timeScale is 0 (menus)
        float delta = keyboardAdjustSpeed * Time.unscaledDeltaTime;

        // Hold 1 + Arrow Keys to move the left edge
        if (Input.GetKey(KeyCode.Alpha1))
        {
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                _leftEdgeNormX = Mathf.Max(0.02f, _leftEdgeNormX - delta);
                SyncBoundsToInputManager();
            }
            if (Input.GetKey(KeyCode.RightArrow))
            {
                _leftEdgeNormX = Mathf.Min(_rightEdgeNormX - 0.03f, _leftEdgeNormX + delta);
                SyncBoundsToInputManager();
            }
        }

        // Hold 2 + Arrow Keys to move the right edge
        if (Input.GetKey(KeyCode.Alpha2))
        {
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                _rightEdgeNormX = Mathf.Max(_leftEdgeNormX + 0.03f, _rightEdgeNormX - delta);
                SyncBoundsToInputManager();
            }
            if (Input.GetKey(KeyCode.RightArrow))
            {
                _rightEdgeNormX = Mathf.Min(0.98f, _rightEdgeNormX + delta);
                SyncBoundsToInputManager();
            }
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  InputManager sync
    // ────────────────────────────────────────────────────────────────

    private void SyncBoundsToInputManager()
    {
        if (InputManager.Instance == null) return;

        float leftRealX  = NormScreenXToRealWorldX(_leftEdgeNormX);
        float rightRealX = NormScreenXToRealWorldX(_rightEdgeNormX);

        // min/max guards in case mirrorView flips the direction
        InputManager.Instance.minRealWorldX = Mathf.Min(leftRealX, rightRealX);
        InputManager.Instance.maxRealWorldX = Mathf.Max(leftRealX, rightRealX);

        // Persist so boundaries survive scene reloads and game restarts
        PlayerPrefs.SetFloat(PrefKeyLeftEdge,  _leftEdgeNormX);
        PlayerPrefs.SetFloat(PrefKeyRightEdge, _rightEdgeNormX);
    }

    private void LogCurrentBounds()
    {
        if (InputManager.Instance == null) return;
        Debug.Log(
            $"[BoundaryBox] Left={InputManager.Instance.minRealWorldX:F2}m  " +
            $"Right={InputManager.Instance.maxRealWorldX:F2}m  " +
            $"(PlayerZ={_trackedPlayerZ:F2}m)");
    }

    // ────────────────────────────────────────────────────────────────
    //  Coordinate conversion:  real-world meters ↔ normalized screen X
    // ────────────────────────────────────────────────────────────────

    // Converts a physical X position (meters from Kinect center) to a 0-1 screen position.
    // Uses the Kinect CoordinateMapper when available for lens-corrected accuracy,
    // otherwise approximates with the Kinect v2 color camera horizontal FOV (~84.1°).
    private float RealWorldXToNormScreenX(float realX)
    {
        if (_mapper != null)
        {
            var csp = new Kinect.CameraSpacePoint();
            csp.X = realX;
            csp.Y = 0f;
            csp.Z = _trackedPlayerZ;

            var colorPt = _mapper.MapCameraPointToColorSpace(csp);

            // MapCameraPointToColorSpace returns -Infinity when the sensor has no frame
            // data yet (common at startup). Fall through to the FOV approximation.
            if (!float.IsInfinity(colorPt.X) && !float.IsNaN(colorPt.X))
            {
                float nx = colorPt.X / 1920f;
                if (mirrorView) nx = 1f - nx;
                return Mathf.Clamp01(nx);
            }
        }

        // FOV-based fallback (always valid, no sensor data needed)
        float halfFovRad = 42.05f * Mathf.Deg2Rad;
        float halfWidth  = _trackedPlayerZ * Mathf.Tan(halfFovRad);
        float nx2 = (realX / (2f * halfWidth)) + 0.5f;
        if (mirrorView) nx2 = 1f - nx2;
        return Mathf.Clamp01(nx2);
    }

    // Inverse of above — normalized screen X back to meters.
    private float NormScreenXToRealWorldX(float normX)
    {
        if (mirrorView) normX = 1f - normX;

        float halfFovRad = 42.05f * Mathf.Deg2Rad;
        float halfWidth  = _trackedPlayerZ * Mathf.Tan(halfFovRad);
        return (normX - 0.5f) * 2f * halfWidth;
    }

    // ────────────────────────────────────────────────────────────────
    //  GL rendering (runs after the KinectCamera finishes its frame)
    // ────────────────────────────────────────────────────────────────

    void OnPostRender()
    {
        if (LineMaterial == null) return;

        // Boundary overlays first (drawn behind the skeleton)
        if (showBoundaryBoxes && _boxMaterial != null)
        {
            _boxMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadOrtho();
            DrawBoundaryBoxes();
            GL.PopMatrix();
        }

        // Skeleton bones
        if (_bodies != null && _mapper != null)
        {
            LineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.LINES);

            foreach (var body in _bodies)
            {
                if (body == null || !body.IsTracked) continue;

                foreach (var bone in BoneMap)
                {
                    var j1 = body.Joints[bone.Key];
                    var j2 = body.Joints[bone.Value];

                    if (j1.TrackingState == Kinect.TrackingState.NotTracked ||
                        j2.TrackingState == Kinect.TrackingState.NotTracked)
                        continue;

                    Vector2 p1 = JointToNormScreen(j1);
                    Vector2 p2 = JointToNormScreen(j2);

                    // Green = fully tracked, Red = inferred position
                    Color c = (j1.TrackingState == Kinect.TrackingState.Tracked) ? Color.green : Color.red;
                    GL.Color(c);
                    GL.Vertex3(p1.x, p1.y, 0f);
                    GL.Vertex3(p2.x, p2.y, 0f);
                }
            }

            GL.End();
            GL.PopMatrix();
        }
    }

    // Draws the two shaded overlay regions and the bright draggable edge lines.
    private void DrawBoundaryBoxes()
    {
        // Left overlay: screen-left → left edge
        DrawQuad(0f, _leftEdgeNormX, leftBoxColor);

        // Right overlay: right edge → screen-right
        DrawQuad(_rightEdgeNormX, 1f, rightBoxColor);

        // Left draggable edge
        bool leftActive = _hoveringLeft || _draggingLeft;
        DrawEdgeLine(_leftEdgeNormX,
                     leftActive ? edgeHoverColor : edgeColor,
                     leftActive ? edgeThickness * 2f : edgeThickness);

        // Right draggable edge
        bool rightActive = _hoveringRight || _draggingRight;
        DrawEdgeLine(_rightEdgeNormX,
                     rightActive ? edgeHoverColor : edgeColor,
                     rightActive ? edgeThickness * 2f : edgeThickness);
    }

    // Full-height colored quad between two normalized X positions.
    private void DrawQuad(float xMin, float xMax, Color color)
    {
        GL.Begin(GL.QUADS);
        GL.Color(color);
        GL.Vertex3(xMin, 0f, 0f);
        GL.Vertex3(xMax, 0f, 0f);
        GL.Vertex3(xMax, 1f, 0f);
        GL.Vertex3(xMin, 1f, 0f);
        GL.End();
    }

    // Full-height vertical line drawn as a thin quad.
    private void DrawEdgeLine(float normX, Color color, float halfWidth)
    {
        GL.Begin(GL.QUADS);
        GL.Color(color);
        GL.Vertex3(normX - halfWidth, 0f, 0f);
        GL.Vertex3(normX + halfWidth, 0f, 0f);
        GL.Vertex3(normX + halfWidth, 1f, 0f);
        GL.Vertex3(normX - halfWidth, 1f, 0f);
        GL.End();
    }

    // ────────────────────────────────────────────────────────────────
    //  Joint-to-screen mapping (shared with skeleton rendering)
    // ────────────────────────────────────────────────────────────────

    // Maps a Kinect camera-space joint to a 0-1 normalized screen position on the
    // 1920×1080 color frame, then applies mirror/flip settings.
    private Vector2 JointToNormScreen(Kinect.Joint joint)
    {
        var cp = _mapper.MapCameraPointToColorSpace(joint.Position);
        float x = cp.X / 1920f;
        float y = cp.Y / 1080f;

        if (mirrorView) x = 1f - x;
        if (!flipY)     y = 1f - y;

        return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
    }
}
