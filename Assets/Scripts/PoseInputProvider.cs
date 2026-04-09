using System.Collections;
using UnityEngine;
using Unity.InferenceEngine;

/// <summary>
/// ML-based body tracking using a webcam and a MoveNet SinglePose Lightning ONNX model
/// via Unity Sentis. Reports horizontal position (hip midpoint) and jump detection
/// (shoulder height) through the MotionInputProvider API.
///
/// SETUP:
///  1. Download the MoveNet SinglePose Lightning model in ONNX format.
///     - Search for "movenet singlepose lightning onnx" or convert from TensorFlow Hub
///       using tf2onnx. A direct ONNX export is also available on Hugging Face.
///  2. Place the .onnx file inside Assets/ (e.g. Assets/Models/movenet_lightning.onnx).
///  3. Assign the resulting ModelAsset to the "Model Asset" field in the Inspector.
///  4. Assign this component to InputManager's "Pose Input" slot or GameManager's
///     tracking toggle.
///
/// HOW IT WORKS:
///  - Each frame the webcam texture is resized to 192×192 and fed through the model.
///  - The model outputs 17 body keypoints (y, x, confidence) in normalised 0-1 coords.
///  - Horizontal position = midpoint of left and right hip X.
///  - Jump detection = shoulder midpoint Y rising above a running baseline.
/// </summary>
public class PoseInputProvider : MotionInputProvider
{
    [Header("Webcam Settings")]
    [Tooltip("Index of the webcam device to use (0 = first/default camera).")]
    public int deviceIndex = 0;

    [Tooltip("Requested capture resolution width.")]
    public int captureWidth = 320;

    [Tooltip("Requested capture resolution height.")]
    public int captureHeight = 240;

    [Tooltip("Requested capture frame rate.")]
    public int fps = 30;

    [Tooltip("Mirror the image horizontally (most front-facing webcams are mirrored).")]
    public bool mirrorHorizontal = true;

    [Header("Model")]
    [Tooltip("MoveNet SinglePose Lightning ONNX model asset. Expects input [1,192,192,3] " +
             "and output [1,1,17,3] with (y,x,confidence) per keypoint.")]
    public ModelAsset modelAsset;

    [Header("Inference")]
    [Tooltip("Run inference every N frames to save performance (1 = every frame).")]
    [Range(1, 10)]
    public int inferenceInterval = 1;

    [Tooltip("Use GPU for inference. Disable to fall back to CPU. For small models like MoveNet, "
             + "CPU is often faster because it avoids GPU pipeline stall on synchronous readback.")]
    public bool useGPU = false;

    [Header("Model Input Size")]
    [Tooltip("Resolution the webcam frame is resized to before inference. "
             + "MoveNet Lightning = 192, MoveNet Thunder = 256. Change this when you swap models.")]
    public int modelInputSize = 192;

    [Header("Tracking")]
    [Tooltip("Minimum keypoint confidence (0-1) to consider a detection valid.")]
    [Range(0.1f, 0.9f)]
    public float minimumConfidence = 0.3f;

    [Header("Jump Detection")]
    [Tooltip("How much the shoulder midpoint must rise (normalised 0-1 Y) above the " +
             "baseline to trigger a jump.")]
    [Range(0.02f, 0.3f)]
    public float jumpYThreshold = 0.06f;

    [Tooltip("How quickly the Y baseline adapts to the player's resting posture.")]
    [Range(0.5f, 5f)]
    public float baselineAdaptSpeed = 1.5f;

    [Header("Smoothing")]
    [Tooltip("How quickly the tracked shoulder Y catches up for jump detection. Higher = more responsive.")]
    [Range(2f, 50f)]
    public float positionSmoothSpeed = 15f;

    [Header("Jump Calibration")]
    [Tooltip("Seconds after a jump ends before the baseline starts adapting again. " +
             "Prevents post-jump crouch from corrupting the baseline.")]
    [Range(0.1f, 2f)]
    public float jumpCooldown = 0.5f;

    [Tooltip("Seconds to measure standing height at game start before allowing jumps.")]
    [Range(0.5f, 5f)]
    public float calibrationDuration = 1.5f;

    // ── MoveNet keypoint indices ──────────────────────────────────────────────────
    const int NOSE            = 0;
    const int LEFT_EYE        = 1;
    const int RIGHT_EYE       = 2;
    const int LEFT_EAR        = 3;
    const int RIGHT_EAR       = 4;
    const int LEFT_SHOULDER   = 5;
    const int RIGHT_SHOULDER  = 6;
    const int LEFT_ELBOW      = 7;
    const int RIGHT_ELBOW     = 8;
    const int LEFT_WRIST      = 9;
    const int RIGHT_WRIST     = 10;
    const int LEFT_HIP        = 11;
    const int RIGHT_HIP       = 12;
    const int LEFT_KNEE       = 13;
    const int RIGHT_KNEE      = 14;
    const int LEFT_ANKLE      = 15;
    const int RIGHT_ANKLE     = 16;
    const int NUM_KEYPOINTS   = 17;

    // Skeleton connectivity for debug drawing (pairs of keypoint indices)
    private static readonly int[,] SkeletonBones = {
        {NOSE, LEFT_EYE}, {NOSE, RIGHT_EYE}, {LEFT_EYE, LEFT_EAR}, {RIGHT_EYE, RIGHT_EAR},
        {LEFT_SHOULDER, RIGHT_SHOULDER}, {LEFT_SHOULDER, LEFT_ELBOW}, {RIGHT_SHOULDER, RIGHT_ELBOW},
        {LEFT_ELBOW, LEFT_WRIST}, {RIGHT_ELBOW, RIGHT_WRIST},
        {LEFT_SHOULDER, LEFT_HIP}, {RIGHT_SHOULDER, RIGHT_HIP},
        {LEFT_HIP, RIGHT_HIP}, {LEFT_HIP, LEFT_KNEE}, {RIGHT_HIP, RIGHT_KNEE},
        {LEFT_KNEE, LEFT_ANKLE}, {RIGHT_KNEE, RIGHT_ANKLE},
    };

    // ── Internal state ────────────────────────────────────────────────────────────
    private WebCamTexture _webcamTexture;
    private Model _runtimeModel;
    private Worker _worker;
    private bool _isInitialized;
    private int _frameCounter;

    // Inference render target (resized webcam for the model)
    private RenderTexture _inferenceRT;

    // Keypoints: each is (x, y, confidence) in normalised 0-1 image coords
    private Vector3[] _keypoints = new Vector3[NUM_KEYPOINTS];

    // Tracking output
    private bool  _isTracked;
    private float _positionX = 0.5f;
    private float _rawPositionX = 0.5f;
    private float _shoulderY = 0.5f;
    private float _rawShoulderY = 0.5f;
    private float _baselineY = 0.5f;
    private bool  _isBaselineSet;
    private bool  _isJumping;

    // Jump cooldown / calibration
    private float _jumpCooldownTimer;
    private float _calibrationTimer;
    private bool  _calibrated;
    private float _calibrationAccumY;
    private int   _calibrationSamples;

    // Debug visualisation
    private Texture2D _debugTexture;
    private Color32[] _debugPixels;
    private bool _webcamHasDeliveredFrame;

    // ── MotionInputProvider API ───────────────────────────────────────────────────
    public override string ProviderName => "ML Pose";

    public override bool IsProviderAvailable =>
        _isInitialized && _webcamTexture != null && _webcamTexture.isPlaying;

    public bool IsWebcamRunning => IsProviderAvailable;

    public Texture GetRawTexture() => _webcamTexture;

    public override bool IsTracked() => _isTracked;

    public override bool TryGetHorizontalPosition(out float positionX, out MotionHorizontalSpace positionSpace)
    {
        positionX     = mirrorHorizontal ? (1f - _positionX) : _positionX;
        positionSpace = MotionHorizontalSpace.NormalizedScreenX;
        return _isTracked;
    }

    public override bool GetJumpInput() => _isJumping;

    public override Texture GetDebugTexture() => _debugTexture;

    /// <summary>Returns the raw keypoint array. Index with the keypoint constants.</summary>
    public Vector3[] GetKeypoints() => _keypoints;

    // ── Lifecycle ─────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        Debug.Log("[PoseInputProvider] OnEnable called");
        InitializeModel();
        StartCoroutine(StartWebcamDelayed());
    }

    private IEnumerator StartWebcamDelayed()
    {
        // Wait one frame so the OS has time to release the device handle
        // from any previous provider that was just destroyed/disabled.
        yield return null;
        StartWebcam();
        Debug.Log($"[PoseInputProvider] After init: _isInitialized={_isInitialized}, webcam={(_webcamTexture != null ? _webcamTexture.isPlaying.ToString() : "null")}");
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        StopWebcam();
        CleanupModel();
        _isInitialized = false;
        _isTracked = false;
        _isBaselineSet = false;
        _calibrated = false;
        _calibrationTimer = 0f;
        _calibrationAccumY = 0f;
        _calibrationSamples = 0;
    }

    private float _debugLogTimer;
    private void Update()
    {
        if (!_isInitialized || _webcamTexture == null || !_webcamTexture.isPlaying)
        {
            _debugLogTimer += Time.unscaledDeltaTime;
            if (_debugLogTimer > 3f)
            {
                Debug.Log($"[PoseInputProvider] Update early-out: init={_isInitialized}, tex={(_webcamTexture != null)}, playing={(_webcamTexture != null && _webcamTexture.isPlaying)}");
                _debugLogTimer = 0f;
            }
            return;
        }

        if (!_webcamTexture.didUpdateThisFrame)
            return;

        if (!_webcamHasDeliveredFrame)
        {
            _webcamHasDeliveredFrame = true;
            Debug.Log("[PoseInputProvider] First webcam frame received");
        }

        _frameCounter++;
        if (_frameCounter % inferenceInterval == 0)
        {
            RunInference();
            ExtractTracking();

            _debugLogTimer += Time.unscaledDeltaTime;
            if (_debugLogTimer > 3f)
            {
                Debug.Log($"[PoseInputProvider] Tracked={_isTracked}, hipLConf={_keypoints[LEFT_HIP].z:F2}, hipRConf={_keypoints[RIGHT_HIP].z:F2}, shlLConf={_keypoints[LEFT_SHOULDER].z:F2}, shlRConf={_keypoints[RIGHT_SHOULDER].z:F2}, posX={_positionX:F3}");
                _debugLogTimer = 0f;
            }
        }

        UpdateDebugTexture();
    }

    private void OnDestroy()
    {
        StopWebcam();
        CleanupModel();
        if (_debugTexture != null) Destroy(_debugTexture);
        if (_inferenceRT != null) RenderTexture.ReleaseTemporary(_inferenceRT);
    }

    // ── Webcam ────────────────────────────────────────────────────────────────────

    private void StartWebcam()
    {
        if (_webcamTexture != null && _webcamTexture.isPlaying)
            return; // already running

        var devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogWarning("PoseInputProvider: No webcam devices found.");
            return;
        }

        int idx = Mathf.Clamp(deviceIndex, 0, devices.Length - 1);
        _webcamTexture = new WebCamTexture(devices[idx].name, captureWidth, captureHeight, fps);
        _webcamTexture.Play();

        Debug.Log($"PoseInputProvider: Started webcam '{devices[idx].name}' ({captureWidth}x{captureHeight}).");
    }

    private void StopWebcam()
    {
        if (_webcamTexture != null)
        {
            if (_webcamTexture.isPlaying) _webcamTexture.Stop();
            Destroy(_webcamTexture);
            _webcamTexture = null;
        }
        _webcamHasDeliveredFrame = false;
    }

    // ── Model ─────────────────────────────────────────────────────────────────────

    private void InitializeModel()
    {
        if (modelAsset == null)
        {
            Debug.LogWarning("PoseInputProvider: No model asset assigned. " +
                             "Assign a MoveNet ONNX ModelAsset in the Inspector.");
            return;
        }

        _runtimeModel = ModelLoader.Load(modelAsset);
        var backend = useGPU ? BackendType.GPUCompute : BackendType.CPU;
        _worker = new Worker(_runtimeModel, backend);

        // Auto-detect input size from the model so Lightning (192) and Thunder (256)
        // both work without touching the Inspector. The inspector field is kept as a
        // visible read-only hint but the runtime value always wins.
        if (_runtimeModel.inputs.Count > 0)
        {
            var inputShape = _runtimeModel.inputs[0].shape;
            // NHWC: [batch, height, width, channels] — Get(axis) returns -1 for dynamic dims
            int detectedH = inputShape.Get(1);
            int detectedW = inputShape.Get(2);
            if (detectedH > 1 && detectedW > 1)
            {
                modelInputSize = detectedH; // H == W for MoveNet
                Debug.Log($"PoseInputProvider: Auto-detected model input size: {modelInputSize}x{modelInputSize}");
            }
        }

        _inferenceRT = RenderTexture.GetTemporary(modelInputSize, modelInputSize, 0, RenderTextureFormat.ARGB32);

        _isInitialized = true;
        Debug.Log($"PoseInputProvider: Model loaded ({backend}), input size {modelInputSize}x{modelInputSize}. Ready for inference.");
    }

    private void CleanupModel()
    {
        if (_worker != null)
        {
            _worker.Dispose();
            _worker = null;
        }
        _runtimeModel = null;

        if (_inferenceRT != null)
        {
            RenderTexture.ReleaseTemporary(_inferenceRT);
            _inferenceRT = null;
        }
    }

    // ── Inference ─────────────────────────────────────────────────────────────────

    private void RunInference()
    {
        if (_worker == null || _webcamTexture == null) return;

        // Resize webcam frame to model input size
        Graphics.Blit(_webcamTexture, _inferenceRT);

        // MoveNet expects int32 [1, H, W, 3] in NHWC layout, values 0-255.
        using var floatTensor = new Tensor<float>(new TensorShape(1, modelInputSize, modelInputSize, 3));
        var transform = new TextureTransform().SetTensorLayout(TensorLayout.NHWC);
        TextureConverter.ToTensor(_inferenceRT, floatTensor, transform);

        // Convert float 0-1 → int 0-255
        using var intTensor = new Tensor<int>(new TensorShape(1, modelInputSize, modelInputSize, 3));
        var floatData = floatTensor.DownloadToArray();
        var intData = new int[floatData.Length];
        for (int i = 0; i < floatData.Length; i++)
            intData[i] = Mathf.RoundToInt(Mathf.Clamp01(floatData[i]) * 255f);
        intTensor.Upload(intData);

        _worker.Schedule(intTensor);

        // Read results synchronously
        var output = _worker.PeekOutput() as Tensor<float>;
        if (output == null) return;

        var outputData = output.DownloadToArray();

        // MoveNet output shape: [1, 1, 17, 3] — each keypoint is (y, x, confidence)
        for (int i = 0; i < NUM_KEYPOINTS; i++)
        {
            int offset = i * 3;
            float y    = outputData[offset + 0];
            float x    = outputData[offset + 1];
            float conf = outputData[offset + 2];
            _keypoints[i] = new Vector3(x, y, conf);
        }

        output.Dispose();
    }

    // ── Tracking extraction ───────────────────────────────────────────────────────

    private void ExtractTracking()
    {
        // Check that key body parts are visible
        float hipLConf = _keypoints[LEFT_HIP].z;
        float hipRConf = _keypoints[RIGHT_HIP].z;
        float shlLConf = _keypoints[LEFT_SHOULDER].z;
        float shlRConf = _keypoints[RIGHT_SHOULDER].z;

        bool hipsVisible = hipLConf > minimumConfidence && hipRConf > minimumConfidence;
        bool shouldersVisible = shlLConf > minimumConfidence && shlRConf > minimumConfidence;

        _isTracked = hipsVisible && shouldersVisible;

        if (!_isTracked)
        {
            _isJumping = false;
            _isBaselineSet = false;
            // Hold _positionX at last known value — don't freeze/reset, so the player
            // model keeps its position during brief tracking loss (e.g. during fast motion).
            return;
        }

        // ── Horizontal position: weighted torso centroid ──────────────────────
        // Use 60% hips + 40% shoulders — more stable than hips alone because
        // the average of 4 keypoints cancels out per-joint noise.
        float hipMidX = (_keypoints[LEFT_HIP].x + _keypoints[RIGHT_HIP].x) * 0.5f;
        float shlMidX = (_keypoints[LEFT_SHOULDER].x + _keypoints[RIGHT_SHOULDER].x) * 0.5f;
        _rawPositionX = hipMidX * 0.6f + shlMidX * 0.4f;

        // Pass the torso centroid through directly — smoothing happens once in
        // PlayerMovement.SmoothDamp so we don't stack multiple lag sources.
        _positionX = _rawPositionX;

        // ── Vertical position for jump ───────────────────────────────────────────
        float dt = Time.unscaledDeltaTime;
        _rawShoulderY = 1f - (_keypoints[LEFT_SHOULDER].y + _keypoints[RIGHT_SHOULDER].y) * 0.5f;
        float tY = positionSmoothSpeed * dt;
        _shoulderY = Mathf.Lerp(_shoulderY, _rawShoulderY, Mathf.Clamp01(tY));

        // ── Calibration: measure standing baseline for a few seconds ────────────
        if (!_calibrated)
        {
            _calibrationAccumY += _shoulderY;
            _calibrationSamples++;
            _calibrationTimer += dt;

            if (_calibrationTimer >= calibrationDuration && _calibrationSamples > 0)
            {
                _baselineY = _calibrationAccumY / _calibrationSamples;
                _isBaselineSet = true;
                _calibrated = true;
                _isJumping = false;
                Debug.Log($"[PoseInputProvider] Calibrated baseline Y = {_baselineY:F3}");
            }
            return; // Don't process jumps during calibration
        }

        // ── Jump detection ───────────────────────────────────────────────────────
        // Tick down the cooldown timer
        if (_jumpCooldownTimer > 0f)
            _jumpCooldownTimer -= dt;

        bool wasJumping = _isJumping;
        _isJumping = _shoulderY > (_baselineY + jumpYThreshold);

        // When a jump ends, start the cooldown — prevents the post-jump crouch
        // from dragging the baseline down and making the next jump harder.
        if (wasJumping && !_isJumping)
            _jumpCooldownTimer = jumpCooldown;

        // Only adapt baseline when NOT jumping and the cooldown has expired.
        // Only drift downward (toward resting); never upward during normal standing.
        if (!_isJumping && _jumpCooldownTimer <= 0f)
        {
            if (_shoulderY < _baselineY)
            {
                _baselineY = Mathf.Lerp(_baselineY, _shoulderY,
                                         baselineAdaptSpeed * dt);
            }
        }
    }

    // Update the Y tracking for jump detection only — one smooth step per frame.
    /// <summary>
    /// Resets the jump calibration so the baseline is re-measured.
    /// Call this when the game starts playing.
    /// </summary>
    public void Recalibrate()
    {
        _calibrated = false;
        _calibrationTimer = 0f;
        _calibrationAccumY = 0f;
        _calibrationSamples = 0;
        _isBaselineSet = false;
        _isJumping = false;
        _jumpCooldownTimer = 0f;
        Debug.Log("[PoseInputProvider] Recalibrating...");
    }

    // ── Debug visualisation ───────────────────────────────────────────────────────

    private void UpdateDebugTexture()
    {
        if (_webcamTexture == null || !_webcamHasDeliveredFrame) return;

        int w = _webcamTexture.width;
        int h = _webcamTexture.height;

        // Lazy-init debug texture at actual webcam resolution
        if (_debugTexture == null || _debugTexture.width != w || _debugTexture.height != h)
        {
            if (_debugTexture != null) Destroy(_debugTexture);
            _debugTexture = new Texture2D(w, h, TextureFormat.RGB24, false);
            _debugPixels = new Color32[w * h];
        }

        // Copy raw webcam into debug pixels
        var rawPixels = _webcamTexture.GetPixels32();
        System.Array.Copy(rawPixels, _debugPixels, rawPixels.Length);

        // Draw keypoints and skeleton
        if (_isTracked)
        {
            // Draw bones
            for (int b = 0; b < SkeletonBones.GetLength(0); b++)
            {
                int i1 = SkeletonBones[b, 0];
                int i2 = SkeletonBones[b, 1];
                if (_keypoints[i1].z > minimumConfidence && _keypoints[i2].z > minimumConfidence)
                {
                    DrawLine(w, h,
                        (int)(_keypoints[i1].x * w), (int)(_keypoints[i1].y * h),
                        (int)(_keypoints[i2].x * w), (int)(_keypoints[i2].y * h),
                        new Color32(0, 200, 255, 255));
                }
            }

            // Draw keypoint dots
            for (int i = 0; i < NUM_KEYPOINTS; i++)
            {
                if (_keypoints[i].z > minimumConfidence)
                {
                    int px = (int)(_keypoints[i].x * w);
                    int py = (int)(_keypoints[i].y * h);
                    DrawDot(w, h, px, py, 3, new Color32(255, 50, 50, 255));
                }
            }
        }
        else
        {
            // Not tracked — tint red
            for (int i = 0; i < _debugPixels.Length; i++)
            {
                Color32 c = _debugPixels[i];
                _debugPixels[i] = new Color32(
                    (byte)Mathf.Min(255, c.r / 2 + 80),
                    (byte)(c.g >> 2),
                    (byte)(c.b >> 2),
                    255);
            }
        }

        _debugTexture.SetPixels32(_debugPixels);
        _debugTexture.Apply();
    }

    // Simple Bresenham line drawing into the debug pixel buffer
    private void DrawLine(int w, int h, int x0, int y0, int x1, int y1, Color32 color)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        for (int iter = 0; iter < 1000; iter++) // safety limit
        {
            if (x0 >= 0 && x0 < w && y0 >= 0 && y0 < h)
                _debugPixels[y0 * w + x0] = color;

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    // Draw a small filled circle into the debug pixel buffer
    private void DrawDot(int w, int h, int cx, int cy, int radius, Color32 color)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int px = cx + dx;
            int py = cy + dy;
            if (px >= 0 && px < w && py >= 0 && py < h)
                _debugPixels[py * w + px] = color;
        }
    }
}
