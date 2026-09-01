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

    void Awake()
    {
        Apply();
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
