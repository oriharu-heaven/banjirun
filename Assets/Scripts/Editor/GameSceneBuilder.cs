using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// VR 版と画面版のシーンで共通の部分を作る: 部屋（試作と同じモデル）・窓とドア・軽量化・窓の外のバンジージャンプ・ライトのベイク。
/// プレイヤー（体と操作）だけが違い、それは VRSceneBuilder / FlatSceneBuilder が置く。
///   1. CreateWorld で新しいシーンに部屋とバンジーを作る
///   2. 呼んだ側がプレイヤーなどを置く
///   3. SaveAndBake で保存してライトをベイクする
/// </summary>
public static class GameSceneBuilder
{
    public const string DesktopPrototypeScenePath = "Assets/Scenes/Prototype.unity"; // 小人の試作（作者 Haru さんの元のゲーム）

    /// <summary>軽量化（SceneMeshCombiner）を掛けるシーン</summary>
    public static readonly string[] OptimizedScenes = { VRSceneBuilder.ScenePath, FlatSceneBuilder.ScenePath };

    public struct World
    {
        public Scene scene;
        public GameObject room;
        public Vector3 spawn;
        public BungeeJump bungee;
    }

    /// <summary>VR 版と画面版のシーンを両方作り直す（コマンドからは -executeMethod GameSceneBuilder.BuildAllScenes）。</summary>
    [MenuItem("BanjiRun/Build All Scenes (VR + 画面版)")]
    public static void BuildAllScenes()
    {
        VRSceneBuilder.Build();
        FlatSceneBuilder.Build();
    }

    public static bool CreateWorld(out World world)
    {
        world = default;
        SceneOptimizer.EnsureLightmapUVs();
        var modelAsset = PrototypeSceneBuilder.LoadRoomModel();
        if (modelAsset == null) return false;

        world.scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        world.room = PrototypeSceneBuilder.CreateRoom(modelAsset, out world.spawn);
        SceneOptimizer.PrepareRoom(world.room);
        SceneOptimizer.AddProbes(world.room);
        world.bungee = BungeeBuilder.Create(world.room);
        return true;
    }

    /// <summary>ベイクは保存したシーンにしかできないので、保存 → ベイク → もう一度保存。</summary>
    public static void SaveAndBake(Scene scene, string path)
    {
        EditorSceneManager.SaveScene(scene, path);
        SceneOptimizer.Bake();
        SceneOptimizer.BakeOcclusion();
        EditorSceneManager.SaveScene(scene);
        UpdateBuildSettings();
    }

    /// <summary>
    /// ビルドするシーンの一覧。Quest 用（VR）が先頭。画面版と小人の試作は一覧に入れておくだけ（チェックなし）。
    /// メニューのビルド（Quest APK / Mac App）は、この一覧ではなくそれぞれのシーンだけを使う。
    /// </summary>
    static void UpdateBuildSettings()
    {
        string[] order = { VRSceneBuilder.ScenePath, FlatSceneBuilder.ScenePath, DesktopPrototypeScenePath };
        EditorBuildSettings.scenes = order
            .Where(p => AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null)
            .Select(p => new EditorBuildSettingsScene(p, p == VRSceneBuilder.ScenePath))
            .ToArray();
    }
}
