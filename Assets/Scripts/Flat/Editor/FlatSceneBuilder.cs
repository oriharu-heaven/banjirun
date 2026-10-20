using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// メニュー「BanjiRun/画面版/...」: VR を使わずに同じ体験（等身大の一人称で窓から出てバンジー）を画面で遊ぶ版。
/// 部屋とバンジーは GameSceneBuilder（VR 版と共通）。ここで置くのはプレイヤー（体・操作・画面の表示・画面だけの演出）。
/// Mac 用のアプリと、ブラウザ版（unityroom に上げる WebGL）も作れる（XR は使わない。XR Plug-in Management の Standalone・WebGL は空のまま）。
/// </summary>
public static class FlatSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Flat_Prototype.unity";
    const string MacAppPath = "Builds/Mac/BanjiRun.app";
    const string WebGLRoot = "Builds/WebGL";
    const string FontPath = "Assets/Fonts/MPLUSRounded1c-Bold.ttf";

    const float EyeHeight = 1.6f;
    // 体の大きさは VR の体（XRI が頭の高さに合わせる CharacterController）とそろえる。
    // 太い・背が高いと、机の間の通路（約 0.6m）や、舞台の縁から窓をまたぐ所で引っかかる
    const float BodyHeight = 1.6f;
    const float BodyRadius = 0.1f;

    [MenuItem("BanjiRun/画面版/1. Build Flat Scene")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    public static void Build()
    {
        if (!GameSceneBuilder.CreateWorld(out var world)) return;
        var player = CreatePlayer(world.spawn);
        GameSceneBuilder.SaveAndBake(world.scene, ScenePath);
        Selection.activeGameObject = player;
        Debug.Log($"[画面版] シーン生成完了: {ScenePath} / バンジー {(world.bungee != null ? "あり" : "なし")}, spawn {world.spawn}");
    }

    static GameObject CreatePlayer(Vector3 spawn)
    {
        var player = new GameObject("Player");
        player.transform.SetPositionAndRotation(spawn, Quaternion.identity);
        var controller = player.AddComponent<CharacterController>();
        controller.height = BodyHeight;
        controller.radius = BodyRadius;
        controller.center = new Vector3(0f, BodyHeight / 2f + 0.08f, 0f); // XRI と同じく、すき間（skinWidth）の分だけ上げて足を床に付ける
        controller.minMoveDistance = 0f;
        // 段を上がる高さ。高くすると、舞台（20cm）の上で天井のライトレール（2.28m）に頭が当たったとみなされて進めなくなる。
        // 上がる段で一番高いのはバンジーの階段の 29cm
        controller.stepOffset = 0.3f;
        controller.slopeLimit = 45f;
        controller.skinWidth = 0.08f;

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        camGo.transform.SetParent(player.transform, false);
        camGo.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;
        cam.fieldOfView = 70f;
        camGo.AddComponent<AudioListener>();

        var body = player.AddComponent<FlatPlayerBody>();
        var so = new SerializedObject(body);
        so.FindProperty("head").objectReferenceValue = cam;
        so.ApplyModifiedPropertiesWithoutUndo();
        player.AddComponent<FirstPersonControls>().eyeHeight = EyeHeight;
        player.AddComponent<ClimbAssist>(); // ミニキャラのとき、段をよじ登る
        player.AddComponent<FlatHUD>().font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        player.AddComponent<FlatCameraEffects>();
        return player;
    }

    [MenuItem("BanjiRun/画面版/2. Build Mac App")]
    public static void BuildMacApp()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = MacAppPath,
            target = BuildTarget.StandaloneOSX,
            targetGroup = BuildTargetGroup.Standalone,
        };
        var summary = BuildPipeline.BuildPlayer(options).summary;
        Debug.Log($"[画面版] Mac アプリのビルド: {summary.result} / {Path.GetFullPath(MacAppPath)} / {summary.totalTime}");
        if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    /// <summary>
    /// ブラウザ版（unityroom 用）。Builds/WebGL/BanjiRun_日時/Build/ の 4 ファイルを unityroom の「WebGL設定」に上げる。
    /// unityroom の決まり（LG の作品と同じ）: Gzip で圧縮・ブラウザでの解凍なし・WebGL2・ファイル名はハッシュにしない。
    /// 画質はブラウザ向けの Mobile 段階（Quest と同じ）。
    /// </summary>
    [MenuItem("BanjiRun/画面版/5. Build WebGL (unityroom)")]
    public static void BuildWebGL()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.nameFilesAsHashes = false;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        PlayerSettings.WebGL.threadsSupport = false;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.defaultWebScreenWidth = 960;
        PlayerSettings.defaultWebScreenHeight = 540;
        PlayerSettings.runInBackground = false;
        PlayerSettings.SplashScreen.show = false;
        // コードの最適化（WebGL モジュール側の設定なので、名前で探して入れる）
        var userSettings = Type.GetType("UnityEditor.WebGL.UserBuildSettings, UnityEditor.WebGL.Extensions");
        var optimization = userSettings?.GetProperty("codeOptimization");
        if (optimization != null) optimization.SetValue(null, Enum.Parse(optimization.PropertyType, "DiskSizeLTO"));
        AssetDatabase.SaveAssets();

        string dir = Path.Combine(WebGLRoot, $"BanjiRun_{DateTime.Now:yyyyMMdd-HHmm}");
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = dir,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
        };
        var summary = BuildPipeline.BuildPlayer(options).summary;
        Debug.Log($"[画面版] WebGL のビルド: {summary.result} / {Path.GetFullPath(dir)}/Build / {summary.totalSize / 1048576f:F0} MB / {summary.totalTime}");
        if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
