using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
    // トラッカーが M3T に渡している内部パラメータ [fx, fy, cx, cy] と画像サイズ [w, h]。
    // M3TCameraIntrinsics がこれに合わせて投影行列を作る。旧トラッカーからは来ない。
    public float[] K;
    public int[] image_size;
    // ArUco 基準との実測誤差（tracker_four_group.py が送る。旧トラッカーからは来ず既定値のまま）。
    public bool marker_registered;
    public bool marker_visible;
    public bool marker_valid;
    public float marker_err_mm;
    public float marker_err_deg;
    public int marker_samples;
    public string marker_msg;
    // 回転不確か警告: 統合（回転固定）が長い／歯列が大きく動いた／HOLD 中／F 復帰直後（tracker_four_group.py）。
    public bool rotation_uncertain;
    public string rotation_uncertain_reason;
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
    // トラッカーの内部パラメータ [fx, fy, cx, cy] と画像サイズ [w, h]。未受信なら null。
    // 値が変わるたびに IntrinsicsVersion が増える（M3TCameraIntrinsics が変化を検知する）。
    public float[] CameraK { get; private set; }
    public int[] ImageSize { get; private set; }
    public int IntrinsicsVersion { get; private set; }
    // ArUco 基準との実測誤差。MarkerValid のときだけ ErrMm/ErrDeg が意味を持つ。
    public bool MarkerRegistered { get; private set; }
    public bool MarkerVisible { get; private set; }
    public bool MarkerValid { get; private set; }
    public float MarkerErrMm { get; private set; }
    public float MarkerErrDeg { get; private set; }
    public int MarkerSamples { get; private set; }
    public string MarkerMessage { get; private set; } = "";
    // 出力姿勢の回転が古い可能性があるとき true（遮蔽中は回転を固定しているため）。理由は表示用の英文。
    public bool RotationUncertain { get; private set; }
    public string RotationUncertainReason { get; private set; } = "";

    // bind に失敗しても無効化せず、この間隔で再試行する（ポートを掴んでいた古いソケットが解放されれば自動復旧）。
    const float BindRetryIntervalSec = 1f;
    float nextBindAttemptTime;
    bool bindErrorLogged;

    void Start()
    {
        if (target == null)
            target = transform;
        TryStartReceiver();
    }

    void OnEnable()
    {
#if UNITY_EDITOR
        // Play 中にスクリプトが再コンパイルされると（Preferences の Script Changes While Playing が
        // "Recompile And Continue Playing" の場合）、OnDisable を経ずに udpClient の参照だけが失われ、
        // ソケットがエディタのプロセス内でポートを掴んだまま残る（2026-10-09: 以降の Play が全て
        // "could not listen on UDP port 5005" になった）。再読み込み直前に必ず閉じる。
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopReceiver;
#endif
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);
    const uint HANDLE_FLAG_INHERIT = 0x1;
#endif

    // M3TProcessController は Python トラッカーを UseShellExecute=false（＝ハンドル継承あり）で起動するため、
    // このソケットのハンドルがトラッカー（cmd/python）に複製される。トラッカーが終了処理で固まって残ると
    // （2026-10-09: カメラドライバ待ちで HasExited=True のまま消えず、PC 再起動まで解放されなかった）、
    // 複製ハンドルがポート 5005 を掴み続け、以降の Play が全て bind に失敗した。継承不可にして断ち切る。
    static void MakeNonInheritable(Socket socket)
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try
        {
            if (!SetHandleInformation(socket.Handle, HANDLE_FLAG_INHERIT, 0))
                Debug.LogWarning($"M3TPoseReceiver: SetHandleInformation failed ({Marshal.GetLastWin32Error()}); socket may be inherited by the tracker process.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"M3TPoseReceiver: could not make the socket non-inheritable: {e.Message}");
        }
#endif
    }

    bool TryStartReceiver()
    {
        try
        {
            udpClient = new UdpClient(port);
            MakeNonInheritable(udpClient.Client);
        }
        catch (SocketException e)
        {
            udpClient = null;
            nextBindAttemptTime = Time.unscaledTime + BindRetryIntervalSec;
            if (!bindErrorLogged)
            {
                Debug.LogError($"M3TPoseReceiver could not listen on UDP port {port} (retrying every {BindRetryIntervalSec:0} s). " +
                               $"If this persists, another Unity play session/app holds the port; restarting the Unity Editor frees it. {e.Message}");
                bindErrorLogged = true;
            }
            return false;
        }

        if (bindErrorLogged)
            Debug.Log($"M3TPoseReceiver: listening on UDP port {port} (recovered).");
        bindErrorLogged = false;
        running = true;
        receiveThread = new Thread(ReceiveLoop);
        receiveThread.IsBackground = true;
        receiveThread.Start();
        return true;
    }

    void Update()
    {
        // Start 時の bind 失敗、またはスクリプト再読み込み後（フィールドが失われ Start は呼ばれない）に受信を再開する
        if (udpClient == null && Time.unscaledTime >= nextBindAttemptTime)
            TryStartReceiver();

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
        if (packet != null && packet.K != null && packet.K.Length == 4
            && packet.image_size != null && packet.image_size.Length == 2
            && !SameIntrinsics(packet.K, packet.image_size))
        {
            CameraK = packet.K;
            ImageSize = packet.image_size;
            IntrinsicsVersion++;
        }
        if (packet != null)
        {
            MarkerRegistered = packet.marker_registered;
            MarkerVisible = packet.marker_visible;
            MarkerValid = packet.marker_valid;
            MarkerErrMm = packet.marker_err_mm;
            MarkerErrDeg = packet.marker_err_deg;
            MarkerSamples = packet.marker_samples;
            MarkerMessage = packet.marker_msg ?? "";
            RotationUncertain = packet.rotation_uncertain;
            RotationUncertainReason = packet.rotation_uncertain_reason ?? "";
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

    bool SameIntrinsics(float[] k, int[] size)
    {
        if (CameraK == null || ImageSize == null)
            return false;
        if (size[0] != ImageSize[0] || size[1] != ImageSize[1])
            return false;
        for (int i = 0; i < 4; i++)
            if (Mathf.Abs(k[i] - CameraK[i]) > 1e-3f)
                return false;
        return true;
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
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopReceiver;
#endif
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

