using UnityEngine;
using Windows.Kinect;

public class KinectInputProvider : MonoBehaviour
{
    private KinectSensor _sensor;
    private BodyFrameReader _bodyFrameReader;
    private Body[] _bodies = null;

    [Header("Jump Settings")]
    [Tooltip("How high (meters) hips must rise. Try 0.1 (10cm).")]
    public float heightThreshold = 0.1f;

    [Tooltip("How fast (m/s) hips must move up. Try 0.5. If too sensitive, increase to 0.8.")]
    public float velocityThreshold = 0.5f;

    public float jumpCooldown = 1.0f;
    private float _timeSinceLastJump = 1.0f; // Start ready to jump

    // Calibration
    private float _baselineHipHeight = 0f;
    private float _lastHipY = 0f;
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
        _timeSinceLastJump += Time.deltaTime;

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
                        _currentTrackingId = 0;
                    }
                }
            }
        }
    }

    void ProcessBody(Body body)
    {
        float currentHipY = body.Joints[JointType.SpineBase].Position.Y;
        float currentHipX = body.Joints[JointType.SpineBase].Position.X;

        // 1. Calibration
        if (body.TrackingId != _currentTrackingId)
        {
            _currentTrackingId = body.TrackingId;
            _baselineHipHeight = currentHipY;
            _lastHipY = currentHipY;
        }

        // Lower baseline if crouching
        if (currentHipY < _baselineHipHeight)
        {
            _baselineHipHeight = currentHipY;
        }

        // 2. Calculate Velocity
        float velocity = (currentHipY - _lastHipY) / Time.deltaTime;

        // 3. Check for Jump
        if (_timeSinceLastJump > jumpCooldown)
        {
            bool heightMet = currentHipY > (_baselineHipHeight + heightThreshold);
            bool speedMet = velocity > velocityThreshold;

            // Debugging: Uncomment this if you can't get it to work
            // Debug.Log($"Vel: {velocity:F2} / HeightDiff: {currentHipY - _baselineHipHeight:F2}");

            if (heightMet && speedMet)
            {
                Debug.Log("KINECT JUMP DETECTED!"); // Check Console for this
                InputManager.Instance.SetKinectJump(true);
                _timeSinceLastJump = 0f;
            }
        }

        InputManager.Instance.SetKinectInput(currentHipX);
        _lastHipY = currentHipY;
    }

    void OnApplicationQuit()
    {
        if (_bodyFrameReader != null) _bodyFrameReader.Dispose();
        if (_sensor != null && _sensor.IsOpen) _sensor.Close();
    }
}