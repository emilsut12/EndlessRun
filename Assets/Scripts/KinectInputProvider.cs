using UnityEngine;
using Windows.Kinect;

public class KinectInputProvider : MonoBehaviour
{
    private KinectSensor _sensor;
    private BodyFrameReader _bodyFrameReader;
    private Body[] _bodies = null;

    [Header("Jump Settings")]
    [Tooltip("How high (in meters) hips must rise to trigger a jump.")]
    public float jumpThreshold = 0.08f;

    // Track the lowest height the hips have been recently
    private float _baselineHipHeight = 0f;
    private ulong _currentTrackingId = 0;

    void Start()
    {
        _sensor = KinectSensor.GetDefault();
        if (_sensor != null)
        {
            _bodyFrameReader = _sensor.BodyFrameSource.OpenReader();
            if (!_sensor.IsOpen) _sensor.Open();
        }
    }

    void Update()
    {
        if (_bodyFrameReader != null)
        {
            using (BodyFrame frame = _bodyFrameReader.AcquireLatestFrame())
            {
                if (frame != null)
                {
                    if (_bodies == null) _bodies = new Body[_sensor.BodyFrameSource.BodyCount];
                    frame.GetAndRefreshBodyData(_bodies);

                    bool foundBody = false;
                    foreach (var body in _bodies)
                    {
                        if (body.IsTracked)
                        {
                            foundBody = true;
                            ProcessBody(body);
                            break;
                        }
                    }

                    if (!foundBody)
                    {
                        InputManager.Instance.SetKinectInactive();
                        // Reset ID to recalibrate when they come back
                        _currentTrackingId = 0;
                    }
                }
            }
        }
    }

    void ProcessBody(Body body)
    {
        // New Person Detected? Calibrate!
        if (body.TrackingId != _currentTrackingId)
        {
            _currentTrackingId = body.TrackingId;
            // Set baseline to current height
            _baselineHipHeight = body.Joints[JointType.SpineBase].Position.Y;
        }

        float currentHipY = body.Joints[JointType.SpineBase].Position.Y;
        float currentHipX = body.Joints[JointType.SpineBase].Position.X;

        // Dynamic Calibration
        // If they squat down or are shorter than we thought, lower the baseline.
        // This ensures the "floor" is always the lowest point they've reached.
        if (currentHipY < _baselineHipHeight)
        {
            _baselineHipHeight = currentHipY;
        }

        // Check for Jump
        // If current height > baseline + threshold
        if (currentHipY > _baselineHipHeight + jumpThreshold)
        {
            InputManager.Instance.SetKinectJump(true);
        }

        // Send Movement
        InputManager.Instance.SetKinectInput(currentHipX);
    }

    void OnApplicationQuit()
    {
        if (_bodyFrameReader != null) _bodyFrameReader.Dispose();
        if (_sensor != null && _sensor.IsOpen) _sensor.Close();
    }
}