using UnityEngine;
using System;
using Windows.Kinect;

/// <summary>
/// Connects to the Kinect v2 sensor, tracks the player's body, and calculates movement/jump inputs.
/// </summary>
public class KinectInputProvider : MotionInputProvider
{
    // Kinect Sensor and Body tracking variables
    private KinectSensor sensor;
    private BodyFrameReader bodyFrameReader;
    public BodySourceManager bodyManager;
    private Body[] bodies = null;

    [Header("Jump Settings")]
    [Tooltip("How much the spine base must rise on the Y axis to trigger a jump (in meters).")]
    public float jumpHeightThreshold = 0.15f;

    [Header("UI Feedback")]
    [Tooltip("Optional: A UI Text or Panel that activates if the Kinect runtime is missing.")]
    public GameObject kinectWarningUI;

    // Publicly accessible state variables
    public bool IsKinectInitialized { get; private set; } = false;
    private float currentLeanValue = 0f;
    private bool isPlayerTracked = false;
    private bool isJumping = false;

    public override string ProviderName => "Kinect";
    public override bool IsProviderAvailable => IsKinectInitialized;

    // Baseline tracking for jumps
    private float spineBaseBaselineY = 0f;
    private bool isBaselineSet = false;

    private void Start()
    {
        // Make sure the warning is hidden by default
        if (kinectWarningUI != null) kinectWarningUI.SetActive(false);

        // Attempt to initialize safely
        InitializeKinectSafe();
    }

    // We put this in a separate method to prevent the JIT compiler from crashing 
    // the script before it can even read the try-catch block.
    private void InitializeKinectSafe()
    {
        try
        {
            // Initialize Kinect Sensor
            sensor = KinectSensor.GetDefault();

            if (sensor != null)
            {
                bodyFrameReader = sensor.BodyFrameSource.OpenReader();

                if (!sensor.IsOpen)
                {
                    sensor.Open();
                }

                IsKinectInitialized = true;
                Debug.Log("Kinect initialized successfully.");
            }
            else
            {
                Debug.LogWarning("KinectInputProvider: No Kinect v2 sensor found or connected!");
                IsKinectInitialized = false;
            }
        }
        catch (DllNotFoundException)
        {
            Debug.LogWarning("Kinect Runtime is not installed on this machine. Kinect input disabled.");
            IsKinectInitialized = false;
            if (kinectWarningUI != null) kinectWarningUI.SetActive(true);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Failed to initialize Kinect: " + e.Message);
            IsKinectInitialized = false;
            if (kinectWarningUI != null) kinectWarningUI.SetActive(true);
        }
    }

    private void Update()
    {
        // SAFEGUARD: Do not run any Kinect code if initialization failed
        if (!IsKinectInitialized) return;

        if (bodyFrameReader != null)
        {
            using (BodyFrame frame = bodyFrameReader.AcquireLatestFrame())
            {
                if (frame != null)
                {
                    if (bodies == null)
                    {
                        bodies = new Body[sensor.BodyFrameSource.BodyCount];
                    }

                    frame.GetAndRefreshBodyData(bodies);
                    ProcessBodyData();
                }
            }
        }
    }

    public float GetPlayerRealWorldX()
    {
        // SAFEGUARD
        if (!IsKinectInitialized || bodyManager == null) return 0f;

        Windows.Kinect.Body[] data = bodyManager.GetData();
        if (data == null) return 0f;

        foreach (var body in data)
        {
            if (body != null && body.IsTracked)
            {
                // Returns the physical X position (in meters) relative to the camera center
                return body.Joints[Windows.Kinect.JointType.SpineBase].Position.X;
            }
        }
        return 0f;
    }

    /// <summary>
    /// Processes the tracked bodies to calculate lean and jump state.
    /// </summary>
    private void ProcessBodyData()
    {
        isPlayerTracked = false;
        isJumping = false;

        foreach (Body body in bodies)
        {
            if (body.IsTracked)
            {
                isPlayerTracked = true;

                // Fetch key joints for calculation
                Windows.Kinect.Joint spineBase = body.Joints[JointType.SpineBase];
                Windows.Kinect.Joint head = body.Joints[JointType.Head];

                // 1. Calculate Lean (Horizontal movement)
                if (spineBase.TrackingState == TrackingState.Tracked && head.TrackingState == TrackingState.Tracked)
                {
                    currentLeanValue = head.Position.X - spineBase.Position.X;
                }

                // 2. Calculate Jump (Vertical movement)
                if (spineBase.TrackingState == TrackingState.Tracked)
                {
                    if (!isBaselineSet)
                    {
                        // Set the initial height of the player when they step into frame
                        spineBaseBaselineY = spineBase.Position.Y;
                        isBaselineSet = true;
                    }
                    else
                    {
                        // Check if the current Y position exceeds the baseline + our threshold
                        if (spineBase.Position.Y > (spineBaseBaselineY + jumpHeightThreshold))
                        {
                            isJumping = true;
                        }

                        // Slowly adjust the baseline over time to account for different players 
                        // or shifting postures, so they don't get stuck in a "jumping" state
                        spineBaseBaselineY = Mathf.Lerp(spineBaseBaselineY, spineBase.Position.Y, Time.deltaTime * 2f);
                    }
                }

                // We only track the first valid body we find, so break out of the loop
                break;
            }
        }

        // Clean up data if the player walks out of frame mid-game
        if (!isPlayerTracked)
        {
            isBaselineSet = false;
            currentLeanValue = 0f;
        }
    }

    private void OnDestroy()
    {
        // SAFEGUARD: Only close if we successfully opened it
        if (!IsKinectInitialized) return;

        // Always clean up the Kinect sensor when the game stops
        if (bodyFrameReader != null)
        {
            bodyFrameReader.Dispose();
            bodyFrameReader = null;
        }

        if (sensor != null)
        {
            if (sensor.IsOpen)
            {
                sensor.Close();
            }
            sensor = null;
        }
    }

    // PUBLIC API FOR INPUT MANAGER

    /// <summary>
    /// Returns true if the Kinect is currently tracking a player.
    /// </summary>
    public override bool IsTracked()
    {
        if (!IsKinectInitialized) return false;
        return isPlayerTracked;
    }

    public override bool TryGetHorizontalPosition(out float positionX, out MotionHorizontalSpace positionSpace)
    {
        positionX = 0f;
        positionSpace = MotionHorizontalSpace.RealWorldMeters;

        if (!IsKinectInitialized || !IsTracked())
            return false;

        positionX = GetPlayerRealWorldX();
        return true;
    }

    /// <summary>
    /// Returns the calculated horizontal lean value.
    /// </summary>
    public float GetLeanValue()
    {
        return currentLeanValue;
    }

    /// <summary>
    /// Returns true if the player is currently jumping.
    /// </summary>
    public bool IsJumping()
    {
        return isJumping;
    }

    public override bool GetJumpInput()
    {
        return IsJumping();
    }
}