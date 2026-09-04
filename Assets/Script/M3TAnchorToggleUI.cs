using UnityEngine;

/// <summary>
/// トラッキング中に A/B/C/D アンカーを手動で切り替えるオンスクリーン UI。
/// M3TFourAnchorController.ManualOverride を書き換えることで切り替える。
/// "Auto" ボタンを押すと ManualOverride を空文字にしてトラッカー追従に戻る。
/// </summary>
public class M3TAnchorToggleUI : MonoBehaviour
{
    public M3TFourAnchorController anchorController;

    readonly string[] anchorKeys   = { "A", "B", "C", "D" };
    readonly string[] anchorLabels = { "A  Molar", "B  Premolar-Canine", "C  Canine-Lateral", "D  Incisor" };

    static readonly Color ActiveBg   = new Color(0.15f, 0.55f, 0.15f);
    static readonly Color InactiveBg = new Color(0.2f, 0.2f, 0.2f);
    static readonly Color AutoBg     = new Color(0.15f, 0.35f, 0.6f);

    GUIStyle btnStyle;
    Rect windowRect = new Rect(16, 390, 240, 190);

    void Awake()
    {
        if (anchorController == null)
            anchorController = GetComponent<M3TFourAnchorController>();
    }

    void OnGUI()
    {
        if (anchorController == null) return;
        EnsureStyles();
        windowRect = GUI.Window(GetInstanceID() + 100, windowRect, DrawWindow, "Anchor Select");
    }

    void DrawWindow(int id)
    {
        var isAuto = string.IsNullOrEmpty(anchorController.ManualOverride);
        var activeKey = isAuto ? anchorController.poseReceiver?.ActiveAnchor : anchorController.ManualOverride;

        // Auto ボタン
        GUI.backgroundColor = isAuto ? AutoBg : InactiveBg;
        if (GUILayout.Button(isAuto ? "Auto  (tracking)" : "Auto", btnStyle, GUILayout.Height(30)))
            anchorController.ManualOverride = "";

        GUILayout.Space(4);

        // A/B/C/D ボタン
        for (int i = 0; i < anchorKeys.Length; i++)
        {
            var isSelected = !isAuto && anchorKeys[i] == anchorController.ManualOverride;
            GUI.backgroundColor = isSelected ? ActiveBg : InactiveBg;
            if (GUILayout.Button(anchorLabels[i], btnStyle, GUILayout.Height(28)))
                anchorController.ManualOverride = anchorKeys[i];
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
