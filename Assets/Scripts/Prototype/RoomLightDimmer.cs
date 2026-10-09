using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 子階層のライトの明るさを一括調整する。元の強さを記録し、倍率を掛けて反映する。
/// インスペクターのスライダーでリアルタイムに調整可能。
/// </summary>
[ExecuteAlways]
public class RoomLightDimmer : MonoBehaviour
{
    [Range(0f, 1.5f)] public float brightness = 0.3f;
    [Tooltip("ライトの届く距離の上限 (m)。0 で無制限")]
    public float maxRange = 6f;

    [SerializeField, HideInInspector] List<Light> lights = new List<Light>();
    [SerializeField, HideInInspector] List<float> baseIntensities = new List<float>();

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
        for (int i = 0; i < lights.Count; i++)
        {
            Light l = lights[i];
            if (l == null) continue;
            l.intensity = baseIntensities[i] * brightness;
            if (maxRange > 0f) l.range = Mathf.Min(l.range, maxRange);
        }
    }
}
