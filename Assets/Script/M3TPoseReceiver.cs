using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

[Serializable]
public class M3TPosePacket
{
    public string name;
    public double timestamp;
    public string mode;
    public float[] pose_cv;
}

public class M3TPoseReceiver : MonoBehaviour
{
    [Header("UDP")]
    public int port = 5005;

    [Header("Target")]
    public Transform target;
    public bool applyPose = true;

    [Header("Coordinate Conversion")]
    public bool convertOpenCvToUnity = true;
    public Vector3 positionScale = Vector3.one;
    public Vector3 positionOffset = Vector3.zero;
    public Vector3 rotationOffsetEuler = Vector3.zero;

    UdpClient udpClient;
    Thread receiveThread;
    volatile bool running;
    readonly object packetLock = new object();
    M3TPosePacket latestPacket;

    void Start()
    {
        if (target == null)
            target = transform;

        try
        {
            udpClient = new UdpClient(port);
        }
        catch (SocketException e)
        {
            Debug.LogError($"M3TPoseReceiver could not listen on UDP port {port}. Stop any other running Unity play session or app using this port, then play again. {e.Message}");
            enabled = false;
            return;
        }

        running = true;
        receiveThread = new Thread(ReceiveLoop);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    void Update()
    {
        M3TPosePacket packet = null;
        lock (packetLock)
        {
            packet = latestPacket;
            latestPacket = null;
        }

        if (!applyPose || packet == null || packet.pose_cv == null)
            return;

        if (!TryBuildUnityPose(packet.pose_cv, out var position, out var rotation))
            return;

        position = Vector3.Scale(position, positionScale) + positionOffset;
        // Applied in local (object) space -- i.e. as a fixed relabeling of the
        // mesh's own axes -- rather than world space. A world-space (left-
        // multiplied) offset whose axis doesn't commute with the live tracked
        // rotation inverts the apparent rotation direction for that axis
        // (e.g. an X-axis offset flips the perceived direction of Z-axis
        // spin). A local-space (right-multiplied) fixed offset can't do that:
        // it only relabels which physical mesh axis corresponds to which
        // tracked axis, so live rotation direction is preserved on every axis.
        rotation = rotation * Quaternion.Euler(rotationOffsetEuler);

        target.SetPositionAndRotation(position, rotation);
    }

    void ReceiveLoop()
    {
        var endpoint = new IPEndPoint(IPAddress.Any, port);
        while (running)
        {
            try
            {
                var data = udpClient.Receive(ref endpoint);
                var json = Encoding.UTF8.GetString(data);
                var packet = JsonUtility.FromJson<M3TPosePacket>(json);
                lock (packetLock)
                {
                    latestPacket = packet;
                }
            }
            catch (SocketException)
            {
                if (running)
                    Debug.LogWarning("M3TPoseReceiver socket stopped unexpectedly.");
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception e)
            {
                if (running)
                    Debug.LogWarning($"M3TPoseReceiver parse error: {e.Message}");
            }
        }
    }

    bool TryBuildUnityPose(float[] poseCv, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (poseCv.Length < 16)
            return false;

        var r = new Matrix4x4();
        r.SetColumn(0, new Vector4(poseCv[0], poseCv[4], poseCv[8], 0));
        r.SetColumn(1, new Vector4(poseCv[1], poseCv[5], poseCv[9], 0));
        r.SetColumn(2, new Vector4(poseCv[2], poseCv[6], poseCv[10], 0));
        r.SetColumn(3, new Vector4(0, 0, 0, 1));

        position = new Vector3(poseCv[3], poseCv[7], poseCv[11]);

        if (convertOpenCvToUnity)
        {
            position = new Vector3(position.x, -position.y, position.z);

            var cvToUnity = Matrix4x4.Scale(new Vector3(1, -1, 1));
            r = cvToUnity * r * cvToUnity;
        }

        rotation = RotationFromMatrix(r);
        return true;
    }

    static Quaternion RotationFromMatrix(Matrix4x4 m)
    {
        // Direct 3x3 rotation-matrix -> quaternion (Shepperd's method).
        // Quaternion.LookRotation(forward, upwards) silently misbehaves when
        // forward/upwards become nearly parallel -- easy to hit transiently
        // under fast/large tracked rotations -- which froze rotation updates
        // while position kept updating normally.
        float m00 = m.m00, m01 = m.m01, m02 = m.m02;
        float m10 = m.m10, m11 = m.m11, m12 = m.m12;
        float m20 = m.m20, m21 = m.m21, m22 = m.m22;

        float trace = m00 + m11 + m22;
        float qw, qx, qy, qz;

        if (trace > 0f)
        {
            float s = 0.5f / Mathf.Sqrt(trace + 1.0f);
            qw = 0.25f / s;
            qx = (m21 - m12) * s;
            qy = (m02 - m20) * s;
            qz = (m10 - m01) * s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            float s = 2.0f * Mathf.Sqrt(1.0f + m00 - m11 - m22);
            qw = (m21 - m12) / s;
            qx = 0.25f * s;
            qy = (m01 + m10) / s;
            qz = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            float s = 2.0f * Mathf.Sqrt(1.0f + m11 - m00 - m22);
            qw = (m02 - m20) / s;
            qx = (m01 + m10) / s;
            qy = 0.25f * s;
            qz = (m12 + m21) / s;
        }
        else
        {
            float s = 2.0f * Mathf.Sqrt(1.0f + m22 - m00 - m11);
            qw = (m10 - m01) / s;
            qx = (m02 + m20) / s;
            qy = (m12 + m21) / s;
            qz = 0.25f * s;
        }

        return new Quaternion(qx, qy, qz, qw).normalized;
    }

    void OnDisable()
    {
        StopReceiver();
    }

    void OnDestroy()
    {
        StopReceiver();
    }

    void StopReceiver()
    {
        running = false;

        udpClient?.Close();
        udpClient = null;

        if (receiveThread != null && receiveThread.IsAlive)
            receiveThread.Join(100);

        receiveThread = null;
    }
}

