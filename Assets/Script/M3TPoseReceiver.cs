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
    // Anchor Switching (tracker_anchor_switching.py) が送る場合のみ入る。
    // 単一モデルのトラッカー(tracker_premolar_canine.py 等)からは常に null/空。
    public string active_anchor;
    // 階層アンカー版 tracker_four_group.py が送る: 実際に姿勢を出しているアンカー（"F"=歯列全体, "A".."D"）と
    // 各アンカーの適合度 [F, A, B, C, D]（0..1）。旧トラッカーからは来ない。
    public string pose_anchor;
    public float[] quality;
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
    // 既定 (0,0,180) の根拠 (2026-09-24):
    //   convertOpenCvToUnity は Y 反転 S=diag(1,-1,1) をカメラ側・モデル側の両方に掛ける (S R S)。
    //   一方 Unity の OBJ インポートはメッシュの X を反転する (右手系→左手系)。
    //   モデル側に必要なのは diag(-1,1,1) なので、差分 diag(1,-1,1)·diag(-1,1,1) = diag(-1,-1,1) = Z 軸 180° を
    //   ローカル側に右から掛ける。トラッカーの投影と Unity の投影が画素単位で一致することを確認済み。
    //   (180,180,180) は 3 軸 180° の積 = 恒等回転なので「オフセットなし」と同じ。
    public Vector3 rotationOffsetEuler = new Vector3(0f, 0f, 180f);

    UdpClient udpClient;
    Thread receiveThread;
    volatile bool running;
    readonly object packetLock = new object();
    M3TPosePacket latestPacket;

    // Anchor Switching 用: 直近に受信した active_anchor ("A"/"B")。
    // 単一モデル用トラッカーからはこのフィールドが来ないので既定値のまま。
    public string ActiveAnchor { get; private set; } = "A";
    public string TrackingMode { get; private set; } = "Waiting";
    public string PoseAnchor { get; private set; } = "";
    // [F, A, B, C, D] の順。未受信なら長さ 0。
    public float[] Quality { get; private set; } = new float[0];

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

        if (packet != null && !string.IsNullOrEmpty(packet.active_anchor))
            ActiveAnchor = packet.active_anchor;
        if (packet != null && !string.IsNullOrEmpty(packet.mode))
            TrackingMode = packet.mode;
        if (packet != null && !string.IsNullOrEmpty(packet.pose_anchor))
            PoseAnchor = packet.pose_anchor;
        if (packet != null && packet.quality != null && packet.quality.Length > 0)
            Quality = packet.quality;

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

