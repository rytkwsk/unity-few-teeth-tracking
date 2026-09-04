using UnityEngine;

// Anchor Switching (tracker_anchor_switching.py) 用。M3TPoseReceiver が
// 受信した active_anchor ("A"/"B") を毎フレーム見て、2つのアンカーモデルの
// うちどちらが「今トラッキング出力として採用されているか」を、アウトラインの
// 太さ/濃さの差で見た目にも分かるようにする。
//
// 2つのアンカーは tracker_anchor_switching.py 側で同じ typodont 座標系を
// 共有しているため、Unity 側では M3TPoseReceiver が更新する1つの親
// Transform（M3T ルート）にどちらのアンカーの子メッシュも一緒にぶら下げれば、
// 受信した1つの pose がそのまま両方に正しく当たる。このスクリプトは見た目の
// ハイライトだけを担当する。
public class M3TAnchorHighlighter : MonoBehaviour
{
    [Header("Source")]
    public M3TPoseReceiver poseReceiver;

    [Header("Anchor A")]
    public M3TModelVisualController anchorAVisual;
    public Color anchorAActiveColor = new Color(1f, 0f, 1f, 0.9f);   // マゼンタ
    public Color anchorAInactiveColor = new Color(0.4f, 0.4f, 0.4f, 0.25f);

    [Header("Anchor B")]
    public M3TModelVisualController anchorBVisual;
    public Color anchorBActiveColor = new Color(1f, 1f, 0f, 0.9f);   // イエロー
    public Color anchorBInactiveColor = new Color(0.4f, 0.4f, 0.4f, 0.25f);

    [Header("Outline width")]
    public float activeOutlineWidth = 0.006f;
    public float inactiveOutlineWidth = 0.0025f;

    string lastAppliedAnchor;

    void Update()
    {
        if (poseReceiver == null)
            return;

        var active = poseReceiver.ActiveAnchor;
        if (active == lastAppliedAnchor)
            return;
        lastAppliedAnchor = active;

        var aIsActive = active == "A";
        if (anchorAVisual != null)
            anchorAVisual.SetAnchorHighlight(aIsActive, anchorAActiveColor, anchorAInactiveColor,
                activeOutlineWidth, inactiveOutlineWidth);
        if (anchorBVisual != null)
            anchorBVisual.SetAnchorHighlight(!aIsActive, anchorBActiveColor, anchorBInactiveColor,
                activeOutlineWidth, inactiveOutlineWidth);
    }
}
