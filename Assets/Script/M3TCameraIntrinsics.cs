using UnityEngine;

[ExecuteAlways]
public class M3TCameraIntrinsics : MonoBehaviour
{
    public Camera targetCamera;
    public int imageWidth = 640;
    public int imageHeight = 480;
    public float fx = 553.5494f;
    public float fy = 551.1633f;
    public float cx = 318.2961f;
    public float cy = 229.68362f;
    public float nearClip = 0.01f;
    public float farClip = 5f;
    // Game ビューの縦横比が映像（imageWidth:imageHeight）と違うとき、引き延ばさずに
    // レターボックス（黒帯）で表示する。false だと Unity がビュー全体に引き延ばして描くため
    // 映像も重畳も横に伸びてぼけたように見える。
    public bool fitViewport = true;
    // 実行中はトラッカーから届く K（M3T に渡したのと同じ歪み補正後の値）で上の fx/fy/cx/cy/画像サイズを
    // 上書きする。手書きの値は再キャリブのたびにずれるため（2026-10-09: fx 1001.6 vs 1146.6 で重畳が
    // 画像端で最大 77 px 外れた）。インスペクタの値はトラッカー未接続時の初期値としてだけ使われる。
    public bool syncFromTracker = true;
    public M3TPoseReceiver poseReceiver;
    int appliedIntrinsicsVersion = -1;

    void Awake()
    {
        Apply();
    }

    void LateUpdate()
    {
        SyncFromTracker();
        ApplyViewportRect();
    }

    void SyncFromTracker()
    {
        if (!syncFromTracker || !Application.isPlaying)
            return;
        if (poseReceiver == null)
            poseReceiver = FindObjectOfType<M3TPoseReceiver>();
        if (poseReceiver == null || poseReceiver.CameraK == null || poseReceiver.ImageSize == null)
            return;
        if (poseReceiver.IntrinsicsVersion == appliedIntrinsicsVersion)
            return;

        var k = poseReceiver.CameraK;
        var size = poseReceiver.ImageSize;
        Debug.Log($"M3TCameraIntrinsics: tracker K を適用 fx={k[0]:F1} fy={k[1]:F1} cx={k[2]:F1} cy={k[3]:F1} " +
                  $"{size[0]}x{size[1]}（シーン設定値 fx={fx:F1} fy={fy:F1} cx={cx:F1} cy={cy:F1} {imageWidth}x{imageHeight} を上書き）");
        fx = k[0]; fy = k[1]; cx = k[2]; cy = k[3];
        imageWidth = size[0]; imageHeight = size[1];
        appliedIntrinsicsVersion = poseReceiver.IntrinsicsVersion;
        Apply();
    }

    void ApplyViewportRect()
    {
        if (!fitViewport || targetCamera == null || imageWidth <= 0 || imageHeight <= 0)
            return;
        var want = imageWidth / (float)imageHeight;
        var have = Screen.width / (float)Screen.height;
        Rect r;
        if (have > want)
        {
            var w = want / have;
            r = new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }
        else
        {
            var h = have / want;
            r = new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }
        if (targetCamera.rect != r)
        {
            targetCamera.rect = r;
            targetCamera.aspect = want;   // rect 変更で aspect が再計算されるので固定し直す
        }
    }

    void OnEnable()
    {
        Apply();
    }

    void OnValidate()
    {
        Apply();
    }

    public void Apply()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();
        if (targetCamera == null || imageWidth <= 0 || imageHeight <= 0)
            return;

        targetCamera.nearClipPlane = nearClip;
        targetCamera.farClipPlane = farClip;
        targetCamera.aspect = imageWidth / (float)imageHeight;

        var projection = new Matrix4x4();
        projection[0, 0] = 2f * fx / imageWidth;
        projection[0, 2] = 1f - 2f * cx / imageWidth;
        projection[1, 1] = 2f * fy / imageHeight;
        projection[1, 2] = 2f * cy / imageHeight - 1f;
        projection[2, 2] = -(farClip + nearClip) / (farClip - nearClip);
        projection[2, 3] = -(2f * farClip * nearClip) / (farClip - nearClip);
        projection[3, 2] = -1f;

        targetCamera.projectionMatrix = projection;
    }
}
