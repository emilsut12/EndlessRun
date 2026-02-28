using UnityEngine;
using Windows.Kinect;

/// <summary>
/// Connects to the Kinect v2 sensor, tracks the player's body, and calculates movement/jump inputs.
/// </summary>
public class KinectInputProvider : MonoBehaviour
{
    // Kinect Sensor and Body tracking variables
    private KinectSensor sensor;
    private BodyFrameReader bodyFrameReader;
    private Body[] bodies = null;

    [Header("Jump Settings")]
    [Tooltip("How much the spine base must rise on the Y axis to trigger a jump (in meters).")]
    public float jumpHeightThreshold = 0.15f;

    // Publicly accessible state variables
    private float currentLeanValue = 0f;
    private bool isPlayerTracked = false;
    private bool isJumping = false;

    // Baseline tracking for jumps
    private float spineBaseBaselineY = 0f;
    private bool isBaselineSet = false;

    private void Start()
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
        }
        else
        {
            Debug.LogWarning("KinectInputProvider: No Kinect v2 sensor found or connected!");
        }
    }

    private void Update()
    {
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
                // By subtracting the spine base X from the head X, we get a lean amount.
                // Positive lean = right, Negative lean = left
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
    public bool IsTracked()
    {
        return isPlayerTracked;
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
}