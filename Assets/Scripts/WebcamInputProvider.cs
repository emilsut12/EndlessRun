using System.Collections;
using UnityEngine;

/// <summary>
/// Provides a live webcam texture feed for display on the debug monitor.
/// Background-subtraction centroid tracking has been removed; use PoseInputProvider
/// for webcam-based player tracking instead.
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

    // ── Internal state ────────────────────────────────────────────────────────────
    private WebCamTexture _webcamTexture;
    private bool _isInitialized = false;

    // ── MotionInputProvider API ───────────────────────────────────────────────────
    public override string ProviderName => "Webcam";

    /// <summary>True as soon as the WebCamTexture is playing — used by the debug renderer.</summary>
    public bool IsWebcamRunning =>
        _isInitialized && _webcamTexture != null && _webcamTexture.isPlaying;

    /// <summary>The raw WebCamTexture — available immediately after the webcam starts.</summary>
    public Texture GetRawTexture() => _webcamTexture;

    // Centroid tracking removed. Use PoseInputProvider for webcam-based player tracking.
    public override bool IsProviderAvailable => false;
    public override bool IsTracked() => false;

    public override bool TryGetHorizontalPosition(out float positionX, out MotionHorizontalSpace positionSpace)
    {
        positionX     = 0.5f;
        positionSpace = MotionHorizontalSpace.NormalizedScreenX;
        return false;
    }

    public override bool GetJumpInput() => false;
    public override Texture GetDebugTexture() => null;

    // ── Lifecycle ─────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        StartCoroutine(InitializeWebcamDelayed());
    }

    private IEnumerator InitializeWebcamDelayed()
    {
        // Wait one frame so the OS has time to release the device handle
        // from any previous provider that was just destroyed/disabled.
        yield return null;
        TryInitializeWebcam();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        StopAndReleaseWebcam();
    }

    private void OnDestroy()
    {
        StopAndReleaseWebcam();
    }

    private void StopAndReleaseWebcam()
    {
        if (_webcamTexture != null)
        {
            if (_webcamTexture.isPlaying) _webcamTexture.Stop();
            Destroy(_webcamTexture);
            _webcamTexture = null;
        }
        _isInitialized = false;
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
        _isInitialized = true;
        Debug.Log($"WebcamInputProvider: Started '{devices[idx].name}' ({captureWidth}x{captureHeight}).");
    }

    // ── Public utilities ──────────────────────────────────────────────────────────

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
}
