using UnityEngine;

public class M3TTrackingUI : MonoBehaviour
{
    public M3TProcessController processController;
    public M3TPoseReceiver poseReceiver;
    public M3TModelVisualController visualController;
    public M3TVideoBackground videoBackground;
    public bool showPanel = true;

    Rect panelRect = new Rect(16, 16, 380, 360);
    GUIStyle statusStyle;

    void Awake()
    {
        if (processController == null)
            processController = GetComponent<M3TProcessController>();
        if (poseReceiver == null)
            poseReceiver = GetComponent<M3TPoseReceiver>();
        if (visualController == null)
            visualController = GetComponent<M3TModelVisualController>();
        if (videoBackground == null)
            videoBackground = GetComponent<M3TVideoBackground>();
    }

    void OnGUI()
    {
        if (!showPanel)
            return;

        EnsureStyles();
        panelRect = GUI.Window(GetInstanceID(), panelRect, DrawPanel, "M3T Tracking");
    }

    void DrawPanel(int id)
    {
        var isRunning = processController != null && processController.IsRunning;
        var status = processController != null ? processController.LastStatus : "Missing controller";

        GUILayout.Label($"Status: {status}", statusStyle);

        using (new GUILayout.HorizontalScope())
        {
            GUI.enabled = !isRunning && processController != null;
            if (GUILayout.Button("Start Tracking", GUILayout.Height(36)))
                processController.StartTracking();

            GUI.enabled = isRunning && processController != null;
            if (GUILayout.Button("Stop Tracking", GUILayout.Height(36)))
                processController.StopTracking();
            GUI.enabled = true;
        }

        if (poseReceiver != null)
        {
            poseReceiver.applyPose = GUILayout.Toggle(poseReceiver.applyPose, "Apply UDP pose");
            GUILayout.Label($"UDP Port: {poseReceiver.port}");
            GUILayout.Label($"Target: {(poseReceiver.target != null ? poseReceiver.target.name : "None")}");
            GUILayout.Label($"Rotation Offset: {poseReceiver.rotationOffsetEuler}");

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Flip X"))
                    ToggleRotationOffsetAxis(0);
                if (GUILayout.Button("Flip Y"))
                    ToggleRotationOffsetAxis(1);
                if (GUILayout.Button("Flip Z"))
                    ToggleRotationOffsetAxis(2);
                if (GUILayout.Button("Reset"))
                    poseReceiver.rotationOffsetEuler = Vector3.zero;
            }
        }

        if (processController != null)
        {
            if (!string.IsNullOrEmpty(processController.LastError))
                GUILayout.Label($"Last error: {processController.LastError}");
            if (!string.IsNullOrEmpty(processController.LogPath))
                GUILayout.Label($"Log: {processController.LogPath}");
        }

        if (videoBackground != null)
        {
            GUILayout.Label($"Video: {videoBackground.Status}");
        }

        if (visualController != null)
        {
            var enabledVisuals = GUILayout.Toggle(visualController.visualsEnabled, "Transparent model + outline");
            if (enabledVisuals != visualController.visualsEnabled)
                visualController.SetVisualsEnabled(enabledVisuals);

            var showSurface = GUILayout.Toggle(visualController.showModelSurface, "Show transparent surface");
            if (showSurface != visualController.showModelSurface)
            {
                visualController.showModelSurface = showSurface;
                visualController.SetVisualsEnabled(visualController.visualsEnabled);
            }

            if (visualController.targetRoot != null)
            {
                GUILayout.Label($"Model Local Offset: {visualController.targetRoot.localEulerAngles}");

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Model X"))
                        ToggleModelAxis(0);
                    if (GUILayout.Button("Model Y"))
                        ToggleModelAxis(1);
                    if (GUILayout.Button("Model Z"))
                        ToggleModelAxis(2);
                    if (GUILayout.Button("Model Reset"))
                        visualController.targetRoot.localEulerAngles = Vector3.zero;
                }
            }
        }

        GUI.DragWindow(new Rect(0, 0, 10000, 22));
    }

    void ToggleRotationOffsetAxis(int axis)
    {
        if (poseReceiver == null)
            return;

        var offset = poseReceiver.rotationOffsetEuler;
        if (axis == 0)
            offset.x = Mathf.Approximately(Mathf.Repeat(offset.x, 360f), 180f) ? 0f : 180f;
        else if (axis == 1)
            offset.y = Mathf.Approximately(Mathf.Repeat(offset.y, 360f), 180f) ? 0f : 180f;
        else if (axis == 2)
            offset.z = Mathf.Approximately(Mathf.Repeat(offset.z, 360f), 180f) ? 0f : 180f;

        poseReceiver.rotationOffsetEuler = offset;
    }

    void ToggleModelAxis(int axis)
    {
        if (visualController == null || visualController.targetRoot == null)
            return;

        var offset = visualController.targetRoot.localEulerAngles;
        if (axis == 0)
            offset.x = Mathf.Approximately(Mathf.Repeat(offset.x, 360f), 180f) ? 0f : 180f;
        else if (axis == 1)
            offset.y = Mathf.Approximately(Mathf.Repeat(offset.y, 360f), 180f) ? 0f : 180f;
        else if (axis == 2)
            offset.z = Mathf.Approximately(Mathf.Repeat(offset.z, 360f), 180f) ? 0f : 180f;

        visualController.targetRoot.localEulerAngles = offset;
    }

    void EnsureStyles()
    {
        if (statusStyle != null)
            return;

        statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold
        };
    }
}
