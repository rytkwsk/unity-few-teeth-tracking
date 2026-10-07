import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { Presentation, PresentationFile } from "@oai/artifact-tool";

const workspaceDir = process.cwd();
const SKILL_DIR = "C:/Users/rytkw/.codex/plugins/cache/openai-primary-runtime/presentations/26.909.12148/skills/presentations";
const RUNTIME_PYTHON = "C:/Users/rytkw/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe";
const buildDir = path.join(workspaceDir, ".codex-slides");
const finalPath = path.join(workspaceDir, "output", "presentation", "2026-10-03_progress_and_ieee_demo_practice.pptx");

const utils = await import(pathToFileURL(path.join(SKILL_DIR, "container_tools/artifact_tool_utils.mjs")).href);
const { resolvePresentationFont, finalizePresentation, makeNativeBulletParagraphs } = utils;
const family = resolvePresentationFont({ fontFamily: "Yu Gothic" });

const C = {
  navy: "#10253F", blue: "#1C6E9E", cyan: "#35B6C8", pale: "#EAF4F7",
  white: "#FFFFFF", ink: "#172433", gray: "#5D6B78", light: "#F5F7F9",
  green: "#2E8B70", amber: "#D98C2B", red: "#C9504D",
};

const pres = Presentation.create({ slideSize: { width: 1280, height: 720 } });

function rect(slide, x, y, w, h, fill, radius = 0) {
  return slide.shapes.add({
    geometry: radius ? "roundRect" : "rect",
    position: { left: x, top: y, width: w, height: h },
    fill,
    line: { fill: "none", width: 0 },
  });
}

function text(slide, value, x, y, w, h, size = 28, color = C.ink, bold = false, align = "left") {
  const box = slide.shapes.add({
    geometry: "textbox",
    position: { left: x, top: y, width: w, height: h },
    fill: "none",
    line: { fill: "none", width: 0 },
  });
  box.text = value;
  box.text.style = { typeface: family, fontSize: size, color, bold, autoFit: "none", alignment: align };
  return box;
}

function bullets(slide, items, x, y, w, h, size = 25, color = C.ink) {
  const box = slide.shapes.add({
    geometry: "textbox",
    position: { left: x, top: y, width: w, height: h },
    fill: "none",
    line: { fill: "none", width: 0 },
  });
  box.text = makeNativeBulletParagraphs(items, { marginLeftPoints: 20, hangingPoints: 9, spaceAfterPoints: 12 });
  box.text.style = { typeface: family, fontSize: size, color, autoFit: "none" };
  return box;
}

function base(title, section, num) {
  const slide = pres.slides.add();
  slide.background.fill = C.white;
  rect(slide, 0, 0, 1280, 18, C.cyan);
  text(slide, section, 72, 40, 280, 28, 15, C.blue, true);
  text(slide, title, 72, 78, 1136, 64, 36, C.navy, true);
  text(slide, String(num).padStart(2, "0"), 1180, 670, 48, 24, 14, C.gray, false, "right");
  return slide;
}

// 1. Cover
{
  const s = pres.slides.add();
  s.background.fill = C.navy;
  rect(s, 0, 0, 28, 720, C.cyan);
  text(s, "Progress Update and IEEE Demo Practice", 92, 150, 1040, 140, 48, C.white, true);
  text(s, "Few-teeth tracking for AR-assisted dental treatment", 96, 305, 960, 62, 26, "#CAE8EF");
  rect(s, 96, 405, 190, 5, C.cyan);
  text(s, "Meeting: October 3, 2026", 96, 442, 520, 40, 21, C.white, false);
  text(s, "Draft based on the current Unity and Python implementation", 96, 492, 760, 38, 18, "#AFC3D2");
  s.speakerNotes.textFrame.setText(
    "Opening (English): Today I will first summarize this month's implementation progress. Then I will practice the short explanation for the IEEE demonstration. The abstract PDF still needs to be checked, so any statement that depends on the submitted abstract should be confirmed before the meeting."
  );
}

// 2. Submitted abstract
{
  const s = base("Submitted abstract: Anchor Switching", "ABSTRACT BASELINE", 2);
  text(s, "Objective", 82, 170, 250, 40, 24, C.blue, true);
  text(s, "Maintain AR implant guidance when a hand or contra-angle handpiece partially occludes the dental cast.", 82, 214, 520, 122, 27, C.ink, false);
  const blocks = [
    ["Multiple models", "Independent dental\narch targets", C.blue],
    ["Select anchor", "Use the most stable\ntracking result", C.green],
    ["Output model", "Keep the AR guidance\nvisible", C.amber],
  ];
  blocks.forEach((b, i) => {
    const x = 660 + i * 185;
    rect(s, x, 210, 155, 150, C.light, 12);
    rect(s, x, 210, 155, 12, b[2]);
    text(s, b[0], x + 12, 230, 131, 56, 18, C.navy, true, "center");
    text(s, b[1], x + 12, 292, 131, 55, 15, C.gray, false, "center");
    if (i < 2) text(s, ">", x + 157, 267, 28, 35, 27, C.cyan, true, "center");
  });
  rect(s, 80, 405, 1120, 1, "#CFD8DF");
  bullets(s, [
    "The submitted evaluation divided the full arch into left and right segments.",
    "The active region was shown by changing color and transparency.",
    "The paper reports continued visualization under some partial occlusions, but also positional deviations.",
  ], 92, 440, 1080, 170, 22);
  s.speakerNotes.textFrame.setText(
    "English practice: The submitted abstract proposes Anchor Switching. It treats several dental arch models as independent tracking targets and transfers the pose from the most stable target to an output model. The goal is to maintain AR implant guidance when part of the cast is occluded. The submitted experiment used left and right segmented arch models."
  );
}

// 3. This month's changes
{
  const s = base("Changes completed this month", "MONTHLY PROGRESS", 3);
  const rows = [
    ["Tracker", "VisionLib", "Python implementation of M3T", C.green],
    ["Anchor geometry", "Left and right arch segments", "Few-tooth groups and a smooth combined arch", C.blue],
    ["Physical scale", "Earlier 0.04 m per unit", "0.025 m per unit from marker-based measurement", C.amber],
    ["Camera and overlay", "Submitted prototype", "720p calibration and corrected Unity transform", C.cyan],
  ];
  text(s, "Area", 80, 168, 230, 34, 18, C.gray, true);
  text(s, "Previous", 330, 168, 345, 34, 18, C.gray, true);
  text(s, "Current", 720, 168, 450, 34, 18, C.gray, true);
  rows.forEach((r, i) => {
    const y = 214 + i * 100;
    if (i % 2 === 0) rect(s, 68, y - 8, 1132, 82, C.light);
    rect(s, 80, y + 3, 8, 50, r[3]);
    text(s, r[0], 105, y, 195, 55, 21, C.navy, true);
    text(s, r[1], 330, y, 340, 60, 20, C.gray);
    text(s, r[2], 720, y, 455, 60, 20, C.ink, true);
  });
  s.speakerNotes.textFrame.setText(
    "English practice: Since submission, the implementation has changed in four ways. We replaced the tracking backend with a Python implementation of M3T. We created smaller tooth-region models and a smooth combined arch. We corrected the physical model scale using an ArUco reference. We also updated the camera calibration and the Unity coordinate conversion for the 720p setup."
  );
}

// 4. Robustness
{
  const s = base("Tracking stability and recovery", "MONTHLY PROGRESS", 4);
  const xs = [90, 385, 680, 975];
  const titles = ["Track", "Evaluate", "Hold", "Relock"];
  const bodies = [
    "M3T updates the pose from the current camera frame.",
    "Projected model edges are compared with image edges.",
    "The last reliable pose is kept during short occlusion.",
    "A wider pose search runs after sustained quality loss.",
  ];
  const colors = [C.blue, C.green, C.amber, C.red];
  xs.forEach((x, i) => {
    text(s, String(i + 1), x, 190, 54, 54, 32, colors[i], true, "center");
    text(s, titles[i], x + 62, 195, 165, 40, 24, C.navy, true);
    text(s, bodies[i], x, 265, 220, 145, 21, C.ink);
    if (i < 3) text(s, ">", x + 235, 205, 38, 45, 30, C.cyan, true, "center");
  });
  rect(s, 84, 455, 1110, 112, C.pale, 10);
  text(s, "Current M3T quality logic", 108, 478, 260, 35, 20, C.blue, true);
  text(s, "EMA smoothing reduces frame-to-frame noise. Quality checks run on a downscaled image to preserve speed and threshold consistency.", 370, 475, 790, 70, 23, C.ink);
  text(s, "Evidence still needed: repeatable accuracy and recovery measurements under the intended demo conditions.", 94, 605, 1080, 42, 19, C.red, true);
  s.speakerNotes.textFrame.setText(
    "English practice: I added a quality-based recovery strategy. A short degradation does not immediately trigger a global search. Instead, the system holds the last reliable pose while continuing to test the current image. If the low-quality state continues, it starts a guarded relock and later expands the search if necessary. We still need systematic measurements under the final demo setup."
  );
}

// 5. Current evidence and gaps
{
  const s = base("Critical difference before the demo", "MONTHLY PROGRESS", 5);
  text(s, "Current single-body mode", 85, 175, 500, 45, 25, C.green, true);
  bullets(s, [
    "SINGLE_BODY_MODE is enabled in tracker_four_group.py.",
    "One combined arch supplies the tracking pose.",
    "A-D selection changes the Unity highlight only.",
    "This mode does not demonstrate dynamic anchor switching.",
  ], 85, 235, 510, 290, 22);
  rect(s, 635, 160, 2, 430, "#D3DCE2");
  text(s, "Decision for the IEEE demo", 685, 175, 470, 45, 25, C.red, true);
  bullets(s, [
    "Use multi-anchor mode if switching itself must be shown.",
    "Use single-body mode if stable overlay is the priority.",
    "Explain the difference explicitly in either case.",
    "Prepare a recorded fallback for the live sequence.",
  ], 685, 235, 500, 290, 22);
  text(s, "Recommended: rehearse both modes, then select the live mode after a repeatability test.", 85, 618, 1080, 34, 19, C.red, true, "center");
  s.speakerNotes.textFrame.setText(
    "For the progress meeting: this is the central decision. The submitted contribution is dynamic anchor selection. The current default configuration prioritizes stability by using one combined rigid body. If the conference demonstration must visually show switching, set SINGLE_BODY_MODE to false and verify the four independent anchors. If the single-body mode is used, describe it as a post-submission implementation update and do not claim that A-D highlighting is actual switching."
  );
}

// 6. Demo pitch
{
  const s = base("IEEE demo: 75-second explanation", "DEMO PRACTICE", 6);
  rect(s, 78, 165, 1124, 400, C.light, 12);
  text(s,
    "Our submitted demonstration addresses tracking loss caused by partial occlusion during AR-assisted dental implant treatment. " +
    "The abstract describes Anchor Switching, which selects the most stable pose from multiple dental arch models and transfers it to the AR output. Since submission, we replaced the original tracking backend with a Python implementation of M3T and investigated tooth-region models that avoid the artificial cut surfaces of the earlier left-right segmentation. " +
    "For the current stable configuration, we combine the visible tooth groups into one rigid-body model. This is an implementation update rather than anchor switching. We also corrected the physical scale, updated the 720p camera calibration, and added quality-based pose holding and relocking. I will demonstrate the current tracking behavior and explain how these changes relate to the submitted method.",
    112, 205, 1056, 320, 23, C.ink);
  text(s, "Speak slowly: about 125 words per minute", 85, 610, 1080, 32, 19, C.blue, true, "center");
  s.speakerNotes.textFrame.setText(
    "Practice target: 70 to 80 seconds. Stress these words: partial occlusion, Anchor Switching, since submission, implementation update, one rigid body. The sentence 'This is an implementation update rather than anchor switching' is important if SINGLE_BODY_MODE remains enabled."
  );
}

// 7. Abstract changes
{
  const s = base("Submitted method and current demonstration", "DEMO PRACTICE", 7);
  text(s, "Suggested transition", 88, 172, 300, 38, 23, C.blue, true);
  rect(s, 82, 225, 1115, 150, C.pale, 10);
  text(s,
    "Since the abstract submission, we revised the implementation to improve the stability of the live demonstration. " +
    "The objective remains the same, but the current stable mode uses one combined rigid-body model instead of dynamically switching between independent anchors.",
    112, 245, 1050, 115, 23, C.navy, true);
  text(s, "Use this structure for each difference", 88, 410, 440, 38, 23, C.gray, true);
  const steps = [
    ["Submitted", "Multiple independent models and dynamic selection."],
    ["Current", "M3T with one combined model in the stable mode."],
    ["Reason", "All visible contours constrain one pose estimate."],
    ["Scope", "A technical demo, not a clinical validation."],
  ];
  steps.forEach((st, i) => {
    const x = 88 + i * 280;
    text(s, `${i + 1}. ${st[0]}`, x, 475, 240, 38, 21, [C.blue, C.green, C.amber, C.red][i], true);
    text(s, st[1], x, 525, 235, 88, 19, C.ink);
  });
  s.speakerNotes.textFrame.setText(
    "If multi-anchor mode is restored and verified before the conference, revise the current-mode statement. Otherwise keep this wording. Do not describe manual A-D highlighting as dynamic anchor switching. The abstract itself already limits the claim to certain partial occlusion conditions and states that anchor design still needs improvement."
  );
}

// 8. Questions
{
  const s = base("Likely questions and concise answers", "DEMO PRACTICE", 8);
  const qa = [
    ["Is the current mode Anchor Switching?", "No. The stable mode uses one combined rigid body. Multi-anchor mode remains available for evaluation."],
    ["Why change the submitted design?", "Independent small models could drift, while the combined model uses more visible contour information."],
    ["What improved after submission?", "We corrected scale and calibration, revised the model geometry, and added quality-based recovery."],
    ["How do you evaluate accuracy?", "We log translation and rotation error against a registered ArUco reference."],
    ["Is it clinically validated?", "No. This is a technical demonstration, and clinical validation remains future work."],
  ];
  qa.forEach((q, i) => {
    const y = 165 + i * 92;
    text(s, `Q${i + 1}`, 78, y, 52, 35, 20, C.cyan, true);
    text(s, q[0], 140, y, 430, 35, 21, C.navy, true);
    text(s, q[1], 595, y, 600, 62, 20, C.ink);
    if (i < qa.length - 1) rect(s, 138, y + 72, 1055, 1, "#D9E0E5");
  });
  s.speakerNotes.textFrame.setText(
    "Answer pattern: one direct sentence, one technical detail if requested, then return to the live demonstration. Do not blur the distinction between Anchor Switching and single-body tracking. If asked about the abstract, say: The abstract describes the submitted multi-anchor method. The stable configuration shown here is a post-submission implementation update that uses one combined rigid body."
  );
}

await fs.mkdir(buildDir, { recursive: true });
await fs.mkdir(path.dirname(finalPath), { recursive: true });

const requirements = {
  explicitTotalSlideCount: 8,
  requiredNativeTableOwnerSlides: [],
  requiredNativeChartOwnerSlides: [],
};
const fontPolicy = { basis: "design", families: [family] };
const candidatePath = path.join(buildDir, "candidate.pptx");
await (await PresentationFile.exportPptx(pres)).save(candidatePath);

if (!process.env.SKIP_FINALIZER) await finalizePresentation({
  ...requirements,
  workspaceDir,
  candidatePath,
  finalPath,
  pythonExecutable: RUNTIME_PYTHON,
  integrityValidatorPath: path.join(SKILL_DIR, "container_tools/inspect_presentation_package_integrity.py"),
  layoutValidatorPath: path.join(SKILL_DIR, "container_tools/inspect_presentation_layout_geometry.py"),
  layoutArgs: ["--expected-slide-size-emu", "12192000,6858000", "--validate-bullet-geometry", "--validate-heading-fit"],
  requiredNativeTableOwnerSlides: [],
  fontPolicy,
  verifyArtifactToolImport: true,
  receiptPath: path.join(buildDir, "validation.json"),
});

console.log(finalPath);
