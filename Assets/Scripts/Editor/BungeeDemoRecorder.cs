using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 窓を開ける → バンジー → 着地 → 戻る、を BungeeDemoDirector に自動で操作・録画させる（ffmpeg が要る）。
/// メニュー「BanjiRun/VR/6. Record Bungee Demo」→ Recordings/BungeeDemo.mp4（VR シーン。ヘッドセットなしのキーボード操作で動かす）
/// メニュー「BanjiRun/画面版/3. Record Bungee Demo」→ Recordings/BungeeDemo_Flat.mp4（画面版のシーン。視野の広がりや揺れも映る）
/// ミニキャラで同じ流れ: 「BanjiRun/VR/7. Record Mini Bungee Demo」「BanjiRun/画面版/4. Record Mini Bungee Demo」→ Recordings/BungeeDemo_Mini_VR.mp4 / BungeeDemo_Mini_Flat.mp4
/// コマンドから: Unity -batchmode -projectPath &lt;project&gt; -executeMethod BungeeDemoRecorder.RecordVR（RecordFlat / RecordVRMini / RecordFlatMini）
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

    [MenuItem("BanjiRun/VR/7. Record Mini Bungee Demo")]
    public static void RecordVRMiniFromMenu()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) RecordVRMini();
    }

    [MenuItem("BanjiRun/画面版/4. Record Mini Bungee Demo")]
    public static void RecordFlatMiniFromMenu()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) RecordFlatMini();
    }

    public static void RecordVR() => Record(VRSceneBuilder.ScenePath, "Recordings/BungeeDemo.mp4", false);
    public static void RecordFlat() => Record(FlatSceneBuilder.ScenePath, "Recordings/BungeeDemo_Flat.mp4", false);
    public static void RecordVRMini() => Record(VRSceneBuilder.ScenePath, "Recordings/BungeeDemo_Mini_VR.mp4", true);
    public static void RecordFlatMini() => Record(FlatSceneBuilder.ScenePath, "Recordings/BungeeDemo_Mini_Flat.mp4", true);

    static void Record(string path, string output, bool mini)
    {
        scenePath = path;
        EditorSceneManager.OpenScene(path);
        var director = new GameObject("BungeeDemoDirector").AddComponent<BungeeDemoDirector>();
        director.outputPath = output;
        director.mini = mini;
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
