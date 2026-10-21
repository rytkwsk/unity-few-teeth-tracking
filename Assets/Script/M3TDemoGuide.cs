using UnityEngine;

/// <summary>
/// 来場者向けの操作アナウンス（デモ）。初めての人でも迷わないように、画面上部に今やってほしいことを
/// 1 つだけ大きく出し、トラッカーの状態（遮蔽中か・どれだけ動かしたか）を見て次のステップへ自動で進む。
///   1. 模型を輪郭に合わせて置く（スタッフが Quick Align → Start Demo）
///   2. 手で歯の一部を隠す
///   3. 隠したまま緑の円まで横にすべらせる
///   4. 隠したまま回す（rotateTargetDegrees くらい。遮蔽中の回転は 35〜40° までが確かめた範囲）
///   5. 手を離す → 完了
/// スタッフ用キー: G = 表示切替 / N = 次へ / B = 戻る / R = 最初から（右下のボタンも同じ）。
/// M3TDemoUI が同じ GameObject に自動で追加する（シーンの編集は不要）。
/// </summary>
public class M3TDemoGuide : MonoBehaviour
{
    public M3TProcessController processController;
    public M3TPoseReceiver poseReceiver;
    public Camera arCamera;

    [Header("Targets")]
    public float slideTargetMillimeters = 30f;
    public float rotateTargetDegrees = 30f;
    [Tooltip("これを超えて回したら「それ以上は回さないで」と出す（遮蔽中の回転は 35〜40° までしか確かめていない）")]
    public float rotateWarnDegrees = 40f;
    public float coverHoldSeconds = 1.0f;
    public bool showGuide = true;

    enum Step { Align, Cover, Slide, Rotate, Release, Done }
    Step step = Step.Align;
    float conditionSince = -1f;
    bool stepAnchored;
    Vector3 stepStartCenter, slideTargetWorld;
    Quaternion stepStartRotation;
    Vector3 localCenter;
    bool haveLocalCenter;
    float progress;
    string progressText = "";
    float lastAngle;

    GUIStyle stepStyle, bigStyle, subStyle, hintStyle, smallButtonStyle, goalStyle;
    Texture2D whiteTex, ringTex;

    static readonly Color BannerColor = new Color(0f, 0f, 0f, 0.72f);
    static readonly Color DoneColor   = new Color(0.05f, 0.35f, 0.12f, 0.85f);
    static readonly Color AccentColor = new Color(0.35f, 0.85f, 1.0f);
    static readonly Color GoalColor   = new Color(0.2f, 1.0f, 0.35f);
    static readonly Color HintColor   = new Color(1.0f, 0.7f, 0.2f);

    void Awake()
    {
        if (processController == null) processController = GetComponent<M3TProcessController>();
        if (poseReceiver == null) poseReceiver = GetComponent<M3TPoseReceiver>();
    }

    Camera Cam
    {
        get
        {
            if (arCamera == null)
            {
                var intr = FindObjectOfType<M3TCameraIntrinsics>();
                arCamera = intr != null && intr.targetCamera != null ? intr.targetCamera : Camera.main;
            }
            return arCamera;
        }
    }

    bool Tracking => poseReceiver != null && poseReceiver.TrackingMode.StartsWith("TRACKING");
    bool Relocking => poseReceiver != null && poseReceiver.TrackingMode.StartsWith("SNAP");
    // pose_anchor は F で追跡中は "F"、遮蔽中（統合）は "A+B" などの歯群か "hold"
    bool Occluded => Tracking && !string.IsNullOrEmpty(poseReceiver.PoseAnchor) && poseReceiver.PoseAnchor != "F";
    bool OnFullArch => Tracking && poseReceiver.PoseAnchor == "F";

    void Update()
    {
        if (poseReceiver == null || poseReceiver.target == null) return;

        // スタッフが追跡を止めたら最初から
        if (poseReceiver.TrackingMode == "ADJUST" && step != Step.Align && step != Step.Done)
            GoTo(Step.Align);

        switch (step)
        {
            case Step.Align:
                if (Held(OnFullArch, 0.5f)) GoTo(Step.Cover);
                break;
            case Step.Cover:
                if (Held(Occluded, coverHoldSeconds)) GoTo(Step.Slide);
                break;
            case Step.Slide:
            {
                AnchorStep();
                float moved = (ArchCenter() - stepStartCenter).magnitude * 1000f;
                progress = Mathf.Clamp01(moved / slideTargetMillimeters);
                progressText = $"{moved:0} / {slideTargetMillimeters:0} mm";
                if (moved >= slideTargetMillimeters * 0.85f) GoTo(Step.Rotate);
                break;
            }
            case Step.Rotate:
            {
                AnchorStep();
                lastAngle = Quaternion.Angle(stepStartRotation, poseReceiver.target.rotation);
                progress = Mathf.Clamp01(lastAngle / rotateTargetDegrees);
                progressText = $"{lastAngle:0}° / {rotateTargetDegrees:0}°";
                if (lastAngle >= rotateTargetDegrees * 0.9f && Held(true, 0.6f)) GoTo(Step.Release);
                break;
            }
            case Step.Release:
                if (Held(OnFullArch, 1.0f)) GoTo(Step.Done);
                break;
        }
    }

    // cond がこの秒数続いたら true
    bool Held(bool cond, float seconds)
    {
        if (!cond) { conditionSince = -1f; return false; }
        if (conditionSince < 0f) conditionSince = Time.unscaledTime;
        return Time.unscaledTime - conditionSince >= seconds;
    }

    void GoTo(Step next)
    {
        step = next;
        conditionSince = -1f;
        stepAnchored = false;
        progress = 0f;
        progressText = "";
        lastAngle = 0f;
    }

    // ステップに入ったときの位置・回転を基準にする（移動のゴールは画面の中央寄りの横方向に置く）
    void AnchorStep()
    {
        if (stepAnchored) return;
        stepAnchored = true;
        stepStartCenter = ArchCenter();
        stepStartRotation = poseReceiver.target.rotation;
        var cam = Cam;
        if (cam != null)
        {
            var s = cam.WorldToScreenPoint(stepStartCenter);
            float sign = s.x > cam.pixelRect.center.x ? -1f : 1f;
            slideTargetWorld = stepStartCenter + cam.transform.right * sign * slideTargetMillimeters * 0.001f;
        }
        else
            slideTargetWorld = stepStartCenter;
    }

    // 歯列の見た目の中心（模型の原点は歯列の中心とは限らないので、子の Renderer の範囲の中心を使う）
    Vector3 ArchCenter()
    {
        var t = poseReceiver.target;
        if (!haveLocalCenter)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                localCenter = t.InverseTransformPoint(b.center);
            }
            haveLocalCenter = true;
        }
        return t.TransformPoint(localCenter);
    }

    void OnGUI()
    {
        EnsureStyles();
        HandleKeys();
        if (!showGuide)
        {
            if (GUI.Button(new Rect(12, 12, 110, 26), "Show guide (G)")) showGuide = true;
            return;
        }

        string en, ja, hint = null;
        bool showBar = false;
        switch (step)
        {
            case Step.Align:
                en = "Place the model so the teeth line up with the outline";
                ja = "模型を置いて、歯を線（輪郭）に合わせてください";
                hint = Tracking ? null : "Staff will start tracking  /  スタッフが追跡を開始します";
                break;
            case Step.Cover:
                en = "Cover some of the teeth with your hand";
                ja = "手で歯の一部を隠してみてください";
                hint = "The outline keeps following, using only the teeth you can see\n見えている歯だけで追跡が続きます（Anchor Switching）";
                break;
            case Step.Slide:
                en = "Keep covering, and slowly slide the model to the green circle";
                ja = "隠したまま、緑の円の位置まで模型をゆっくり横にすべらせてください";
                showBar = true;
                if (!Occluded && Tracking) hint = "Keep your hand over the teeth  /  手で歯を隠したままにしてください";
                break;
            case Step.Rotate:
                en = $"Keep covering, and slowly turn the model on the table (about {rotateTargetDegrees:0}°)";
                ja = $"隠したまま、模型を机の上でゆっくり回してください（{rotateTargetDegrees:0}° くらい）";
                showBar = true;
                if (lastAngle > rotateWarnDegrees) hint = "That is enough - please stop turning  /  それ以上は回さないでください";
                else if (!Occluded && Tracking) hint = "Keep your hand over the teeth  /  手で歯を隠したままにしてください";
                break;
            case Step.Release:
                en = "Now take your hand away";
                ja = "手を離してください";
                hint = "The outline should still be on the teeth  /  線が歯に重なったままか見てください";
                break;
            default:
                en = "Done - thank you for trying!";
                ja = "体験ありがとうございました";
                hint = "Hidden teeth were tracked from the visible ones (Anchor Switching)\n隠れた歯も、見えている歯から追跡していました";
                break;
        }
        if (Relocking) hint = "Re-aligning... please hold still  /  位置を合わせ直しています。動かさずにお待ちください";

        // ── アナウンスの帯（映像の上部中央。右のデモパネルにかからない幅）────────────
        float right = Screen.width - 410f;
        float w = Mathf.Min(980f, right - 24f);
        float x = Mathf.Max(12f, (right - w) * 0.5f);
        float h = 150f + (showBar ? 34f : 0f) + (hint != null ? 46f : 0f);
        var banner = new Rect(x, 12f, w, h);
        GUI.DrawTexture(banner, whiteTex, ScaleMode.StretchToFill, true, 0, step == Step.Done ? DoneColor : BannerColor, 0, 12f);

        GUILayout.BeginArea(new Rect(banner.x + 22, banner.y + 12, banner.width - 44, banner.height - 20));
        GUILayout.Label(step == Step.Done ? "COMPLETE" : $"STEP {(int)step + 1} / 5", stepStyle);
        GUILayout.Label(en, bigStyle);
        GUILayout.Label(ja, subStyle);
        if (showBar)
        {
            GUILayout.Space(6);
            var r = GUILayoutUtility.GetRect(banner.width - 44, 22);
            var bar = new Rect(r.x, r.y + 4, r.width - 150, 16);
            GUI.DrawTexture(bar, whiteTex, ScaleMode.StretchToFill, true, 0, new Color(1, 1, 1, 0.18f), 0, 8f);
            if (progress > 0f)
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * progress, bar.height), whiteTex, ScaleMode.StretchToFill,
                                true, 0, GoalColor, 0, 8f);
            GUI.Label(new Rect(bar.xMax + 12, r.y - 2, 140, 26), progressText, subStyle);
        }
        if (hint != null)
        {
            GUILayout.Space(4);
            GUILayout.Label(hint, hintStyle);
        }
        GUILayout.EndArea();

        // スタッフ用の小さなボタン（帯の右下）
        float bx = banner.xMax - 3 * 34f - 10f, by = banner.yMax - 30f;
        if (GUI.Button(new Rect(bx, by, 30, 22), "◀", smallButtonStyle)) Back();
        if (GUI.Button(new Rect(bx + 34, by, 30, 22), "▶", smallButtonStyle)) Next();
        if (GUI.Button(new Rect(bx + 68, by, 30, 22), "↺", smallButtonStyle)) GoTo(Step.Align);

        if (step == Step.Slide && stepAnchored) DrawGoal();
    }

    // 緑の円（ゴール）と、今の歯列の中心の点
    void DrawGoal()
    {
        var cam = Cam;
        if (cam == null) return;
        var g = cam.WorldToScreenPoint(slideTargetWorld);
        var c = cam.WorldToScreenPoint(ArchCenter());
        if (g.z <= 0f) return;
        var gp = new Vector2(g.x, Screen.height - g.y);
        var cp = new Vector2(c.x, Screen.height - c.y);
        float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 5f);
        float size = 96f * pulse;
        GUI.DrawTexture(new Rect(gp.x - size / 2, gp.y - size / 2, size, size), ringTex, ScaleMode.StretchToFill, true, 0, GoalColor, 0, 0);
        GUI.Label(new Rect(gp.x - 80, gp.y + size / 2 + 2, 160, 28), "GOAL / ここまで", goalStyle);
        if (c.z > 0f)
            GUI.DrawTexture(new Rect(cp.x - 7, cp.y - 7, 14, 14), whiteTex, ScaleMode.StretchToFill, true, 0, AccentColor, 0, 7f);
    }

    void HandleKeys()
    {
        var e = Event.current;
        if (e == null || e.type != EventType.KeyDown) return;
        switch (e.keyCode)
        {
            case KeyCode.G: showGuide = !showGuide; e.Use(); break;
            case KeyCode.N: Next(); e.Use(); break;
            case KeyCode.B: Back(); e.Use(); break;
            case KeyCode.R: GoTo(Step.Align); e.Use(); break;
        }
    }

    void Next() { if (step != Step.Done) GoTo(step + 1); }
    void Back() { if (step != Step.Align) GoTo(step - 1); }

    void EnsureStyles()
    {
        if (bigStyle != null) return;
        stepStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = AccentColor } };
        bigStyle  = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = Color.white } };
        subStyle  = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true, normal = { textColor = new Color(0.88f, 0.88f, 0.88f) } };
        hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, normal = { textColor = HintColor } };
        goalStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = GoalColor } };
        smallButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };
        whiteTex = new Texture2D(1, 1); whiteTex.SetPixel(0, 0, Color.white); whiteTex.Apply();
        ringTex = MakeRing(128, 0.40f, 0.48f);
    }

    static Texture2D MakeRing(int n, float inner, float outer)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude / n;
                float a = Mathf.Clamp01((d - inner) * n) * Mathf.Clamp01((outer - d) * n);
                px[y * n + x] = new Color(1, 1, 1, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }
}
