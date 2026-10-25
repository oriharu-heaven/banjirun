using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 画面版シーン（Flat_Prototype）で再生し、「スタート（カウントダウン）→ ゴール（計測停止・誕生日メッセージ）→ リトライ（スタートに戻る）」が
/// 通ることを自動で検証する。
/// メニュー「BanjiRun/画面版/7. Verify Time Attack」またはバッチ `-executeMethod TimeAttackPlaytest.RunBatch`
/// </summary>
public static class TimeAttackPlaytest
{
    [MenuItem("BanjiRun/画面版/7. Verify Time Attack")]
    public static void RunFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        RunBatch();
    }

    public static void RunBatch()
    {
        EditorSceneManager.OpenScene(FlatSceneBuilder.ScenePath);
        new GameObject("TimeAttackPlaytestRunner").AddComponent<TimeAttackPlaytestRunner>();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorSceneManager.OpenScene(FlatSceneBuilder.ScenePath);
    }
}
