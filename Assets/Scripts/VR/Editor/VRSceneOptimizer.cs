using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// VR シーン（VRSceneBuilder）を Meta Quest 単体で動く重さにする。
/// - 部屋のライトをすべてベイクする（ライトマップ + ライトプローブ + 反射プローブ）。毎フレーム計算するライトは 0 個になる
/// - 動かない物を Static にする（ライトマップ・オクルージョン用。描画はビルド時に VRMeshCombiner がまとめる）
/// - コライダーを箱にする。手の届かない天井まわりと小さな飾りには付けない
/// ドア（Door）とその部品は動くので、どれも対象外。
/// </summary>
public static class VRSceneOptimizer
{
    const string ModelPath = "Assets/akiba_school_room.blend";
    const string LightingSettingsPath = "Assets/Settings/VR_Lighting.lighting";

    // ライトマップの細かさ（1m あたりの画素数）。上げると綺麗になるが、ベイクが遅くなり Quest のメモリも食う
    const float LightmapResolution = 25f;
    const int LightmapMaxSize = 1024;

    // 室内ライトの明るさ（RoomLightDimmer の倍率。パソコン版は 0.3）。ベイクすると壁や天井に跳ね返った光も入るので、暗めにしてパソコン版と同じくらいの明るさにする
    const float BakedBrightness = 0.1f;

    // 描画をまとめるのは VRMeshCombiner がビルド時にするので、Unity の静的バッチ（BatchingStatic）は付けない（先に掛かるとまとめられなくなる）
    const StaticEditorFlags StaticFlags = StaticEditorFlags.ContributeGI
        | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

    // この大きさ（m）未満の物はライトマップを使わず、ライトプローブで照らす（ライトマップの空きとベイク時間の節約）
    const float ProbeLitSize = 0.3f;
    // 窓の外の街は遠くから見るだけなので、ライトマップを粗くする
    const string ExteriorGroup = "Grp_Exterior";
    const float ExteriorLightmapScale = 0.1f;

    // コライダー: この大きさ未満の飾りと、下端がこの高さより上の物（天井まわり）には付けない。
    // 残りは箱にする。ただし、これより大きくて箱の形でない物は MeshCollider のまま
    const float TinyColliderSize = 0.1f;
    const float ReachHeight = 2f;
    const float MaxBoxColliderSize = 3f;
    const float MinBoxThickness = 0.01f;

    // ライトプローブ（ドア・手などの動く物と、小さな物を照らす）を並べる床と、床からの高さ・間隔
    static readonly string[] ProbeFloors = { "Floor_Hall", "Floor_Entrance", "Floor_Annex", "Balcony_Floor", "Car_Floor" };
    static readonly float[] ProbeHeights = { 0.3f, 1.2f, 2.1f };
    const float ProbeSpacing = 1.2f;

    // オクルージョンカリング: これより小さい物は壁として扱わない / これより小さい隙間は見通せないとみなす（m）
    const float SmallestOccluder = 1f;
    const float SmallestHole = 0.1f;

    // 反射プローブ（光沢のある床や机に、空ではなく部屋が映るように）を置く部屋の床と、部屋の高さ
    static readonly string[] ReflectionFloors = { "Floor_Hall", "Floor_Entrance", "Floor_Annex" };
    const float RoomHeight = 3f;

    /// <summary>.blend の取り込みでライトマップ用の UV を作る（パソコン版の見た目は変わらない）。設定を変えたときだけ取り込み直す。</summary>
    public static void EnsureLightmapUVs()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        if (importer == null || (importer.generateSecondaryUV && importer.secondaryUVMinLightmapResolution == LightmapResolution)) return;
        importer.generateSecondaryUV = true;
        importer.secondaryUVMarginMethod = ModelImporterSecondaryUVMarginMethod.Calculate;
        importer.secondaryUVMinLightmapResolution = LightmapResolution;
        importer.SaveAndReimport();
        Debug.Log("[VR] 部屋のモデルにライトマップ用の UV を作った（取り込み直し）");
    }

    /// <summary>部屋を Static にし、コライダーを減らし、ライトをベイク用にする。テレポート先を付ける前に呼ぶ（床のコライダーが箱に変わるため）。</summary>
    public static void PrepareRoom(GameObject room)
    {
        var moving = new HashSet<Transform>(room.GetComponentsInChildren<Door>(true).SelectMany(d => d.GetComponentsInChildren<Transform>(true)));
        var exterior = room.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == ExteriorGroup);

        int statics = 0, probeLit = 0;
        foreach (var t in room.GetComponentsInChildren<Transform>(true))
        {
            if (t == room.transform || moving.Contains(t)) continue;
            var renderer = t.GetComponent<MeshRenderer>();
            // 透明な物（ガラス）は、向こうが見えるので「隠す物」にしない
            bool transparent = renderer != null && renderer.sharedMaterials.Any(m => m != null && m.renderQueue >= (int)RenderQueue.Transparent);
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, transparent ? StaticFlags & ~StaticEditorFlags.OccluderStatic : StaticFlags);
            statics++;
            if (renderer == null) continue;
            if (MaxSize(renderer.bounds) < ProbeLitSize)
            {
                renderer.receiveGI = ReceiveGI.LightProbes;
                probeLit++;
            }
            else if (exterior != null && t.IsChildOf(exterior)) renderer.scaleInLightmap = ExteriorLightmapScale;
        }

        int exact = 0, approx = 0, removed = 0, kept = 0;
        foreach (var meshCollider in room.GetComponentsInChildren<MeshCollider>(true))
        {
            if (moving.Contains(meshCollider.transform)) continue;
            var mesh = meshCollider.sharedMesh;
            var bounds = meshCollider.TryGetComponent<Renderer>(out var r) ? r.bounds : meshCollider.bounds;
            float size = MaxSize(bounds);
            bool large = size >= MaxBoxColliderSize;
            bool box = mesh != null && IsBox(mesh);
            if (mesh == null || size < TinyColliderSize || (bounds.min.y > ReachHeight && !large))
            {
                removed++;
            }
            else if (large && !box)
            {
                kept++;
                continue;
            }
            else
            {
                var collider = meshCollider.gameObject.AddComponent<BoxCollider>();
                collider.center = mesh.bounds.center;
                collider.size = Vector3.Max(mesh.bounds.size, Vector3.one * MinBoxThickness);
                if (box) exact++;
                else approx++;
            }
            Object.DestroyImmediate(meshCollider);
        }

        // 毎フレームのライト計算をやめ、明るさはライトマップに焼き込む。動く物はライトプローブで照らす
        var lights = room.GetComponentsInChildren<Light>(true);
        foreach (var light in lights) light.lightmapBakeType = LightmapBakeType.Baked;
        if (room.TryGetComponent<RoomLightDimmer>(out var dimmer))
        {
            dimmer.brightness = BakedBrightness;
            dimmer.Apply();
        }

        Debug.Log($"[VR] Quest 向けの軽量化: Static {statics} 個（うち小物 {probeLit} 個はライトプローブで照らす）, " +
                  $"コライダー 箱 {exact + approx} 個（形どおり {exact} / 外形の箱 {approx}）・外した {removed} 個・MeshCollider のまま {kept} 個, ライト {lights.Length} 個をベイクに");
    }

    /// <summary>ライトプローブと反射プローブを置く。コライダーを整えたあとに呼ぶ（物にめり込むプローブを除くため）。</summary>
    public static void AddProbes(GameObject room)
    {
        Physics.SyncTransforms();
        var floors = room.GetComponentsInChildren<MeshRenderer>(true).GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First().bounds);

        var positions = new List<Vector3>();
        foreach (string name in ProbeFloors)
        {
            if (!floors.TryGetValue(name, out var b)) continue;
            int nx = Mathf.Max(1, Mathf.RoundToInt(b.size.x / ProbeSpacing));
            int nz = Mathf.Max(1, Mathf.RoundToInt(b.size.z / ProbeSpacing));
            for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            foreach (float height in ProbeHeights)
            {
                var p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, (ix + 0.5f) / nx), b.max.y + height, Mathf.Lerp(b.min.z, b.max.z, (iz + 0.5f) / nz));
                if (!Physics.CheckSphere(p, 0.05f)) positions.Add(p);
            }
        }
        new GameObject("Light Probes").AddComponent<LightProbeGroup>().probePositions = positions.ToArray();

        int reflections = 0;
        foreach (string name in ReflectionFloors)
        {
            if (!floors.TryGetValue(name, out var b)) continue;
            var go = new GameObject($"Reflection Probe ({name})");
            go.transform.position = new Vector3(b.center.x, b.max.y + RoomHeight / 2f, b.center.z);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(b.size.x, RoomHeight, b.size.z);
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.hdr = false; // Quest は HDR オフで描くので、反射も 8bit で軽く
            reflections++;
        }
        Debug.Log($"[VR] ライトプローブ {positions.Count} 点, 反射プローブ {reflections} 個");
    }

    /// <summary>保存したシーンのライトをベイクする（ライトマップ・ライトプローブ・反射プローブ）。</summary>
    public static bool Bake()
    {
        Lightmapping.lightingSettings = GetLightingSettings();
        var started = System.DateTime.Now;
        bool ok = Lightmapping.Bake();
        string sizes = string.Join(" ", LightmapSettings.lightmaps.Select(m => m.lightmapColor).Where(t => t != null).Select(t => $"{t.width}x{t.height}"));
        Debug.Log($"[VR] ライトのベイク: {(ok ? "完了" : "失敗")} / ライトマップ {LightmapSettings.lightmaps.Length} 枚 {sizes} / {(System.DateTime.Now - started).TotalSeconds:F0} 秒");
        if (!ok) Debug.LogError("[VR] ライトのベイクに失敗しました。Window > Rendering > Lighting で Generate Lighting を試してください");
        return ok;
    }

    /// <summary>壁の向こうの物を描かないためのデータを作る（オクルージョンカリング）。部屋が壁で区切られているので効く。</summary>
    public static bool BakeOcclusion()
    {
        StaticOcclusionCulling.smallestOccluder = SmallestOccluder;
        StaticOcclusionCulling.smallestHole = SmallestHole;
        StaticOcclusionCulling.backfaceThreshold = 100f;
        var started = System.DateTime.Now;
        bool ok = StaticOcclusionCulling.Compute();
        Debug.Log($"[VR] オクルージョンカリング: {(ok ? "完了" : "失敗")} / {(System.DateTime.Now - started).TotalSeconds:F0} 秒");
        return ok;
    }

    static LightingSettings GetLightingSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
        if (settings == null)
        {
            settings = new LightingSettings { name = "VR_Lighting" };
            AssetDatabase.CreateAsset(settings, LightingSettingsPath);
        }
        settings.bakedGI = true;
        settings.realtimeGI = false;
        // UnityComputeGPU は 78 個のライトから少しずつ選んで計算するため、小さな部屋のライトが拾われず暗く粒っぽくなる。範囲内のライトを全部計算する方式を使う
        settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        settings.lightmapResolution = LightmapResolution;
        settings.lightmapMaxSize = LightmapMaxSize;
        settings.lightmapPadding = 2;
        settings.directionalityMode = LightmapsMode.NonDirectional; // 方向付きは 2 倍のメモリと計算を使うので Quest では使わない
        settings.lightmapCompression = LightmapCompression.NormalQuality;
        settings.ao = true;
        settings.aoMaxDistance = 0.5f;
        settings.directSampleCount = 64;
        settings.indirectSampleCount = 512;
        settings.environmentSampleCount = 128;
        settings.maxBounces = 2;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return settings;
    }

    static float MaxSize(Bounds b) => Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));

    /// <summary>頂点がすべて外形の箱の面の上にある（＝箱そのもの。壁・床・柱など）か。</summary>
    static bool IsBox(Mesh mesh)
    {
        if (mesh.vertexCount > 24) return false;
        var b = mesh.bounds;
        const float e = 0.0005f;
        bool OnFace(float v, float min, float max) => Mathf.Abs(v - min) < e || Mathf.Abs(v - max) < e;
        return mesh.vertices.All(v => OnFace(v.x, b.min.x, b.max.x) && OnFace(v.y, b.min.y, b.max.y) && OnFace(v.z, b.min.z, b.max.z));
    }
}
