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

    void OnEnable() => Apply();
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
        // 旧設定（0.1）で白飛びしていたシーンでも自動で 0.03 に抑える
        float effectiveBrightness = Mathf.Min(brightness, 0.035f);
        for (int i = 0; i < lights.Count; i++)
        {
            Light l = lights[i];
            if (l == null) continue;
            l.intensity = baseIntensities[i] * effectiveBrightness;
            l.bounceIntensity = 0.5f;
            if (maxRange > 0f) l.range = Mathf.Min(l.range, maxRange);
        }
        foreach (Light l in GetComponentsInChildren<Light>(true))
        {
            if (l.type == LightType.Directional) l.intensity = Mathf.Min(l.intensity, 1.2f);
        }

        RenderSettings.ambientIntensity = 0.45f;
        RenderSettings.reflectionIntensity = 0.45f;
        foreach (var rp in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
            rp.intensity = 0.35f;

        if (Application.isPlaying && !probesDimmed && LightmapSettings.lightProbes != null)
        {
            probesDimmed = true;
            var baked = LightmapSettings.lightProbes.bakedProbes;
            if (baked != null && baked.Length > 0)
            {
                for (int i = 0; i < baked.Length; i++)
                    baked[i] = baked[i] * 0.35f;
                LightmapSettings.lightProbes.bakedProbes = baked;
            }
        }
    }
}
