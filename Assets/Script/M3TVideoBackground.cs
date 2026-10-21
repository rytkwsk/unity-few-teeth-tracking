using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class M3TVideoBackground : MonoBehaviour
{
    public Camera targetCamera;
    public string host = "127.0.0.1";
    public int port = 5010;
    public float distance = 4.5f;
    public bool connectOnStart = true;
    public bool flipX = false;
    public bool flipY = false;

    M3TCameraIntrinsics intrinsics;
    Texture2D texture;
    Material material;
    GameObject quad;
    Thread receiveThread;
    TcpClient client;
    volatile bool running;
    readonly object imageLock = new object();
    byte[] latestImage;
    string status = "Stopped";
    int receivedFrames;
    int textureWidth;
    int textureHeight;

    public string Status => textureWidth > 0 ? $"{status} {textureWidth}x{textureHeight} #{receivedFrames}" : status;
    // 最後に映像を貼った時刻（Time.unscaledTime）。M3TDemoGuide が「カメラ起動中」を出すのに使う
    public float LastFrameTime { get; private set; } = -1f;

    void OnEnable()
    {
        // OnEnable is also called after a script recompilation while the Editor
        // remains in Play mode. OnDisable stops the receive thread, so restart
        // it here instead of leaving the Game view at "Video: Stopped".
        if (Application.isPlaying && connectOnStart && !running)
            StartVideo();
    }

    void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera != null)
            intrinsics = targetCamera.GetComponent<M3TCameraIntrinsics>();

        EnsureBackground();

        if (connectOnStart && !running)
            StartVideo();
    }

    void Update()
    {
        ApplyLatestImage();
        UpdateBackgroundTransform();
    }

    public void StartVideo()
    {
        if (running)
            return;

        running = true;
        status = "Connecting";
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();
    }

    public void StopVideo()
    {
        running = false;

        try
        {
            client?.Close();
        }
        catch (Exception)
        {
        }

        if (receiveThread != null && receiveThread.IsAlive)
            receiveThread.Join(100);

        receiveThread = null;
        client = null;
        status = "Stopped";
    }

    void ReceiveLoop()
    {
        while (running)
        {
            try
            {
                using (client = new TcpClient())
                {
                    client.Connect(host, port);
                    status = "Receiving";

                    using (var stream = client.GetStream())
                    {
                        while (running)
                        {
                            var lengthBytes = ReadExactly(stream, 4);
                            if (lengthBytes == null)
                                break;

                            var length =
                                (lengthBytes[0] << 24) |
                                (lengthBytes[1] << 16) |
                                (lengthBytes[2] << 8) |
                                lengthBytes[3];

                            if (length <= 0 || length > 10 * 1024 * 1024)
                                break;

                            var imageBytes = ReadExactly(stream, length);
                            if (imageBytes == null)
                                break;

                            lock (imageLock)
                            {
                                latestImage = imageBytes;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                status = $"Waiting: {e.Message}";
                Thread.Sleep(500);
            }
        }
    }

    static byte[] ReadExactly(NetworkStream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = stream.Read(buffer, offset, length - offset);
            if (read <= 0)
                return null;
            offset += read;
        }

        return buffer;
    }

    void ApplyLatestImage()
    {
        byte[] imageBytes = null;
        lock (imageLock)
        {
            if (latestImage != null)
            {
                imageBytes = latestImage;
                latestImage = null;
            }
        }

        if (imageBytes == null)
            return;

        if (texture == null)
        {
            texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            material.mainTexture = texture;
        }

        texture.LoadImage(imageBytes, false);
        receivedFrames++;
        LastFrameTime = Time.unscaledTime;
        textureWidth = texture.width;
        textureHeight = texture.height;
    }

    void EnsureBackground()
    {
        if (quad != null || targetCamera == null)
            return;

        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "M3T Video Background";
        quad.transform.SetParent(targetCamera.transform, false);

        var collider = quad.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        material = new Material(Shader.Find("M3T/VideoBackground"));
        material.renderQueue = 2900;
        quad.GetComponent<MeshRenderer>().sharedMaterial = material;

        UpdateBackgroundTransform();
    }

    void UpdateBackgroundTransform()
    {
        if (quad == null || targetCamera == null)
            return;

        var safeDistance = Mathf.Min(distance, targetCamera.farClipPlane * 0.95f);
        quad.transform.localPosition = new Vector3(0f, 0f, safeDistance);
        quad.transform.localRotation = Quaternion.identity;

        if (intrinsics == null)
            intrinsics = targetCamera.GetComponent<M3TCameraIntrinsics>();

        if (intrinsics != null && intrinsics.fx > 0f && intrinsics.fy > 0f)
        {
            var left = -intrinsics.cx / intrinsics.fx * safeDistance;
            var right = (intrinsics.imageWidth - intrinsics.cx) / intrinsics.fx * safeDistance;
            var bottom = -(intrinsics.imageHeight - intrinsics.cy) / intrinsics.fy * safeDistance;
            var top = intrinsics.cy / intrinsics.fy * safeDistance;

            quad.transform.localPosition = new Vector3((left + right) * 0.5f, (bottom + top) * 0.5f, safeDistance);
            quad.transform.localScale = new Vector3(
                flipX ? -(right - left) : right - left,
                flipY ? -(top - bottom) : top - bottom,
                1f
            );
            return;
        }

        var projection = targetCamera.projectionMatrix;
        var height = 2f * safeDistance / Mathf.Abs(projection[1, 1]);
        var imageAspect = texture != null && texture.height > 0
            ? texture.width / (float)texture.height
            : targetCamera.aspect;
        var width = height * imageAspect;
        quad.transform.localScale = new Vector3(flipX ? -width : width, flipY ? -height : height, 1f);
    }

    void OnDisable()
    {
        StopVideo();
    }

    void OnDestroy()
    {
        StopVideo();
    }
}
