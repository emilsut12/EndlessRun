using UnityEngine;

/// <summary>
/// Provides motion input using a standard webcam via background subtraction.
/// Tracks the player's horizontal position and jump by computing the centroid of
/// foreground pixels each frame.
///
/// HOW TO USE:
///  1. Add this component to a GameObject in your scene.
///  2. Assign it to InputManager's "Webcam Input" slot.
///  3. When the scene starts, stand CLEAR of the camera for ~3 seconds while the
///     background is captured, then step in front of it to start being tracked.
///  4. Call RecaptureBackground() (or press the UI button) to reset the calibration
///     at any time.
///
/// TUNING TIPS:
///  - Increase differenceThreshold if random noise is triggering false tracking.
///  - Decrease differenceThreshold if the player isn't being detected.
///  - Adjust jumpYThreshold if jumps are too sensitive or not sensitive enough.
///  - If jumping triggers while ducking, toggle invertJumpDetection.
/// </summary>
public class WebcamInputProvider : MotionInputProvider
{
    [Header("Webcam Settings")]
    [Tooltip("Index of the webcam device to use (0 = first/default camera).")]
    public int deviceIndex = 0;

    [Tooltip("Requested capture resolution width. Actual resolution depends on the device.")]
    public int captureWidth = 160;

    [Tooltip("Requested capture resolution height. Actual resolution depends on the device.")]
    public int captureHeight = 120;

    [Tooltip("Requested capture frame rate.")]
    public int fps = 30;

    [Header("Image Orientation")]
    [Tooltip("Mirror the image horizontally. Most front-facing webcams stream a mirrored image.")]
    public bool mirrorHorizontal = true;

    [Tooltip("Override vertical flip direction. Leave false to use the webcam's reported value automatically.")]
    public bool overrideVerticalFlip = false;

    [Tooltip("Active only when overrideVerticalFlip is true. Set this if the jump axis is inverted.")]
    public bool manualFlipVertical = false;

    [Header("Background Subtraction")]
    [Tooltip("Seconds to wait at scene start before snapping the background reference. " +
             "The player should NOT be in front of the camera during this window.")]
    public float backgroundCaptureDelay = 1f;

    [Tooltip("Per-pixel colour difference magnitude (0-1) required to classify a pixel as foreground.")]
    [Range(0.04f, 0.5f)]
    public float differenceThreshold = 0.15f;

    [Tooltip("Fraction of total pixels that must be foreground for the player to count as tracked.")]
    [Range(0.001f, 0.15f)]
    public float minimumForegroundRatio = 0.005f;

    [Header("Jump Detection")]
    [Tooltip("How much the foreground centroid must rise (normalised 0-1 Y) above the baseline " +
             "to fire a jump. Smaller = more sensitive.")]
    [Range(0.02f, 0.3f)]
    public float jumpYThreshold = 0.06f;

    [Tooltip("How quickly the Y baseline adapts to the player's resting posture (units/sec, lerp factor).")]
    [Range(0.5f, 5f)]
    public float baselineAdaptSpeed = 1.5f;

    // ── Internal state ────────────────────────────────────────────────────────────
    private WebCamTexture _webcamTexture;
    private Color32[] _backgroundPixels;
    private Color32[] _currentPixels;
    private byte[]    _foregroundMask;

    private bool  _isInitialized     = false;
    private bool  _backgroundCaptured = false;
    private bool  _webcamHasDeliveredFrame = false;
    private float _initTimer         = 0f;

    // Tracking output
    private bool  _isTracked   = false;
    private float _centroidX   = 0.5f;   // 0 = left,  1 = right
    private float _centroidY   = 0.5f;   // 0 = bottom, 1 = top (after flip correction)

    // Jump baseline
    private float _baselineCentroidY = 0.5f;
    private bool  _isBaselineSet     = false;
    private bool  _isJumping         = false;

    // Debug visualisation
    private Texture2D _debugTexture;
    private Color32[] _debugPixels;

    // ── MotionInputProvider API ───────────────────────────────────────────────────
    public override string ProviderName       => "Webcam";

    /// <summary>True as soon as the WebCamTexture is playing — used by the debug renderer.</summary>
    public bool IsWebcamRunning =>
        _isInitialized && _webcamTexture != null && _webcamTexture.isPlaying;

    /// <summary>True once the background reference frame has been captured.</summary>
    public bool IsCalibrated => _backgroundCaptured;

    /// <summary>The raw WebCamTexture — available immediately after the webcam starts.</summary>
    public Texture GetRawTexture() => _webcamTexture;

    public override bool   IsProviderAvailable =>
        _isInitialized && _webcamTexture != null && _webcamTexture.isPlaying && _backgroundCaptured;

    public override bool IsTracked() => _isTracked;

    public override bool TryGetHorizontalPosition(out float positionX, out MotionHorizontalSpace positionSpace)
    {
        positionX     = _centroidX;
        positionSpace = MotionHorizontalSpace.NormalizedScreenX;
        return _isTracked;
    }

    public override bool GetJumpInput() => _isJumping;

    public override Texture GetDebugTexture() => _debugTexture;

    // ── Lifecycle ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        TryInitializeWebcam();
    }

    private void Start()
    {
        // Webcam is now initialized in Awake for earliest possible start.
    }

    private void Update()
    {
        if (!_isInitialized || _webcamTexture == null || !_webcamTexture.isPlaying)
            return;

        // Track whether the webcam hardware has started delivering frames yet.
        bool hasFrame = _webcamTexture.didUpdateThisFrame;
        bool webcamReady = _webcamHasDeliveredFrame || hasFrame;
        if (hasFrame) _webcamHasDeliveredFrame = true;

        // Reallocate buffers if the actual resolution differs from requested.
        if (webcamReady)
        {
            int actualW = _webcamTexture.width;
            int actualH = _webcamTexture.height;
            if (actualW > 1 && actualH > 1 && _currentPixels.Length != actualW * actualH)
            {
                ReallocateBuffers(actualW, actualH);
            }
        }

        if (!_backgroundCaptured)
        {
// Use unscaledDeltaTime so the timer runs even when Time.timeScale == 0
        // (e.g. during the MainMenu state).
        if (webcamReady)
            _initTimer += Time.unscaledDeltaTime;

            // Show progress/preview in the debug texture.
            UpdateDebugTexturePreCapture();

            // Capture background once we have actual pixels and the delay has elapsed.
            if (webcamReady && _initTimer >= backgroundCaptureDelay)
            {
                CaptureBackground();
            }
            return;
        }

        if (!hasFrame) return;

        _webcamTexture.GetPixels32(_currentPixels);
        ProcessFrame();
    }

    private void OnDestroy()
    {
        if (_webcamTexture != null && _webcamTexture.isPlaying)
            _webcamTexture.Stop();

        if (_debugTexture != null)
            Destroy(_debugTexture);
    }

    // ── Initialisation ────────────────────────────────────────────────────────────

    private void TryInitializeWebcam()
    {
        WebCamDevice[] devices = WebCamTexture.devices;

        if (devices.Length == 0)
        {
            Debug.LogWarning("WebcamInputProvider: No webcam devices found.");
            return;
        }

        int idx = Mathf.Clamp(deviceIndex, 0, devices.Length - 1);
        _webcamTexture = new WebCamTexture(devices[idx].name, captureWidth, captureHeight, fps);
        _webcamTexture.Play();

        int pixelCount = captureWidth * captureHeight;
        _backgroundPixels = new Color32[pixelCount];
        _currentPixels    = new Color32[pixelCount];
        _foregroundMask   = new byte[pixelCount];

        _debugTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        _debugPixels  = new Color32[pixelCount];

        _isInitialized = true;
        Debug.Log($"WebcamInputProvider: Started '{devices[idx].name}' ({captureWidth}x{captureHeight}). " +
                  $"Background will be captured in {backgroundCaptureDelay}s — keep the camera clear.");
    }

    private void ReallocateBuffers(int w, int h)
    {
        int count     = w * h;
        captureWidth  = w;
        captureHeight = h;

        _backgroundPixels   = new Color32[count];
        _currentPixels      = new Color32[count];
        _foregroundMask     = new byte[count];
        _debugPixels        = new Color32[count];

        // Recreate the debug texture at the corrected size
        if (_debugTexture != null) Destroy(_debugTexture);
        _debugTexture = new Texture2D(w, h, TextureFormat.RGB24, false);

        // Reset calibration so background is re-captured at the correct size
        _backgroundCaptured = false;
        _initTimer          = 0f;
        _isBaselineSet      = false;
        _isTracked          = false;
        _isJumping          = false;

        Debug.Log($"WebcamInputProvider: Actual resolution {w}x{h} differs from requested — buffers reallocated.");
    }

    private void CaptureBackground()
    {
        _webcamTexture.GetPixels32(_backgroundPixels);
        _backgroundCaptured = true;
        _isBaselineSet      = false;
        Debug.Log("WebcamInputProvider: Background captured. Step into frame to begin tracking.");
    }

    // ── Per-frame processing ──────────────────────────────────────────────────────

    private void ProcessFrame()
    {
        int width  = captureWidth;
        int height = captureHeight;
        int total  = width * height;

        // Determine vertical flip: we want _centroidY=1 to mean "player is high up"
        // so that rising centroid → jump.
        bool flipVertical = overrideVerticalFlip
            ? manualFlipVertical
            : _webcamTexture.videoVerticallyMirrored;

        // ── Background subtraction & centroid accumulation ────────────────────────
        double sumX = 0, sumY = 0;
        int foregroundCount = 0;

        for (int i = 0; i < total; i++)
        {
            Color32 c = _currentPixels[i];
            Color32 b = _backgroundPixels[i];

            // Per-channel absolute difference, averaged and normalised to 0-1
            float diff = (Mathf.Abs(c.r - b.r) +
                          Mathf.Abs(c.g - b.g) +
                          Mathf.Abs(c.b - b.b)) / (3f * 255f);

            bool isForeground = diff > differenceThreshold;
            _foregroundMask[i] = isForeground ? (byte)255 : (byte)0;

            if (isForeground)
            {
                sumX += i % width;
                sumY += i / width;
                foregroundCount++;
            }
        }

        // ── Tracking decision ─────────────────────────────────────────────────────
        float foregroundRatio = (float)foregroundCount / total;
        _isTracked = foregroundRatio >= minimumForegroundRatio;

        if (_isTracked)
        {
            float rawX = (float)(sumX / foregroundCount) / (width  - 1);
            float rawY = (float)(sumY / foregroundCount) / (height - 1);

            // rawY: 0 = bottom of pixel buffer (OpenGL origin), 1 = top of pixel buffer.
            // After flip correction, _centroidY=0 means low in the real world,
            // _centroidY=1 means high → player jumping raises _centroidY.
            _centroidX = mirrorHorizontal ? (1f - rawX) : rawX;
            _centroidY = flipVertical     ? (1f - rawY) : rawY;

            // ── Jump detection ────────────────────────────────────────────────────
            if (!_isBaselineSet)
            {
                _baselineCentroidY = _centroidY;
                _isBaselineSet     = true;
                _isJumping         = false;
            }
            else
            {
                _isJumping = _centroidY > (_baselineCentroidY + jumpYThreshold);

                // Slowly drift baseline toward the player's resting position so it
                // adapts to posture changes without getting permanently stuck.
                _baselineCentroidY = Mathf.Lerp(_baselineCentroidY, _centroidY,
                                                 baselineAdaptSpeed * Time.deltaTime);
            }
        }
        else
        {
            _isJumping     = false;
            _isBaselineSet = false;
        }

        UpdateDebugTexture();
    }

    // ── Debug visualisation ───────────────────────────────────────────────────────

    /// <summary>
    /// Called each frame BEFORE background capture is complete.
    /// Shows the live camera feed with a blue tint so the user knows the camera
    /// is active and waiting. A progress bar is drawn across the bottom.
    /// </summary>
    private void UpdateDebugTexturePreCapture()
    {
        if (_debugTexture == null || _webcamTexture == null) return;

        // Only read pixels if the webcam has actually delivered a frame.
        bool hasPixels = _webcamHasDeliveredFrame;
        if (hasPixels)
            _webcamTexture.GetPixels32(_currentPixels);

        int width = captureWidth;
        int height = captureHeight;
        int total = width * height;
        float progress = Mathf.Clamp01(_initTimer / backgroundCaptureDelay);
        int progressPixelX = Mathf.RoundToInt(progress * width);
        int barHeight = Mathf.Max(2, height / 20); // bottom ~5% of frame

        for (int i = 0; i < total; i++)
        {
            int px = i % width;
            int py = i / width;

            if (py < barHeight)
            {
                // Progress bar: green for filled, dark grey for unfilled
                _debugPixels[i] = px < progressPixelX
                    ? new Color32(0, 210, 60, 255)
                    : new Color32(40, 40, 40, 255);
            }
            else if (hasPixels)
            {
                // Blue-tinted live feed
                Color32 c = _currentPixels[i];
                _debugPixels[i] = new Color32(
                    (byte)(c.r >> 2),
                    (byte)(c.g >> 2),
                    (byte)Mathf.Min(255, c.b / 2 + 100),
                    255);
            }
            else
            {
                // No frames yet — dark blue
                _debugPixels[i] = new Color32(10, 10, 40, 255);
            }
        }

        _debugTexture.SetPixels32(_debugPixels);
        _debugTexture.Apply();
    }

    private void UpdateDebugTexture()
    {
        if (_debugTexture == null) return;

        int total = captureWidth * captureHeight;
        for (int i = 0; i < total; i++)
        {
            if (_foregroundMask[i] > 0)
            {
                // Foreground: bright green
                _debugPixels[i] = new Color32(0, 210, 60, 255);
            }
            else
            {
                // Background: dim version of the live feed
                Color32 c = _currentPixels[i];
                _debugPixels[i] = new Color32(
                    (byte)(c.r >> 2),
                    (byte)(c.g >> 2),
                    (byte)(c.b >> 2),
                    255);
            }
        }

        _debugTexture.SetPixels32(_debugPixels);
        _debugTexture.Apply();
    }

    // ── Public utilities ──────────────────────────────────────────────────────────

    /// <summary>
    /// Restart the background capture countdown. Call this via UI button or when
    /// the scene setup changes (e.g. lighting shift).
    /// </summary>
    public void RecaptureBackground()
    {
        _backgroundCaptured = false;
        _initTimer          = 0f;
        _isTracked          = false;
        _isJumping          = false;
        _isBaselineSet      = false;
        Debug.Log($"WebcamInputProvider: Recapturing background in {backgroundCaptureDelay}s. " +
                   "Step out of the camera's view.");
    }

    /// <summary>
    /// Returns a description of all detected webcam devices — useful for debugging
    /// or building a device-selection UI.
    /// </summary>
    public static string GetDeviceList()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0) return "No webcam devices found.";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < devices.Length; i++)
            sb.AppendLine($"  [{i}] {devices[i].name}");
        return sb.ToString();
    }

    /// <summary>
    /// Returns the index of the device currently in use, or -1 if not initialised.
    /// </summary>
    public int ActiveDeviceIndex => _isInitialized ? Mathf.Clamp(deviceIndex, 0, WebCamTexture.devices.Length - 1) : -1;

    /// <summary>
    /// Returns the current foreground centroid in normalised screen space (both axes 0-1).
    /// </summary>
    public Vector2 GetCentroid() => new Vector2(_centroidX, _centroidY);
}
