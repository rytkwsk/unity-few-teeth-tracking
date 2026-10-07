using UnityEngine;

/// <summary>
/// Demo-facing controls for the isolated Anchor Switching experiment.
/// All alignment commands are sent to Python over the existing command UDP
/// channel, so the participant does not need to use the OpenCV window or keys.
/// </summary>
public class M3TAnchorExperimentUI : MonoBehaviour
{
    public M3TProcessController processController;
    public M3TPoseReceiver poseReceiver;

    [Header("Adjustment steps")]
    public float translationStepMillimeters = 5f;
    public float rotationStepDegrees = 2f;
    [Range(1, 9)] public int poseSlot = 1;

    Rect windowRect;
    string lastAction = "①カメラを起動 → ②紫の輪郭を実物の歯に重ねてください。";
    bool showAdvanced;
    GUIStyle titleStyle;
    GUIStyle labelStyle;
    GUIStyle guideStyle;
    GUIStyle statusStyle;

    void Awake()
    {
        if (processController == null)
            processController = GetComponent<M3TProcessController>();
        if (poseReceiver == null)
            poseReceiver = GetComponent<M3TPoseReceiver>();

        // Keep the controls away from the teeth used for initial alignment.
        windowRect = new Rect(Mathf.Max(8, Screen.width - 438), 12, 426,
            Mathf.Min(696, Screen.height - 24));
    }

    void OnGUI()
    {
        EnsureStyles();
        windowRect = GUI.Window(GetInstanceID() + 200, windowRect, DrawWindow,
            "Anchor Switching Setup");
    }

    void DrawWindow(int id)
    {
        var running = processController != null && processController.IsRunning;
        var active = poseReceiver != null ? poseReceiver.ActiveAnchor : "-";
        var mode = poseReceiver != null ? poseReceiver.TrackingMode : "Waiting";

        GUI.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        GUILayout.Box("位置合わせする場所", titleStyle, GUILayout.Height(31));
        GUI.backgroundColor = Color.white;
        GUILayout.Label("紫の輪郭を、実物の『尖った犬歯』と\nそのすぐ奥の『小臼歯』に重ねます。", guideStyle);
        GUILayout.Label("順番： 1 犬歯の尖端 → 2 歯の外周 → 3 小臼歯", labelStyle);

        GUILayout.Space(5);
        GUILayout.Label("表示中の歯群", titleStyle);

        GUI.backgroundColor = active == "A" ? new Color(1f, 0.25f, 1f) : new Color(0.45f, 0.45f, 0.45f);
        GUILayout.Box($"A  小臼歯 - 犬歯  {(active == "A" ? "[ACTIVE]" : "")}", GUILayout.Height(30));

        GUI.backgroundColor = active == "B" ? new Color(1f, 0.9f, 0.15f) : new Color(0.45f, 0.45f, 0.45f);
        GUILayout.Box($"B  犬歯 - 側切歯  {(active == "B" ? "[ACTIVE]" : "")}", GUILayout.Height(30));
        GUI.backgroundColor = Color.white;
        GUILayout.Label("位置合わせ中は紫のAだけを表示します。追跡中は遮蔽に応じてA/Bが切り替わります。", labelStyle);

        GUILayout.Space(6);
        GUILayout.Label($"Tracker: {(running ? "Running" : "Stopped")}    Alignment: {mode}", titleStyle);
        GUI.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
        GUILayout.Box(lastAction, statusStyle, GUILayout.MinHeight(42));
        GUI.backgroundColor = Color.white;
        GUI.backgroundColor = running ? new Color(0.75f, 0.35f, 0.3f) : new Color(0.25f, 0.7f, 0.3f);
        if (GUILayout.Button(running ? "Shutdown camera / tracker" : "1. Launch camera / tracker", GUILayout.Height(34)))
        {
            if (running)
            {
                processController.StopTracking();
                lastAction = "停止しました。";
            }
            else if (processController != null)
            {
                processController.StartTracking();
                lastAction = "映像が出たら、歯型を保存時と近い位置に置いて②を押してください。";
            }
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(7);
        GUI.backgroundColor = new Color(0.95f, 0.65f, 0.15f);
        if (GUILayout.Button("2. Quick Align from saved pose", GUILayout.Height(42)))
            Send("auto_align", "紫の輪郭が、尖った犬歯と隣の小臼歯を囲んでいるか確認してください。");

        GUI.backgroundColor = new Color(0.25f, 0.7f, 0.3f);
        if (GUILayout.Button("3. Start Demo", GUILayout.Height(42)))
            Send("start_from_pose", "追跡中です。歯型をゆっくり動かし、色の切替を確認してください。");

        GUI.backgroundColor = Color.white;
        showAdvanced = GUILayout.Toggle(showAdvanced, "Advanced adjustment");
        // Keep the GUILayout control tree stable between Layout and Repaint.
        // Controls stay present but disabled while the advanced section is off.
        GUI.enabled = showAdvanced;
        {
            GUILayout.Label($"保存姿勢  Slot {poseSlot}", titleStyle);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load")) Send($"load_pose:{poseSlot}", "保存姿勢を読み込みました");
                if (GUILayout.Button("Save")) Send($"save_pose:{poseSlot}", "現在姿勢を保存しました");
            }

            GUILayout.Label($"位置  {translationStepMillimeters:0.#} mm", titleStyle);
            DrawAxisRow("X  左 / 右", "x", translationStepMillimeters * 0.001f, false);
            DrawAxisRow("Y  上 / 下", "y", translationStepMillimeters * 0.001f, false);
            DrawAxisRow("Z  手前 / 奥", "z", translationStepMillimeters * 0.001f, false);

            GUILayout.Label($"回転  {rotationStepDegrees:0.#} deg", titleStyle);
            DrawAxisRow("Rotate X", "x", rotationStepDegrees, true);
            DrawAxisRow("Rotate Y", "y", rotationStepDegrees, true);
            DrawAxisRow("Rotate Z", "z", rotationStepDegrees, true);
        }
        GUI.enabled = true;

        GUI.backgroundColor = new Color(0.75f, 0.35f, 0.3f);
        if (GUILayout.Button("Stop tracking / return to adjustment", GUILayout.Height(32)))
            Send("stop", "位置合わせモードへ戻りました");
        GUI.backgroundColor = Color.white;

        GUI.DragWindow(new Rect(0, 0, 10000, 22));
    }

    void DrawAxisRow(string label, string axis, float amount, bool rotation)
    {
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label(label, GUILayout.Width(125));
            if (GUILayout.Button("-", GUILayout.Height(27)))
                Send($"{(rotation ? "adjust_r" : "adjust_t")}:{axis}:{-amount}", $"{label} -");
            if (GUILayout.Button("+", GUILayout.Height(27)))
                Send($"{(rotation ? "adjust_r" : "adjust_t")}:{axis}:{amount}", $"{label} +");
        }
    }

    void Send(string command, string successText)
    {
        if (processController == null || !processController.IsRunning)
        {
            lastAction = "先に『1. Launch camera / tracker』を押してください。";
            return;
        }

        processController.SendCommandToPython(command);
        lastAction = successText;
    }

    void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        guideStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 18,
            wordWrap = true,
            normal = { textColor = new Color(1f, 0.35f, 1f) }
        };
        statusStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 13,
            wordWrap = true,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };
    }
}
