using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// メニュー「BanjiRun/VR/6. Record Bungee Demo」: VR シーンを開いて再生し、BungeeDemoDirector に
/// 窓を開ける → バンジー → 着地 → 戻る、を自動で操作・録画させる。出力は Recordings/BungeeDemo.mp4（ffmpeg が要る）。
/// コマンドから: Unity -batchmode -projectPath &lt;project&gt; -executeMethod BungeeDemoRecorder.Record（-quit は付けない。終わると自分で閉じる）
/// </summary>
public static class BungeeDemoRecorder
{
    const string ScenePath = "Assets/Scenes/VR_Prototype.unity";

    [MenuItem("BanjiRun/VR/6. Record Bungee Demo")]
    public static void RecordFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Record();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene(ScenePath);
        new GameObject("BungeeDemoDirector").AddComponent<BungeeDemoDirector>();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorSceneManager.OpenScene(ScenePath); // 再生前に置いた BungeeDemoDirector を消す（シーンには保存していない）
    }
}
