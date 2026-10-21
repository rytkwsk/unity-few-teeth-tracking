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
    [Tooltip("横に動かした後、続けてカメラの方へ（机の上で手前へ）動かす距離。0 で横だけ")]
    public float slideTowardCameraMillimeters = 20f;
    [Tooltip("手前に寄せた後、同じ距離だけ奥へ戻す（元の奥行きに戻した方が、続く回転がしやすい）")]
    public bool slideBackAfterTowardCamera = true;
    [Tooltip("30 → 25（2026-10-21 のデモ撮影: 隠したまま 20° を超えて回すと位置が 8〜15 mm ずれる回があった。20° では見栄えが小さい）")]
    public float rotateTargetDegrees = 25f;
    [Tooltip("これを超えて回したら「それ以上は回さないで」と出す")]
    public float rotateWarnDegrees = 35f;
    public float coverHoldSeconds = 1.0f;
    [Tooltip("STEP 2 で手のマークを出す歯群（M3TFourAnchorController の A〜D）。前歯側 C+D を隠して奥歯・小臼歯を見せるのが\n" +
             "一番安定していた（2026-10-21 のデモ撮影: 奥歯側や真ん中を隠して回すと外れた）")]
    public string coverGroups = "CD";
    public bool showGuide = true;

    enum Step { Align, Cover, Slide, Rotate, Release, Done }
    Step step = Step.Align;
    float conditionSince = -1f;
    bool stepAnchored;
    Vector3 stepStartCenter, slideTargetWorld;
    Quaternion stepStartRotation;
    Vector3 localCenter, localCoverCenter;
    bool haveLocalCenter, haveCoverCenter;
    float progress;
    string progressText = "";
    float lastAngle;
    int slideLeg;   // STEP 3 の 0 = 横、1 = カメラの方へ、2 = 奥へ戻す

    GUIStyle stepStyle, bigStyle, subStyle, hintStyle, smallButtonStyle, goalStyle, hintLabelStyle;
    Texture2D whiteTex, ringTex, handTex, fingersTex;
    M3TVideoBackground video;

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

    // 映像が 1 s 以内に届いているか（起動直後の白い画面の間は「カメラ起動中」を出す）
    bool VideoLive
    {
        get
        {
            if (video == null) video = FindObjectOfType<M3TVideoBackground>();
            return video == null || (video.LastFrameTime >= 0f && Time.unscaledTime - video.LastFrameTime < 1f);
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
                float legTarget = SlideLegMillimeters;
                progress = Mathf.Clamp01(moved / legTarget);
                progressText = $"{moved:0} / {legTarget:0} mm";
                if (moved >= legTarget * 0.85f)
                {
                    if (slideLeg < LastSlideLeg)
                    {
                        slideLeg++;              // 次の向きへ。ゴールは今の位置から置き直す
                        stepAnchored = false;
                        progress = 0f;
                    }
                    else
                        GoTo(Step.Rotate);
                }
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
        slideLeg = 0;
    }

    float SlideLegMillimeters => slideLeg == 0 ? slideTargetMillimeters : slideTowardCameraMillimeters;
    int LastSlideLeg => slideTowardCameraMillimeters <= 0f ? 0 : slideBackAfterTowardCamera ? 2 : 1;

    // 机の面の法線。歯列モデルの一番薄い方向は模型の +y（OBJ の頂点の主成分で (0, 1, 0.023)、トラッカーの回転軸と同じ）
    Vector3 TableNormal => poseReceiver.target.up.normalized;

    // v を机の面に沿った向きにする（机を斜めに見ているので、画面の横・手前をそのまま使うと机から浮く）
    Vector3 AlongTable(Vector3 v)
    {
        var n = TableNormal;
        v -= n * Vector3.Dot(v, n);
        return v.sqrMagnitude > 1e-10f ? v.normalized : Vector3.zero;
    }

    // ステップに入ったときの位置・回転を基準にする。STEP 3 のゴールは、横は画面の中央寄り、2 本目はカメラの方へ（どちらも机の面上）
    void AnchorStep()
    {
        if (stepAnchored) return;
        stepAnchored = true;
        stepStartCenter = ArchCenter();
        stepStartRotation = poseReceiver.target.rotation;
        var cam = Cam;
        slideTargetWorld = stepStartCenter;
        if (cam == null) return;
        Vector3 dir;
        if (slideLeg == 0)
        {
            var s = cam.WorldToScreenPoint(stepStartCenter);
            float sign = s.x > cam.pixelRect.center.x ? -1f : 1f;
            dir = AlongTable(cam.transform.right * sign);
        }
        else
        {
            dir = AlongTable(cam.transform.position - stepStartCenter);
            if (slideLeg == 2) dir = -dir;       // 奥へ戻す
        }
        slideTargetWorld = stepStartCenter + dir * SlideLegMillimeters * 0.001f;
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

    // 手で隠してほしい歯群（coverGroups）の見た目の中心。歯群が見つからなければ歯列の中心
    Vector3 CoverCenter()
    {
        var t = poseReceiver.target;
        if (!haveCoverCenter)
        {
            var ctrl = GetComponent<M3TFourAnchorController>();   // Unity の null は ?? で比べられないので明示的に
            if (ctrl == null) ctrl = FindObjectOfType<M3TFourAnchorController>();
            var b = new Bounds();
            bool any = false;
            if (ctrl != null)
            {
                var visuals = new[] { ctrl.anchorAVisual, ctrl.anchorBVisual, ctrl.anchorCVisual, ctrl.anchorDVisual };
                for (int i = 0; i < 4; i++)
                {
                    if (coverGroups.IndexOf((char)('A' + i)) < 0 || visuals[i] == null) continue;
                    foreach (var r in visuals[i].GetComponentsInChildren<Renderer>())
                    {
                        if (!any) { b = r.bounds; any = true; }
                        else b.Encapsulate(r.bounds);
                    }
                }
            }
            if (!any) return ArchCenter();
            localCoverCenter = t.InverseTransformPoint(b.center);
            haveCoverCenter = true;
        }
        return t.TransformPoint(localCoverCenter);
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
                // 指 2 本: 手のひらで小臼歯まで覆うと奥歯だけが残り、回したときに 8〜30 mm ずれた（2026-10-21 20:21）
                en = "Cover the front teeth with two fingers, like the mark";
                ja = "マークのように、前歯を指 2 本で隠してください";
                hint = "The outline keeps following, using only the teeth you can see\n見えている歯だけで追跡が続きます（Anchor Switching）";
                break;
            case Step.Slide:
                en = slideLeg == 0 ? "Keep covering, and slowly slide the model sideways to the green circle"
                   : slideLeg == 1 ? "Next, slide it toward the camera, to the new circle"
                                   : "Now slide it back, away from the camera";
                ja = slideLeg == 0 ? "隠したまま、緑の円の位置まで模型をゆっくり横にすべらせてください"
                   : slideLeg == 1 ? "次は、カメラの方（手前）の円まで模型をゆっくりすべらせてください"
                                   : "今度は、奥の円まで模型を戻してください";
                showBar = true;
                if (!Occluded && Tracking) hint = KeepCoveringHint;
                break;
            case Step.Rotate:
                en = $"Keep covering, and slowly turn the model on the table (about {rotateTargetDegrees:0}°)";
                ja = $"隠したまま、模型を机の上でゆっくり回してください（{rotateTargetDegrees:0}° くらい）";
                showBar = true;
                if (lastAngle > rotateWarnDegrees) hint = "That is enough - please stop turning  /  それ以上は回さないでください";
                else if (!Occluded && Tracking) hint = KeepCoveringHint;
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
        bool waiting = !VideoLive;
        if (waiting)
        {
            en = "Starting the camera...";
            ja = "カメラを起動しています。少しお待ちください";
            hint = null;
            showBar = false;
        }

        // ── アナウンスの帯（映像の上部中央。右のデモパネルにかからない幅）────────────
        float right = Screen.width - 410f;
        float w = Mathf.Min(980f, right - 24f);
        float x = Mathf.Max(12f, (right - w) * 0.5f);
        float h = 150f + (showBar ? 34f : 0f) + (hint != null ? 46f : 0f);
        var banner = new Rect(x, 12f, w, h);
        GUI.DrawTexture(banner, whiteTex, ScaleMode.StretchToFill, true, 0, step == Step.Done ? DoneColor : BannerColor, 0, 12f);

        GUILayout.BeginArea(new Rect(banner.x + 22, banner.y + 12, banner.width - 44, banner.height - 20));
        GUILayout.Label(waiting ? "PLEASE WAIT" : step == Step.Done ? "COMPLETE" : $"STEP {(int)step + 1} / 5", stepStyle);
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

        if (waiting) return;
        if (step == Step.Cover && Tracking) DrawHand(1f);
        else if ((step == Step.Slide || step == Step.Rotate) && Tracking && !Occluded) DrawHand(0.5f);   // 手が外れたら薄く出す
        if (step == Step.Slide && stepAnchored) DrawGoal();
        if (step == Step.Rotate && stepAnchored) DrawTurn();
    }

    const string KeepCoveringHint = "Keep two fingers on the front teeth  /  指で前歯を隠したままにしてください";

    // 「ここを指で隠す」: 隠してほしい歯群の上に指 2 本のシルエット（指先が歯の上）とオレンジの円
    void DrawHand(float strength)
    {
        var cam = Cam;
        if (cam == null) return;
        var center = CoverCenter();
        var c = cam.WorldToScreenPoint(center);
        if (c.z <= 0f) return;
        var cp = new Vector2(c.x, Screen.height - c.y);
        var e = cam.WorldToScreenPoint(center + cam.transform.right * 0.02f);
        float w = Mathf.Clamp(Mathf.Abs(e.x - c.x) * 1.25f, 50f, 180f);     // 指 2 本の幅 ≈ 2.5 cm 相当
        float h = w * 160f / 96f;
        float pulse = 0.55f + 0.3f * Mathf.Sin(Time.unscaledTime * 4f);
        float ring = w * (1.25f + 0.06f * Mathf.Sin(Time.unscaledTime * 4f));
        GUI.DrawTexture(new Rect(cp.x - ring / 2, cp.y - ring / 2, ring, ring), ringTex, ScaleMode.StretchToFill, true, 0,
                        new Color(HintColor.r, HintColor.g, HintColor.b, 0.9f * strength), 0, 0);
        // 指先（テクスチャの 85% の高さ）を歯の上に置き、指は上から伸びてくる
        GUI.DrawTexture(new Rect(cp.x - w / 2, cp.y - h * 0.85f, w, h), fingersTex, ScaleMode.StretchToFill, true, 0,
                        new Color(1f, 1f, 1f, pulse * strength), 0, 0);
        if (strength >= 1f)
            GUI.Label(new Rect(cp.x - 120, cp.y + ring / 2 + 4, 240, 28), "Two fingers here / 指 2 本でここを隠す", hintLabelStyle);
    }

    // 緑の円（ゴール）と、今の歯列の中心からゴールへの矢印
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
        if (c.z > 0f && (gp - cp).magnitude > size * 0.6f)
        {
            // 矢印は円の手前で止め、根元から先へ明るさの波を流して「こっちへ」を見せる
            var dir = (gp - cp).normalized;
            var tip = gp - dir * (size * 0.5f + 6f);
            var tail = cp + dir * 12f;
            DrawArrow(tail, tip, 9f, new Color(GoalColor.r, GoalColor.g, GoalColor.b, 0.9f));
            float wave = Mathf.Repeat(Time.unscaledTime * 0.8f, 1f);
            var p = Vector2.Lerp(tail, tip, wave);
            GUI.DrawTexture(new Rect(p.x - 9, p.y - 9, 18, 18), whiteTex, ScaleMode.StretchToFill, true, 0,
                            new Color(1f, 1f, 1f, 0.85f * (1f - wave)), 0, 9f);
        }
        GUI.DrawTexture(new Rect(gp.x - size / 2, gp.y - size / 2, size, size), ringTex, ScaleMode.StretchToFill, true, 0, GoalColor, 0, 0);
        GUI.Label(new Rect(gp.x - 80, gp.y + size / 2 + 2, 160, 28), "GOAL / ここまで", goalStyle);
        if (c.z > 0f)
            GUI.DrawTexture(new Rect(cp.x - 7, cp.y - 7, 14, 14), whiteTex, ScaleMode.StretchToFill, true, 0, AccentColor, 0, 7f);
    }

    // 回転: 歯列のまわりに「机の上で回す」曲がった矢印（机を斜めに見ているので楕円）。回した分だけ緑で塗る
    void DrawTurn()
    {
        var cam = Cam;
        if (cam == null) return;
        var center = ArchCenter();
        var c = cam.WorldToScreenPoint(center);
        if (c.z <= 0f) return;
        var cp = new Vector2(c.x, Screen.height - c.y);
        var e = cam.WorldToScreenPoint(center + cam.transform.right * 0.045f);   // 歯列の半幅くらい
        float rx = Mathf.Clamp(Mathf.Abs(e.x - c.x), 90f, 320f);
        float ry = rx * 0.38f;
        cp.y += ry * 1.3f;                       // 歯の輪郭に重ならないよう、台座のあたり（歯列の下）に回す
        const float sweep = 300f;                // 矢印の長さ（度）。目標角度までの進み具合をこの上に塗る
        const float start = 120f;
        var bg = new Color(1f, 1f, 1f, 0.55f);
        var fg = new Color(GoalColor.r, GoalColor.g, GoalColor.b, 0.95f);
        Vector2 P(float deg) => cp + new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad) * rx, Mathf.Sin(deg * Mathf.Deg2Rad) * ry);
        const int n = 48;
        float done = sweep * progress;
        for (int i = 0; i < n; i++)
        {
            float a0 = start + sweep * i / n, a1 = start + sweep * (i + 1) / n;
            DrawLine(P(a0), P(a1), 8f, (a1 - start) <= done ? fg : bg);
        }
        // 矢じり（弧の終わりの接線方向）
        var tip = P(start + sweep);
        var tangent = (P(start + sweep) - P(start + sweep - 4f)).normalized;
        DrawArrowHead(tip, tangent, 9f, progress >= 1f ? fg : bg);
        GUI.Label(new Rect(cp.x - 80, cp.y + ry + 10, 160, 30), $"{lastAngle:0}° / {rotateTargetDegrees:0}°", goalStyle);
    }

    // 太さ w の線分（GUI を回転させて細長い四角を描く）
    void DrawLine(Vector2 a, Vector2 b, float w, Color color)
    {
        var d = b - a;
        float len = d.magnitude;
        if (len < 0.5f) return;
        var saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
        GUI.DrawTexture(new Rect(a.x, a.y - w / 2, len + w * 0.5f, w), whiteTex, ScaleMode.StretchToFill, true, 0, color, 0, w / 2);
        GUI.matrix = saved;
    }

    void DrawArrow(Vector2 tail, Vector2 tip, float w, Color color)
    {
        DrawLine(tail, tip, w, color);
        DrawArrowHead(tip, (tip - tail).normalized, w, color);
    }

    void DrawArrowHead(Vector2 tip, Vector2 dir, float w, Color color)
    {
        var nrm = new Vector2(-dir.y, dir.x);
        float h = w * 3.4f;
        DrawLine(tip, tip - dir * h + nrm * h * 0.75f, w, color);
        DrawLine(tip, tip - dir * h - nrm * h * 0.75f, w, color);
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
        hintLabelStyle = new GUIStyle(goalStyle) { normal = { textColor = HintColor } };
        ringTex = MakeRing(128, 0.40f, 0.48f);
        handTex = MakeHand(128, 160);
        fingersTex = MakeFingers(96, 160);
    }

    // 指 2 本（人差し指・中指）を上から伸ばした形。指先が下、付け根側は上へ薄くぼかす。テクスチャの y は下から上
    static Texture2D MakeFingers(int wPx, int hPx)
    {
        float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude - r;
        }
        var tex = new Texture2D(wPx, hPx, TextureFormat.RGBA32, false);
        var px = new Color[wPx * hPx];
        for (int y = 0; y < hPx; y++)
            for (int x = 0; x < wPx; x++)
            {
                var p = new Vector2(x + 0.5f, hPx - y - 0.5f);   // 左上原点・下向き
                float d = Mathf.Min(Capsule(p, new Vector2(33, -10), new Vector2(34, 126), 14f),
                                    Capsule(p, new Vector2(63, -10), new Vector2(62, 136), 14f));
                float a = Mathf.Clamp01(0.5f - d / 2f) * Mathf.Clamp01(p.y / 40f);   // 付け根側（上）を薄く
                px[y * wPx + x] = new Color(1, 1, 1, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // 手のシルエット（手のひら＋指 4 本＋親指、縁を少しぼかす）。テクスチャの y は下から上
    static Texture2D MakeHand(int wPx, int hPx)
    {
        // 形はテクスチャの左上原点・下向き y で書き、書き込むときに上下を反転する
        var palm = new Vector2(64, 100);
        Vector2[,] fingers =
        {
            { new Vector2(36, 78), new Vector2(34, 30) },
            { new Vector2(55, 74), new Vector2(55, 16) },
            { new Vector2(74, 74), new Vector2(76, 20) },
            { new Vector2(92, 80), new Vector2(97, 40) },
        };
        var thumbA = new Vector2(34, 112);
        var thumbB = new Vector2(10, 82);
        float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude - r;
        }
        var tex = new Texture2D(wPx, hPx, TextureFormat.RGBA32, false);
        var px = new Color[wPx * hPx];
        for (int y = 0; y < hPx; y++)
            for (int x = 0; x < wPx; x++)
            {
                var p = new Vector2(x + 0.5f, hPx - y - 0.5f);
                var q = p - palm;
                float d = new Vector2(q.x / 42f, q.y / 44f).magnitude * 42f - 42f;   // 手のひら（楕円）
                for (int f = 0; f < 4; f++) d = Mathf.Min(d, Capsule(p, fingers[f, 0], fingers[f, 1], 9f));
                d = Mathf.Min(d, Capsule(p, thumbA, thumbB, 9.5f));
                float a = Mathf.Clamp01(0.5f - d / 2f);
                px[y * wPx + x] = new Color(1, 1, 1, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
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
