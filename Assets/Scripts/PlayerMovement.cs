using UnityEngine;
using UnityEngine.SceneManagement;
// 1. This line lets us talk to the Kinect Plugin
using Windows.Kinect;

public class PlayerMovement : MonoBehaviour
{
    bool alive = true;

    public float speed = 10;
    [SerializeField] Rigidbody rb;

    // 2. New variable to control sensitivity
    // A value of 4 means: moving 1 meter in real life moves the player 4 units in the game
    public float movementScale = 4.0f;

    public float speedIncreasePerPoint = 0.1f;

    // 3. Kinect specific variables
    private KinectSensor _sensor;
    private BodyFrameReader _bodyFrameReader;
    private Body[] _bodies = null;

    // This variable calculates where the player SHOULD be based on your body
    private float _targetX = 0f;

    private void Start()
    {
        // 4. Turn on the Kinect Sensor when the game starts
        _sensor = KinectSensor.GetDefault();

        if (_sensor != null)
        {
            _bodyFrameReader = _sensor.BodyFrameSource.OpenReader();

            if (!_sensor.IsOpen)
            {
                _sensor.Open();
            }
        }
    }

    private void Update()
    {
        if (!alive) return;

        // 5. Ask the Kinect for the latest data (Frame)
        if (_bodyFrameReader != null)
        {
            using (BodyFrame frame = _bodyFrameReader.AcquireLatestFrame())
            {
                if (frame != null)
                {
                    if (_bodies == null)
                    {
                        _bodies = new Body[_sensor.BodyFrameSource.BodyCount];
                    }

                    // Refresh the body data
                    frame.GetAndRefreshBodyData(_bodies);

                    // 6. Find the first person the camera sees
                    foreach (var body in _bodies)
                    {
                        if (body.IsTracked)
                        {
                            // Get the X position of your Spine (Center of your body)
                            // We multiply it by movementScale to fit the game lane
                            float spineX = body.Joints[JointType.SpineBase].Position.X;
                            _targetX = spineX * movementScale;
                            break; // Stop after finding one person
                        }
                    }
                }
            }
        }

        // 7. Keep the player within the lane boundaries (-3.5 to 3.5 roughly)
        // If the Kinect loses you, this keeps the player from flying off screen
        _targetX = Mathf.Clamp(_targetX, -3.5f, 3.5f);

        if (transform.position.y < -5)
        {
            Die();
        }
    }

    private void FixedUpdate()
    {
        if (!alive) return;

        // 8. Move the player
        // Forward movement is constant (speed)
        Vector3 forwardMove = transform.forward * speed * Time.fixedDeltaTime;

        // Side movement is now SET to the specific Kinect position (_targetX)
        // rather than adding force like a keyboard
        Vector3 newPosition = rb.position + forwardMove;
        newPosition.x = _targetX;

        rb.MovePosition(newPosition);
    }

    // 9. Clean up: Turn off the Kinect when the game closes
    private void OnApplicationQuit()
    {
        if (_bodyFrameReader != null)
        {
            _bodyFrameReader.Dispose();
            _bodyFrameReader = null;
        }

        if (_sensor != null)
        {
            if (_sensor.IsOpen)
            {
                _sensor.Close();
            }
            _sensor = null;
        }
    }

    public void Die()
    {
        alive = false;
        Invoke("Restart", 2);
    }

    void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}