using UnityEngine;

/// <summary>
/// 4アンカー切り替えシーン用コントローラー。
/// M3TPoseReceiver が受信した active_anchor ("A"/"B"/"C"/"D") を毎フレーム見て
/// アクティブなアンカーのアウトラインを強調する。
/// 4つのモデルはすべて同じ M3T ルート Transform の子として配置し、
/// 受信した1つのポーズが全モデルに同時に適用される。
/// </summary>
public class M3TFourAnchorController : MonoBehaviour
{
    [Header("Source")]
    public M3TPoseReceiver poseReceiver;

    [Header("Anchor A  (group_1_molar)")]
    public M3TModelVisualController anchorAVisual;
    public Color anchorAActiveColor   = new Color(0.9f, 0.2f, 0.2f, 0.9f);
    public Color anchorAInactiveColor = new Color(0.4f, 0.4f, 0.4f, 0.2f);

    [Header("Anchor B  (group_2_premolar_canine)")]
    public M3TModelVisualController anchorBVisual;
    public Color anchorBActiveColor   = new Color(0.2f, 0.85f, 0.2f, 0.9f);
    public Color anchorBInactiveColor = new Color(0.4f, 0.4f, 0.4f, 0.2f);

    [Header("Anchor C  (group_3_canine_lateral)")]
    public M3TModelVisualController anchorCVisual;
    public Color anchorCActiveColor   = new Color(0.2f, 0.5f, 1.0f, 0.9f);
    public Color anchorCInactiveColor = new Color(0.4f, 0.4f, 0.4f, 0.2f);

    [Header("Anchor D  (group_4_incisor)")]
    public M3TModelVisualController anchorDVisual;
    public Color anchorDActiveColor   = new Color(1.0f, 0.85f, 0.1f, 0.9f);
    public Color anchorDInactiveColor = new Color(0.4f, 0.4f, 0.4f, 0.2f);

    [Header("Outline width")]
    public float activeOutlineWidth   = 0.006f;
    public float inactiveOutlineWidth = 0.0025f;

    // 空文字列 = トラッカーに追従。"A"/"B"/"C"/"D" を設定すると手動でオーバーライド。
    public string ManualOverride = "";

    string lastApplied;

    void Update()
    {
        if (poseReceiver == null) return;

        var active = string.IsNullOrEmpty(ManualOverride)
            ? poseReceiver.ActiveAnchor
            : ManualOverride;
        if (active == lastApplied) return;
        lastApplied = active;

        Apply(anchorAVisual, active == "A", anchorAActiveColor, anchorAInactiveColor);
        Apply(anchorBVisual, active == "B", anchorBActiveColor, anchorBInactiveColor);
        Apply(anchorCVisual, active == "C", anchorCActiveColor, anchorCInactiveColor);
        Apply(anchorDVisual, active == "D", anchorDActiveColor, anchorDInactiveColor);
    }

    void Apply(M3TModelVisualController vis, bool isActive, Color activeCol, Color inactiveCol)
    {
        if (vis != null)
            vis.SetAnchorHighlight(isActive, activeCol, inactiveCol,
                activeOutlineWidth, inactiveOutlineWidth);
    }
}
