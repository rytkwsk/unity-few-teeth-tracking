using System.Collections.Generic;
using UnityEngine;

public class M3TModelVisualController : MonoBehaviour
{
    public Transform targetRoot;
    public bool visualsEnabled = true;
    public bool showModelSurface = false;
    [Range(0.005f, 1f)] public float opacity = 0.025f;
    [Range(0.0001f, 0.05f)] public float outlineWidth = 0.006f;
    public Color modelColor = new Color(0.05f, 0.85f, 1f, 0.025f);
    public Color outlineColor = new Color(0f, 0.75f, 1f, 0.45f);
    public Material modelMaterialOverride;

    readonly List<Renderer> modelRenderers = new List<Renderer>();
    readonly List<GameObject> outlineObjects = new List<GameObject>();
    readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    Material transparentMaterial;
    Material outlineMaterial;
    Material runtimeModelMaterial;

    void Start()
    {
        if (targetRoot == null)
            targetRoot = transform;

        CacheRenderers();
        SetVisualsEnabled(visualsEnabled);
    }

    // Anchor Switching 用: 「今アクティブなアンカーかどうか」でアウトラインの
    // 太さ/濃さを切り替える。マテリアル自体は共有 (outlineMaterial) なので、
    // 呼ぶだけで即座に反映される(BuildOutlineObjectsの再実行は不要)。
    public void SetAnchorHighlight(bool isActive, Color activeColor, Color inactiveColor,
        float activeWidth, float inactiveWidth)
    {
        outlineColor = isActive ? activeColor : inactiveColor;
        outlineWidth = isActive ? activeWidth : inactiveWidth;
        EnsureMaterials();
    }

    public void SetVisualsEnabled(bool enabled)
    {
        visualsEnabled = enabled;

        if (modelRenderers.Count == 0)
            CacheRenderers();

        if (enabled)
            ApplyTransparentLook();
        else
            RestoreOriginalLook();

        foreach (var outlineObject in outlineObjects)
        {
            if (outlineObject != null)
                outlineObject.SetActive(enabled);
        }
    }

    void CacheRenderers()
    {
        modelRenderers.Clear();
        originalMaterials.Clear();

        if (targetRoot == null)
            return;

        foreach (var renderer in targetRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.gameObject.name.StartsWith("M3T_Outline"))
                continue;

            modelRenderers.Add(renderer);
            originalMaterials[renderer] = renderer.sharedMaterials;
        }

        BuildOutlineObjects();
    }

    void ApplyTransparentLook()
    {
        EnsureMaterials();

        if (!showModelSurface)
        {
            foreach (var renderer in modelRenderers)
                renderer.enabled = false;
            return;
        }

        var material = GetModelMaterial();
        material.color = new Color(modelColor.r, modelColor.g, modelColor.b, opacity);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", material.color);
        material.renderQueue = 3020;

        foreach (var renderer in modelRenderers)
        {
            renderer.enabled = true;

            var materials = new Material[renderer.sharedMaterials.Length];
            for (var i = 0; i < materials.Length; i++)
                materials[i] = material;
            renderer.sharedMaterials = materials;
        }
    }

    void RestoreOriginalLook()
    {
        foreach (var pair in originalMaterials)
        {
            if (pair.Key != null)
            {
                pair.Key.enabled = true;
                pair.Key.sharedMaterials = pair.Value;
            }
        }
    }

    void BuildOutlineObjects()
    {
        foreach (var outlineObject in outlineObjects)
        {
            if (outlineObject != null)
                Destroy(outlineObject);
        }
        outlineObjects.Clear();

        EnsureMaterials();

        if (targetRoot == null)
            return;

        foreach (var meshFilter in targetRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (meshFilter.sharedMesh == null || meshFilter.gameObject.name.StartsWith("M3T_Outline"))
                continue;

            var outline = new GameObject($"M3T_Outline_{meshFilter.gameObject.name}");
            outline.transform.SetParent(meshFilter.transform, false);

            var outlineMeshFilter = outline.AddComponent<MeshFilter>();
            outlineMeshFilter.sharedMesh = meshFilter.sharedMesh;

            var outlineRenderer = outline.AddComponent<MeshRenderer>();
            outlineRenderer.sharedMaterial = outlineMaterial;
            outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;

            outlineObjects.Add(outline);
        }
    }

    void EnsureMaterials()
    {
        if (transparentMaterial == null)
        {
            transparentMaterial = new Material(Shader.Find("Standard"));
            transparentMaterial.SetFloat("_Mode", 3f);
            transparentMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            transparentMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            transparentMaterial.SetInt("_ZWrite", 0);
            transparentMaterial.DisableKeyword("_ALPHATEST_ON");
            transparentMaterial.EnableKeyword("_ALPHABLEND_ON");
            transparentMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            transparentMaterial.renderQueue = 3000;
        }

        if (outlineMaterial == null)
        {
            outlineMaterial = new Material(Shader.Find("M3T/OutlineSilhouette"));
        }

        outlineMaterial.color = outlineColor;
        outlineMaterial.SetFloat("_OutlineWidth", outlineWidth);
    }

    Material GetModelMaterial()
    {
        if (modelMaterialOverride == null)
            return transparentMaterial;

        if (runtimeModelMaterial == null || runtimeModelMaterial.shader != modelMaterialOverride.shader)
            runtimeModelMaterial = new Material(modelMaterialOverride);

        return runtimeModelMaterial;
    }
}
