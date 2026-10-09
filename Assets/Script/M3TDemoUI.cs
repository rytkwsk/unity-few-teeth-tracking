using UnityEngine;

/// <summary>
/// Anchor Switching demo panel (IEEE demo). Everything the presenter needs is
/// driven from this panel over the UDP command channel, so the OpenCV window and
/// keyboard are not required during the demo:
///   1. Launch camera / tracker
///   2. Quick Align  (global search from the saved pose slots)
///   3. Start Demo   (start tracking from the current pose)
/// While tracking, the panel shows which anchor is currently supplying the pose
/// (F = full arch, A..D = tooth groups) and the fit quality of every anchor.
/// The A..D buttons force a tooth group as the pose source (Auto returns to the
/// tracker's own selection).
/// </summary>
public class M3TDemoUI : MonoBehaviour
{
    public M3TProcessController processController;
    public M3TPoseReceiver poseReceiver;

    [Header("Adjustment steps")]
    public float translationStepMillimeters = 5f;
    public float rotationStepDegrees = 2f;
    [Range(1, 9)] public int poseSlot = 1;

    static readonly string[] AnchorKeys   = { "F", "A", "B", "C", "D" };
    static readonly string[] AnchorLabels = { "F  Full arch", "A  Molar", "B  Premolar-Canine", "C  Canine-Lateral", "D  Incisor" };
    static readonly Color[]  AnchorColors =
    {
        new Color(0.85f, 0.85f, 0.85f),
        new Color(0.9f, 0.2f, 0.2f),
        new Color(0.2f, 0.85f, 0.2f),
        new Color(0.2f, 0.5f, 1.0f),
        new Color(1.0f, 0.85f, 0.1f),
    };

    Rect windowRect;
    string lastAction = "Press 1 to launch the camera and tracker.";
    string forcedAnchor = "";
    // 0 = baseline (full arch only), 1 = best single group, 2 = fusion of all visible groups (tracker default)
    int switchingMode = 2;
    static readonly string[] SwitchingCommands = { "off", "on", "fusion" };
    static readonly string[] SwitchingLabels =
    {
        "Anchor Switching: OFF  (baseline, full arch only)  - click for best-one",
        "Anchor Switching: BEST-ONE  (highest-scoring group)  - click for fusion",
        "Anchor Switching: FUSION  (all visible groups)  - click for baseline",
    };
    bool showAdvanced;
    Vector2 scrollPos;
    string lastMarkerMessage = "";
    GUIStyle titleStyle, labelStyle, statusStyle, barStyle;
    Texture2D whiteTex;

    void Awake()
    {
        if (processController == null) processController = GetComponent<M3TProcessController>();
        if (poseReceiver == null) poseReceiver = GetComponent<M3TPoseReceiver>();
        windowRect = new Rect(Mathf.Max(8, Screen.width - 400), 12, 388, Mathf.Min(720, Screen.height - 24));
    }

    void Update()
    {
        // トラッカーが返すマーカー登録結果（拒否理由・ばらつき）を状態欄に出す。
        // OnGUI の Layout/Repaint の間で文字列が変わらないよう Update で反映する。
        if (poseReceiver != null && poseReceiver.MarkerMessage != lastMarkerMessage)
        {
            lastMarkerMessage = poseReceiver.MarkerMessage;
            if (!string.IsNullOrEmpty(lastMarkerMessage))
                lastAction = lastMarkerMessage;
        }
    }

    void OnGUI()
    {
        EnsureStyles();
        windowRect = GUI.Window(GetInstanceID() + 300, windowRect, DrawWindow, "Anchor Switching Demo");
    }

    void DrawWindow(int id)
    {
        var running = processController != null && processController.IsRunning;
        var mode = poseReceiver != null ? poseReceiver.TrackingMode : "Waiting";
        var poseAnchor = poseReceiver != null && !string.IsNullOrEmpty(poseReceiver.PoseAnchor) ? poseReceiver.PoseAnchor : "-";
        var highlight = poseReceiver != null ? poseReceiver.ActiveAnchor : "-";
        var q = poseReceiver != null ? poseReceiver.Quality : new float[0];

        // ── status ───────────────────────────────────────────────────────────
        scrollPos = GUILayout.BeginScrollView(scrollPos);
        GUILayout.Label($"Tracker: {(running ? "Running" : "Stopped")}     Mode: {mode}", titleStyle);
        GUILayout.Label($"Pose source: {AnchorName(poseAnchor)}     Highlight: {AnchorName(highlight)}", labelStyle);

        // 回転不確か警告。統合中は回転を F 離脱時のまま固定するので、遮蔽中に模型が回ると出力の回転が古いまま
        // 「良好」に見える（静かな誤り）。その間と F 復帰直後は赤で明示し、人に重畳の確認を促す。
        if (poseReceiver != null && poseReceiver.RotationUncertain)
        {
            GUI.backgroundColor = new Color(0.85f, 0.15f, 0.15f);
            GUILayout.Box("ROTATION UNCERTAIN - " + poseReceiver.RotationUncertainReason, statusStyle, GUILayout.MinHeight(30));
            GUI.backgroundColor = Color.white;
        }

        GUILayout.Space(4);
        GUILayout.Label("Anchor fit quality", titleStyle);
        for (int i = 0; i < AnchorKeys.Length; i++)
        {
            float v = q != null && q.Length > i ? Mathf.Clamp01(q[i]) : 0f;
            bool isSource = poseAnchor == AnchorKeys[i];
            using (new GUILayout.HorizontalScope())
            {
                GUI.color = AnchorColors[i];
                GUILayout.Label(AnchorLabels[i] + (isSource ? "  ◀ pose" : ""), labelStyle, GUILayout.Width(190));
                GUI.color = Color.white;
                var r = GUILayoutUtility.GetRect(150, 16);
                GUI.DrawTexture(new Rect(r.x, r.y + 3, r.width, 10), whiteTex, ScaleMode.StretchToFill, false, 0, new Color(0.25f, 0.25f, 0.25f), 0, 0);
                GUI.DrawTexture(new Rect(r.x, r.y + 3, r.width * v, 10), whiteTex, ScaleMode.StretchToFill, false, 0, AnchorColors[i], 0, 0);
                GUILayout.Label(v.ToString("0.00"), labelStyle, GUILayout.Width(36));
            }
        }

        // ── objective accuracy (ArUco reference) ─────────────────────────────
        // fit quality は「輪郭がエッジに乗っている割合」の代理指標で精度ではない。ここは台座の ArUco から
        // 予測した歯列姿勢と追跡姿勢の差（mm / deg）。登録は追跡が合っている瞬間に視点を変えて 3 回以上。
        GUILayout.Space(4);
        GUILayout.Label("Marker error (ArUco reference)", titleStyle);
        string markerText;
        if (poseReceiver == null)
            markerText = "-";
        else if (!poseReceiver.MarkerRegistered)
            markerText = poseReceiver.MarkerVisible
                ? "Not registered.  Marker: VISIBLE  -> if the overlay matches, press Register (3+ different views)."
                : "Not registered.  Marker: NOT visible  -> turn the board toward the camera before pressing Register.";
        else if (!poseReceiver.MarkerVisible)
            markerText = $"Marker not visible   (samples {poseReceiver.MarkerSamples})";
        else if (!poseReceiver.MarkerValid)
            markerText = $"Registered; start tracking to measure   (samples {poseReceiver.MarkerSamples})";
        else
            markerText = $"{poseReceiver.MarkerErrMm:0.0} mm  /  {poseReceiver.MarkerErrDeg:0.0} deg   (samples {poseReceiver.MarkerSamples})";
        GUILayout.Label(markerText, labelStyle);
        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Register marker", GUILayout.Height(24)))
                Send("marker_register", "Registering the current pose against the marker...");
            if (GUILayout.Button("Undo last", GUILayout.Height(24)))
                Send("marker_undo", "Undoing the last marker sample...");
        }

        // ── main steps ───────────────────────────────────────────────────────
        GUILayout.Space(6);
        GUI.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
        GUILayout.Box(lastAction, statusStyle, GUILayout.MinHeight(40));
        GUI.backgroundColor = running ? new Color(0.75f, 0.35f, 0.3f) : new Color(0.25f, 0.7f, 0.3f);
        if (GUILayout.Button(running ? "Shutdown camera / tracker" : "1. Launch camera / tracker", GUILayout.Height(34)))
        {
            if (running)
            {
                processController.StopTracking();
                lastAction = "Tracker stopped.";
            }
            else if (processController != null)
            {
                processController.StartTracking();
                lastAction = "Starting... when the video appears, place the model roughly as saved and press 2.";
            }
        }
        GUI.backgroundColor = new Color(0.95f, 0.65f, 0.15f);
        if (GUILayout.Button("2. Quick Align (search position + rotation, 5-10 s)", GUILayout.Height(38)))
            Send("auto_align", "Searching (the video pauses for 5-10 s)... then check the outline and press 3. If it is one tooth off, nudge with Advanced or press 2 again.");
        GUI.backgroundColor = new Color(0.25f, 0.7f, 0.3f);
        if (GUILayout.Button("3. Start Demo (track from current pose)", GUILayout.Height(38)))
            Send("start_from_pose", "Tracking. Cover part of the model with your hand or the handpiece and watch the anchor switch.");
        GUI.backgroundColor = new Color(0.75f, 0.35f, 0.3f);
        if (GUILayout.Button("Stop tracking (back to alignment)", GUILayout.Height(28)))
            Send("stop", "Back to alignment mode.");
        GUI.backgroundColor = Color.white;

        // ── anchor switching on/off (comparison baseline) ───────────────────
        GUILayout.Space(6);
        GUI.backgroundColor = switchingMode == 0 ? new Color(0.55f, 0.35f, 0.15f)
                            : switchingMode == 1 ? new Color(0.15f, 0.35f, 0.6f) : new Color(0.15f, 0.5f, 0.35f);
        if (GUILayout.Button(SwitchingLabels[switchingMode], GUILayout.Height(28)))
        {
            switchingMode = (switchingMode + 1) % 3;
            Send("anchor_switching:" + SwitchingCommands[switchingMode],
                 switchingMode == 0 ? "Baseline: only the full arch supplies the pose (logged as anchor_switching=0)."
               : switchingMode == 1 ? "Best-one: the highest-scoring visible group takes over (logged as 1)."
                                    : "Fusion: all visible groups are combined into one pose (logged as 2).");
        }
        GUI.backgroundColor = Color.white;

        // ── anchor override ─────────────────────────────────────────────────
        GUILayout.Space(6);
        GUILayout.Label("Pose source (override)", titleStyle);
        using (new GUILayout.HorizontalScope())
        {
            GUI.backgroundColor = forcedAnchor == "" ? new Color(0.15f, 0.35f, 0.6f) : new Color(0.3f, 0.3f, 0.3f);
            if (GUILayout.Button("Auto", GUILayout.Height(26))) { forcedAnchor = ""; Send("anchor_override:", "Automatic anchor selection."); }
            for (int i = 1; i < AnchorKeys.Length; i++)
            {
                if (AnchorKeys[i] == "C") continue;   // C は姿勢源に使わない（tracker 側 ANCHOR_POSE_SOURCE_KEYS）
                GUI.backgroundColor = forcedAnchor == AnchorKeys[i] ? AnchorColors[i] : new Color(0.3f, 0.3f, 0.3f);
                if (GUILayout.Button(AnchorKeys[i], GUILayout.Height(26)))
                {
                    forcedAnchor = AnchorKeys[i];
                    Send("anchor_override:" + AnchorKeys[i], $"Pose forced from group {AnchorKeys[i]}.");
                }
            }
            GUI.backgroundColor = Color.white;
        }

        // ── advanced ────────────────────────────────────────────────────────
        GUILayout.Space(4);
        showAdvanced = GUILayout.Toggle(showAdvanced, "Advanced (pose slots / manual nudge)");
        GUI.enabled = showAdvanced;   // keep the control tree stable between Layout and Repaint
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label($"Slot {poseSlot}", labelStyle, GUILayout.Width(60));
                if (GUILayout.Button("Load")) Send($"load_pose:{poseSlot}", $"Loaded slot {poseSlot}.");
                if (GUILayout.Button("Save")) Send($"save_pose:{poseSlot}", $"Saved current pose to slot {poseSlot}.");
            }
            GUILayout.Label($"Move {translationStepMillimeters:0.#} mm  /  Rotate {rotationStepDegrees:0.#} deg", labelStyle);
            DrawAxisRow("X  left / right", "x", translationStepMillimeters * 0.001f, false);
            DrawAxisRow("Y  up / down",    "y", translationStepMillimeters * 0.001f, false);
            DrawAxisRow("Z  near / far",   "z", translationStepMillimeters * 0.001f, false);
            DrawAxisRow("Rotate X", "x", rotationStepDegrees, true);
            DrawAxisRow("Rotate Y", "y", rotationStepDegrees, true);
            DrawAxisRow("Rotate Z", "z", rotationStepDegrees, true);
        }
        GUI.enabled = true;

        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0, 0, 10000, 22));
    }

    static string AnchorName(string key)
    {
        switch (key)
        {
            case "F": return "F (full arch)";
            case "A": return "A (molar)";
            case "B": return "B (premolar-canine)";
            case "C": return "C (canine-lateral)";
            case "D": return "D (incisor)";
            case "ALL": return "all";
            default: return key;
        }
    }

    void DrawAxisRow(string label, string axis, float amount, bool rotation)
    {
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label(label, labelStyle, GUILayout.Width(120));
            if (GUILayout.Button("-", GUILayout.Height(24)))
                Send($"{(rotation ? "adjust_r" : "adjust_t")}:{axis}:{-amount}", $"{label} -");
            if (GUILayout.Button("+", GUILayout.Height(24)))
                Send($"{(rotation ? "adjust_r" : "adjust_t")}:{axis}:{amount}", $"{label} +");
        }
    }

    void Send(string command, string successText)
    {
        if (processController == null || !processController.IsRunning)
        {
            lastAction = "Press '1. Launch camera / tracker' first.";
            return;
        }
        processController.SendCommandToPython(command);
        lastAction = successText;
    }

    void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle  = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 };
        labelStyle  = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        statusStyle = new GUIStyle(GUI.skin.box) { fontSize = 12, wordWrap = true, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
        barStyle    = new GUIStyle(GUI.skin.box);
        whiteTex = new Texture2D(1, 1); whiteTex.SetPixel(0, 0, Color.white); whiteTex.Apply();
    }
}
