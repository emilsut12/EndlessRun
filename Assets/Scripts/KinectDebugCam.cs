using UnityEngine;
using UnityEngine.UI;
using Windows.Kinect;

public class KinectDebugCam : MonoBehaviour
{
    // Assign your RawImage here in the Inspector
    public RawImage displayImage;

    private KinectSensor _sensor;
    private ColorFrameSource _colorSource;
    private ColorFrameReader _colorReader;
    private Texture2D _texture;
    private byte[] _data;

    void Start()
    {
        // 1. Activate the Second Screen (if available)
        if (Display.displays.Length > 1)
        {
            Display.displays[1].Activate();
        }

        // 2. Setup Kinect
        _sensor = KinectSensor.GetDefault();

        if (_sensor != null)
        {
            _colorSource = _sensor.ColorFrameSource;
            _colorReader = _colorSource.OpenReader();

            // Create a texture (HD resolution 1920x1080 is standard for Kinect v2)
            var desc = _colorSource.CreateFrameDescription(ColorImageFormat.Rgba);
            _texture = new Texture2D(desc.Width, desc.Height, TextureFormat.RGBA32, false);
            _data = new byte[desc.LengthInPixels * desc.BytesPerPixel];

            // Assign texture to the UI
            if (displayImage != null)
            {
                displayImage.texture = _texture;

                // Fix rotation/flip if needed (Kinect often comes in upsidedown/mirrored)
                displayImage.rectTransform.localScale = new Vector3(1, -1, 1);
            }

            if (!_sensor.IsOpen)
            {
                _sensor.Open();
            }
        }
    }

    void Update()
    {
        if (_colorReader != null)
        {
            var frame = _colorReader.AcquireLatestFrame();
            if (frame != null)
            {
                // Copy the raw Kinect data into our texture
                frame.CopyConvertedFrameDataToArray(_data, ColorImageFormat.Rgba);
                _texture.LoadRawTextureData(_data);
                _texture.Apply();

                frame.Dispose();
            }
        }
    }

    void OnApplicationQuit()
    {
        if (_colorReader != null)
        {
            _colorReader.Dispose();
            _colorReader = null;
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
}