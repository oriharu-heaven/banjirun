using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// メニュー「BanjiRun/VR/5. Scene Stats」で、VR シーンの重さの目安をログと Builds/SceneStats/&lt;名前&gt;/report.txt に出し、
/// 決まった場所から見た画像（Quest と同じ Mobile 画質 = quest_*.png、パソコンの PC 画質 = pc_*.png）を保存する。
/// 数えるもの: リアルタイムのライト, コライダーの種類, 描画の回数（Draw Calls / SetPass / 三角形。Mobile 画質・片目ぶん）。
/// メッシュのまとめ（SceneMeshCombiner）と静的バッチはビルド時にしか掛からないので、同じ処理を一時的に掛けてから数える。シーンは保存しない。
/// コマンド: Unity -batchmode -projectPath . -buildTarget Android -executeMethod VRSceneStats.RunBatch -statsLabel before
/// （-quit は付けない。描画の回数はエディターのフレームが進まないと取れないので、終わったら自分で終了する）
/// </summary>
public static class VRSceneStats
{
    const string ScenePath = "Assets/Scenes/VR_Prototype.unity";
    const string OutFolder = "Builds/SceneStats";
    const float EyeHeight = 1.6f;
    const float FieldOfView = 96f; // Quest 3 の片目の縦の視野くらい
    const int ImageSize = 768;

    // 見る場所: 床の上の位置（床の範囲を 0〜1 で。x が小さい側が窓、z が小さい側が北の壁）から、別の位置の高さ 1.2m を見る
    struct View
    {
        public string name, floor;
        public Vector2 from, to;
        public View(string name, string floor, float fx, float fz, float tx, float tz)
        {
            this.name = name; this.floor = floor; from = new Vector2(fx, fz); to = new Vector2(tx, tz);
        }
    }

    static readonly View[] Views =
    {
        new View("Hall_North", "Floor_Hall", 0.5f, 0.5f, 0.5f, 0f),
        new View("Hall_South", "Floor_Hall", 0.5f, 0.5f, 0.5f, 1f),
        new View("Hall_Window", "Floor_Hall", 0.5f, 0.5f, 0f, 0.5f),
        new View("Hall_West", "Floor_Hall", 0.5f, 0.5f, 1f, 0.5f),
        new View("Hall_Corner", "Floor_Hall", 0.95f, 0.05f, 0.3f, 0.7f),
        new View("Entrance", "Floor_Entrance", 0.05f, 0.5f, 1f, 0.5f),
        new View("Entrance_Back", "Floor_Entrance", 0.95f, 0.5f, 0f, 0.5f),
        new View("Annex", "Floor_Annex", 0.1f, 0.9f, 0.7f, 0.2f),
        new View("Annex_Back", "Floor_Annex", 0.6f, 0.1f, 0f, 0.9f),
    };

    static readonly string[] Columns = { "Draw Calls", "SetPass", "三角形" };

    static List<(string name, Vector3 position, Quaternion rotation)> views;
    static Camera camera;
    static RenderTexture target;
    static long[][] results;
    static int step, viewIndex, renderedFrame, waitTicks;
    static string outDir;
    static StringBuilder report;
    static int qualityBefore;
    static bool exitWhenDone;

    [MenuItem("BanjiRun/VR/5. Scene Stats")]
    public static void RunFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Run("latest", false);
    }

    public static void RunBatch()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-statsLabel");
        Run(i >= 0 && i + 1 < args.Length ? args[i + 1] : "latest", true);
    }

    static void Run(string label, bool exit)
    {
        exitWhenDone = exit;
        outDir = Path.Combine(OutFolder, label);
        Directory.CreateDirectory(outDir);
        EditorSceneManager.OpenScene(ScenePath);
        report = new StringBuilder($"VR シーンの重さ（{label}）\n");
        CountScene();

        // 頭・手（XR Origin）は前後で変わらないので、描画の回数からは外す
        foreach (var origin in Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Exclude)) origin.gameObject.SetActive(false);
        views = FindViews(); // まとめると床の MeshRenderer が消えるので、その前に
        ApplyStaticBatching();

        results = new long[views.Count][];
        camera = new GameObject("SceneStatsCamera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 1000f;
        camera.fieldOfView = FieldOfView;
        target = new RenderTexture(ImageSize, ImageSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        camera.targetTexture = target;

        qualityBefore = QualitySettings.GetQualityLevel();
        SetQuality("Mobile");
        step = viewIndex = 0;
        renderedFrame = -1;
        EditorApplication.update += Tick;
    }

    static void CountScene()
    {
        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        int realtime = lights.Count(l => l.lightmapBakeType == LightmapBakeType.Realtime && l.type != LightType.Rectangle && l.type != LightType.Disc);
        int mixed = lights.Count(l => l.lightmapBakeType == LightmapBakeType.Mixed);
        int baked = lights.Length - realtime - mixed;
        Line($"ライト: {lights.Length} 個（毎フレーム計算 = リアルタイム {realtime} + Mixed {mixed} / ベイクのみ {baked}。面光源はリアルタイムでは光らないのでベイク側に数える）");

        var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude);
        string byType = string.Join(", ", colliders.GroupBy(c => c.GetType().Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"));
        Line($"コライダー: {colliders.Length} 個（{byType}）");

        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude);
        int batchingStatic = renderers.Count(r => GameObjectUtility.AnyStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic));
        int lightmapped = renderers.Count(r => r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE);
        long triangles = renderers.Select(r => r.GetComponent<MeshFilter>()?.sharedMesh).Where(m => m != null).Sum(m => (long)m.triangles.Length / 3);
        Line($"メッシュの描画: {renderers.Length} 個, 三角形 {triangles:N0}, 静的バッチ対象 {batchingStatic}, ライトマップあり {lightmapped}");

        var maps = LightmapSettings.lightmaps;
        string sizes = string.Join(" ", maps.Select(m => m.lightmapColor).Where(t => t != null).Select(t => $"{t.width}x{t.height}"));
        int probes = LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0;
        int reflection = Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude).Length;
        Line($"ライトマップ: {maps.Length} 枚 {sizes} / ライトプローブ {probes} 点 / 反射プローブ {reflection} 個");
    }

    static void ApplyStaticBatching()
    {
        // ビルドと同じ順に、先に SceneMeshCombiner でまとめ、残った Static の物に静的バッチを掛ける
        var room = Object.FindObjectsByType<RoomLightDimmer>(FindObjectsInactive.Exclude).FirstOrDefault();
        if (room != null) Line($"メッシュをまとめる（SceneMeshCombiner）: {SceneMeshCombiner.Combine(room.gameObject)} 個に");
        var statics = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude)
            .Where(r => GameObjectUtility.AnyStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic))
            .Select(r => r.gameObject).ToArray();
        bool enabled = PlayerSettings.GetStaticBatchingForPlatform(BuildTarget.Android);
        if (statics.Length > 0 && enabled) StaticBatchingUtility.Combine(statics, null);
        Line($"静的バッチ（Android の設定 {(enabled ? "オン" : "オフ")}）: {(enabled ? statics.Length : 0)} 個をまとめて数える");
    }

    static List<(string, Vector3, Quaternion)> FindViews()
    {
        var floors = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude).GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First().bounds);
        var list = new List<(string, Vector3, Quaternion)>();
        foreach (var view in Views)
        {
            if (!floors.TryGetValue(view.floor, out var b)) continue;
            Vector3 At(Vector2 uv, float height) => new Vector3(Mathf.Lerp(b.min.x, b.max.x, uv.x), b.max.y + height, Mathf.Lerp(b.min.z, b.max.z, uv.y));
            Vector3 eye = At(view.from, EyeHeight);
            list.Add((view.name, eye, Quaternion.LookRotation(At(view.to, 1.2f) - eye)));
        }
        return list;
    }

    static void Tick()
    {
        // step 0: Mobile 画質で 1 フレームに 1 方向ずつ描き、フレームが進んだら描画の回数を読む / step 1: PC 画質で画像だけ / step 2: まとめ
        if (renderedFrame >= 0)
        {
            if (Time.frameCount <= renderedFrame && ++waitTicks < 300)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }
            if (step == 0)
                results[viewIndex] = new long[] { UnityStats.drawCalls, UnityStats.setPassCalls, UnityStats.triangles };
            renderedFrame = -1;
            viewIndex++;
        }

        if (viewIndex >= views.Count)
        {
            viewIndex = 0;
            step++;
            if (step == 1) SetQuality("PC");
        }
        if (step >= 2)
        {
            Finish();
            return;
        }

        var view = views[viewIndex];
        camera.transform.SetPositionAndRotation(view.position, view.rotation);
        camera.Render();
        SaveImage(Path.Combine(outDir, $"{(step == 0 ? "quest" : "pc")}_{view.name}.png"));
        renderedFrame = Time.frameCount;
        waitTicks = 0;
        EditorApplication.QueuePlayerLoopUpdate();
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;
        Line("描画の回数（Mobile 画質・片目・頭と手を除く）: " + string.Join(" / ", Columns));
        for (int i = 0; i < views.Count; i++)
            Line($"  {views[i].name}: " + string.Join(" / ", results[i].Select(v => v.ToString("N0"))));
        Line("  平均: " + string.Join(" / ", Enumerable.Range(0, Columns.Length).Select(c => results.Average(r => r[c]).ToString("N0"))));
        Line("  最大: " + string.Join(" / ", Enumerable.Range(0, Columns.Length).Select(c => results.Max(r => r[c]).ToString("N0"))));
        File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());

        QualitySettings.SetQualityLevel(qualityBefore, true);
        Object.DestroyImmediate(camera.gameObject);
        target.Release();
        Debug.Log($"[Stats] 完了: {Path.GetFullPath(outDir)}");
        if (exitWhenDone) EditorApplication.Exit(0);
        else EditorSceneManager.OpenScene(ScenePath); // 静的バッチを掛けたシーンを捨てて開き直す
    }

    static void SetQuality(string name)
    {
        int level = System.Array.IndexOf(QualitySettings.names, name);
        if (level >= 0) QualitySettings.SetQualityLevel(level, true);
    }

    static void SaveImage(string path)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(ImageSize, ImageSize, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0);
        RenderTexture.active = previous;
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    static void Line(string text)
    {
        report.AppendLine(text);
        Debug.Log("[Stats] " + text);
    }
}
