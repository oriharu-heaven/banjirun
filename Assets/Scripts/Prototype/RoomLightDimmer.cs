using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 子階層のライトの明るさを一括調整する。元の強さを記録し、倍率を掛けて反映する。
/// インスペクターのスライダーでリアルタイムに調整可能。
/// </summary>
[ExecuteAlways]
public class RoomLightDimmer : MonoBehaviour
{
    [Range(0f, 1.5f)] public float brightness = 0.03f;
    [Tooltip("ライトの届く距離の上限 (m)。0 で無制限")]
    public float maxRange = 6f;

    [SerializeField, HideInInspector] List<Light> lights = new List<Light>();
    [SerializeField, HideInInspector] List<float> baseIntensities = new List<float>();

    bool probesDimmed;
    bool materialsDimmed;

    void Awake()
    {
        if (Application.isPlaying) Apply();
    }
    void OnEnable() => Apply();
    void Start() => ApplyRuntimeMaterialDimming();
    void OnValidate() => Apply();

    [ContextMenu("Recapture Base Intensities")]
    public void Capture()
    {
        lights.Clear();
        baseIntensities.Clear();
        foreach (Light l in GetComponentsInChildren<Light>(true))
        {
            if (l.type == LightType.Directional) continue; // 太陽光は対象外（室内ライトのみ）
            lights.Add(l);
            baseIntensities.Add(l.intensity);
        }
    }

    public void Apply()
    {
        if (lights.Count == 0) Capture();
        // 旧設定（0.1）で白飛びしていたシーンでも自動で 0.02 に抑える
        float effectiveBrightness = Mathf.Min(brightness, 0.02f);
        bool hasBakedLightmaps = LightmapSettings.lightmaps != null && LightmapSettings.lightmaps.Length > 0;
#if UNITY_EDITOR
        bool isBaking = UnityEditor.Lightmapping.isRunning;
#else
        bool isBaking = false;
#endif
        for (int i = 0; i < lights.Count; i++)
        {
            Light l = lights[i];
            if (l == null) continue;
            l.intensity = baseIntensities[i] * effectiveBrightness;
            l.bounceIntensity = 0.35f;
            if (maxRange > 0f) l.range = Mathf.Min(l.range, maxRange);
            // ベイク済みシーンでは、77個のライトがライトマップの上にリアルタイムで二重に重なって白飛びするのを防ぐ
            if (hasBakedLightmaps && !isBaking) l.enabled = false;
        }
        foreach (Light l in GetComponentsInChildren<Light>(true))
        {
            if (l.type == LightType.Directional)
            {
                l.intensity = Mathf.Min(l.intensity, 0.35f);
                if (hasBakedLightmaps && !isBaking) l.enabled = false;
            }
        }

        RenderSettings.ambientIntensity = 0.06f;
        RenderSettings.reflectionIntensity = 0.04f;
        foreach (var rp in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
            rp.intensity = 0.04f;

        if (Application.isPlaying && !probesDimmed && LightmapSettings.lightProbes != null)
        {
            probesDimmed = true;
            var baked = LightmapSettings.lightProbes.bakedProbes;
            if (baked != null && baked.Length > 0)
            {
                for (int i = 0; i < baked.Length; i++)
                    baked[i] = baked[i] * 0.08f;
                LightmapSettings.lightProbes.bakedProbes = baked;
            }
        }

        if (Application.isPlaying)
            ApplyRuntimeMaterialDimming();
    }

    void ApplyRuntimeMaterialDimming()
    {
        if (!Application.isPlaying || materialsDimmed) return;
        materialsDimmed = true;

        const float baseScale = 0.36f;
        const float whiteWallScale = 0.28f; // 白壁・白椅子などの高明度素材はさらに抑えて白飛びを 0% にする
        const float emissionScale = 0.05f;
        var dimmedMap = new Dictionary<Material, Material>();

        var roots = new List<GameObject> { gameObject };
        var combinedRoot = GameObject.Find("Combined Meshes");
        if (combinedRoot != null) roots.Add(combinedRoot);

        foreach (var root in roots)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var shared = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    var src = shared[i];
                    if (src == null) continue;
                    if (!dimmedMap.TryGetValue(src, out var dst))
                    {
                        dst = new Material(src) { name = src.name + "_Dimmed" };
                        if (dst.HasProperty("_BaseColor"))
                        {
                            Color c = dst.GetColor("_BaseColor");
                            float s = c.maxColorComponent > 0.75f ? whiteWallScale : baseScale;
                            dst.SetColor("_BaseColor", new Color(c.r * s, c.g * s, c.b * s, c.a));
                        }
                        if (dst.HasProperty("_Color"))
                        {
                            Color c = dst.GetColor("_Color");
                            float s = c.maxColorComponent > 0.75f ? whiteWallScale : baseScale;
                            dst.SetColor("_Color", new Color(c.r * s, c.g * s, c.b * s, c.a));
                        }
                        if (dst.HasProperty("_EmissionColor"))
                        {
                            Color e = dst.GetColor("_EmissionColor");
                            dst.SetColor("_EmissionColor", new Color(e.r * emissionScale, e.g * emissionScale, e.b * emissionScale, e.a));
                        }
                        if (dst.HasProperty("_Smoothness"))
                        {
                            dst.SetFloat("_Smoothness", Mathf.Min(dst.GetFloat("_Smoothness"), 0.30f));
                        }
                        dimmedMap[src] = dst;
                    }
                    shared[i] = dst;
                    changed = true;
                }
                if (changed) r.sharedMaterials = shared;
            }
        }
    }
}
