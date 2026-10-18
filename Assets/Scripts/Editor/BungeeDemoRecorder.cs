using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 窓を開ける → バンジー → 着地 → 戻る、を BungeeDemoDirector に自動で操作・録画させる（ffmpeg が要る）。
/// メニュー「BanjiRun/VR/6. Record Bungee Demo」→ Recordings/BungeeDemo.mp4（VR シーン。ヘッドセットなしのキーボード操作で動かす）
/// メニュー「BanjiRun/画面版/3. Record Bungee Demo」→ Recordings/BungeeDemo_Flat.mp4（画面版のシーン。視野の広がりや揺れも映る）
/// コマンドから: Unity -batchmode -projectPath &lt;project&gt; -executeMethod BungeeDemoRecorder.RecordVR（または RecordFlat）
/// （-quit は付けない。終わると自分で閉じる）
/// </summary>
public static class BungeeDemoRecorder
{
    static string scenePath;

    [MenuItem("BanjiRun/VR/6. Record Bungee Demo")]
    public static void RecordVRFromMenu()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) RecordVR();
    }

    [MenuItem("BanjiRun/画面版/3. Record Bungee Demo")]
    public static void RecordFlatFromMenu()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) RecordFlat();
    }

    public static void RecordVR() => Record(VRSceneBuilder.ScenePath, "Recordings/BungeeDemo.mp4");
    public static void RecordFlat() => Record(FlatSceneBuilder.ScenePath, "Recordings/BungeeDemo_Flat.mp4");

    static void Record(string path, string output)
    {
        scenePath = path;
        EditorSceneManager.OpenScene(path);
        new GameObject("BungeeDemoDirector").AddComponent<BungeeDemoDirector>().outputPath = output;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorSceneManager.OpenScene(scenePath); // 再生前に置いた BungeeDemoDirector を消す（シーンには保存していない）
    }
}
