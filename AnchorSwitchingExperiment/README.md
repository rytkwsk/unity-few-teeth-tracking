# Anchor Switching experiment

This directory and `Assets/Scenes/AnchorSwitchingExperimentScene.unity` form an
isolated experimental path for the IEEE demonstration. The existing
`FourGroupScene` and `tracker_four_group.py` remain the stable single-body
baseline and are not modified by this experiment.

## Current experiment

- Unity scene: `Assets/Scenes/AnchorSwitchingExperimentScene.unity`
- Python entry point: `Python/tracker_anchor_switching_experiment.py`
- Anchor A: premolar-canine tooth region
- Anchor B: canine-lateral tooth region
- Pose slots: `Python/initial_poses/anchor_switching_premolarcanine_caninelateral`
- Pose UDP: 5005
- Command UDP: 5006
- Video TCP: 5010

The experiment scene includes a Unity-only setup panel. After launching the
Python tracker from that panel, participants can load/save alignment,
move and rotate the model, and start tracking without touching the OpenCV
window. The panel identifies Anchor A as the premolar-canine region and Anchor
B as the canine-lateral-incisor region.

The normal participant flow is `Launch camera / tracker`,
`Quick Align from saved pose`, then `Start Demo`. Quick Align loads the selected
trusted pose slot and performs a short local M3T refinement without running the
unsafe global candidate search or starting continuous tracking. Manual
translation, rotation, and pose-slot controls remain under `Advanced
adjustment`.

`UNITY_UI_ONLY = True` keeps the OpenCV tracking and reference windows hidden.
Set it to `False` only when the developer keyboard controls or landmark-click
initialization are needed for debugging.

The two source OBJ files and their generated M3T metadata remain in the
external `few_teeth_tracking/models` directory to avoid duplicating hundreds
of megabytes. Set `M3T_SOURCE_PROJECT_DIR` or `M3T_SOURCE_MODEL_DIR` when the
source project is in another location.

The experiment requests the iVCam 1280 x 720 stream and uses
`pym3t/camera_calibration.npz`. The Unity scene contains the corresponding
undistorted camera matrix (`fx=1001.6002`, `fy=1024.028`, `cx=640.2212`,
`cy=334.4454`). Override the calibration with `M3T_CALIBRATION_PATH` if the
camera setup changes.

## Development order

1. Establish a repeatable two-anchor baseline using A and B.
2. Confirm that each anchor starts from the same physical pose.
3. Log per-anchor quality and pose differences without switching.
4. Enable switching only after the candidate wins for several frames.
5. Reject candidates with excessive translation or rotation discontinuity.
6. Synchronize anchors only at initialization, accepted switches, and recovery.
7. Test hand and contra-angle occlusion with a fixed sequence.
8. Compare the experiment against the single-body baseline.
9. Add anchors C and D only after the two-anchor experiment is stable.

## Demo gate

Use this experimental scene for the IEEE demonstration only when repeated tests
show that automatic switching is reliable. Otherwise use `FourGroupScene` as
the fallback and explicitly describe it as a post-submission single-body
implementation update.
