using UnityEngine;

/// <summary>
/// FourGroupScene 用。M3T ルートの子として置かれた 4 つの OBJ PrefabInstance に
/// M3TModelVisualController を設定し、M3TFourAnchorController の各フィールドを接続する。
/// モデルはシーン YAML で PrefabInstance として静的配置されているため、Editor でも表示される。
/// </summary>
[RequireComponent(typeof(M3TPoseReceiver))]
[RequireComponent(typeof(M3TFourAnchorController))]
public class M3TFourGroupBootstrap : MonoBehaviour
{
    // 子オブジェクトの順序 (シーン YAML の m_Children 順) と対応
    static readonly (Color active, Color inactive)[] Groups =
    {
        (new Color(0.9f, 0.2f, 0.2f, 0.9f),  new Color(0.4f, 0.4f, 0.4f, 0.2f)), // A molar
        (new Color(0.2f, 0.85f,0.2f, 0.9f),  new Color(0.4f, 0.4f, 0.4f, 0.2f)), // B premolar-canine
        (new Color(0.2f, 0.5f, 1.0f, 0.9f),  new Color(0.4f, 0.4f, 0.4f, 0.2f)), // C canine-lateral
        (new Color(1.0f, 0.85f,0.1f, 0.9f),  new Color(0.4f, 0.4f, 0.4f, 0.2f)), // D incisor
    };

    void Awake()
    {
        var poseReceiver = GetComponent<M3TPoseReceiver>();
        var highlighter  = GetComponent<M3TFourAnchorController>();
        highlighter.poseReceiver = poseReceiver;

        var visuals = new M3TModelVisualController[4];
        int count = Mathf.Min(transform.childCount, Groups.Length);

        for (int i = 0; i < count; i++)
        {
            var child = transform.GetChild(i);
            // Python 側 MODEL_UNIT_IN_METER と一致させる（2026-09-09: ArUco 基準の実測から 0.04 → 0.025）
            child.localScale    = Vector3.one * 0.025f;
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;

            var (activeCol, inactiveCol) = Groups[i];

            var vc = child.GetComponent<M3TModelVisualController>();
            if (vc == null) vc = child.gameObject.AddComponent<M3TModelVisualController>();

            vc.targetRoot       = child;
            vc.visualsEnabled   = true;
            vc.showModelSurface = false;
            vc.opacity          = 0.025f;
            vc.outlineWidth     = 0.0012f;   // メートル。VisualController がオブジェクト単位に換算する
            vc.modelColor       = new Color(activeCol.r, activeCol.g, activeCol.b, 0.025f);
            vc.outlineColor     = inactiveCol;

            visuals[i] = vc;
        }

        highlighter.anchorAVisual = visuals[0];
        highlighter.anchorBVisual = visuals.Length > 1 ? visuals[1] : null;
        highlighter.anchorCVisual = visuals.Length > 2 ? visuals[2] : null;
        highlighter.anchorDVisual = visuals.Length > 3 ? visuals[3] : null;
    }
}
