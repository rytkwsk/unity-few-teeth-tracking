using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// トラッキング中に A/B/C/D アンカーを手動で切り替えるオンスクリーン UI。
/// M3TFourAnchorController.ManualOverride を書き換えるとともに、
/// UDP でトラッカー側（port 5006）にも anchor_override コマンドを送信する。
/// "Auto" ボタンを押すと両方をリセットしてトラッカー追従に戻る。
/// </summary>
public class M3TAnchorToggleUI : MonoBehaviour
{
    public M3TFourAnchorController anchorController;

    [Header("Python Tracker Command")]
    public string trackerHost = "127.0.0.1";
    public int    trackerCommandPort = 5006;

    readonly string[] anchorKeys   = { "A", "B", "C", "D" };
    readonly string[] anchorLabels = { "A  Molar", "B  Premolar-Canine", "C  Canine-Lateral", "D  Incisor" };

    static readonly Color ActiveBg   = new Color(0.15f, 0.55f, 0.15f);
    static readonly Color InactiveBg = new Color(0.2f, 0.2f, 0.2f);
    static readonly Color AutoBg     = new Color(0.15f, 0.35f, 0.6f);

    GUIStyle btnStyle;
    Rect windowRect = new Rect(16, 390, 240, 190);

    UdpClient   udp;
    IPEndPoint  trackerEndPoint;

    void Awake()
    {
        if (anchorController == null)
            anchorController = GetComponent<M3TFourAnchorController>();

        try
        {
            udp             = new UdpClient();
            trackerEndPoint = new IPEndPoint(IPAddress.Parse(trackerHost), trackerCommandPort);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"M3TAnchorToggleUI: UDP 初期化失敗: {e.Message}");
        }
    }

    void OnDestroy()
    {
        udp?.Close();
    }

    void SendOverride(string key)
    {
        if (udp == null) return;
        try
        {
            var msg   = $"anchor_override:{key}";
            var bytes = Encoding.UTF8.GetBytes(msg);
            udp.Send(bytes, bytes.Length, trackerEndPoint);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"M3TAnchorToggleUI: 送信失敗: {e.Message}");
        }
    }

    void OnGUI()
    {
        if (anchorController == null) return;
        EnsureStyles();
        windowRect = GUI.Window(GetInstanceID() + 100, windowRect, DrawWindow, "Anchor Select");
    }

    void DrawWindow(int id)
    {
        var isAuto    = string.IsNullOrEmpty(anchorController.ManualOverride);
        var activeKey = isAuto ? anchorController.poseReceiver?.ActiveAnchor : anchorController.ManualOverride;

        // Auto ボタン
        GUI.backgroundColor = isAuto ? AutoBg : InactiveBg;
        if (GUILayout.Button(isAuto ? "Auto  (tracking)" : "Auto", btnStyle, GUILayout.Height(30)))
        {
            anchorController.ManualOverride = "";
            SendOverride("");
        }

        GUILayout.Space(4);

        // A/B/C/D ボタン
        for (int i = 0; i < anchorKeys.Length; i++)
        {
            var isSelected = !isAuto && anchorKeys[i] == anchorController.ManualOverride;
            GUI.backgroundColor = isSelected ? ActiveBg : InactiveBg;
            if (GUILayout.Button(anchorLabels[i], btnStyle, GUILayout.Height(28)))
            {
                anchorController.ManualOverride = anchorKeys[i];
                SendOverride(anchorKeys[i]);
            }
        }

        GUI.backgroundColor = Color.white;
        GUI.DragWindow(new Rect(0, 0, 10000, 22));
    }

    void EnsureStyles()
    {
        if (btnStyle != null) return;
        btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };
    }
}
