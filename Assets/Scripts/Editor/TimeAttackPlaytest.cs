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
        foreach (var guid in AssetDatabase.FindAssets("Lightmap-", new[] { "Assets/Scenes" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.EndsWith(".exr")) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        }

        string[] scenes = { "Assets/Scenes/VR_Prototype.unity", FlatSceneBuilder.ScenePath };
        foreach (var sPath in scenes)
        {
            if (!System.IO.File.Exists(sPath)) continue;
            var s = EditorSceneManager.OpenScene(sPath);
            var dimmer = Object.FindFirstObjectByType<RoomLightDimmer>();
            if (dimmer != null)
            {
                dimmer.brightness = 0.02f;
                dimmer.Apply();
                foreach (var t in dimmer.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith("EV_CarDoors") || t.name.StartsWith("EV_LandingDoors") || t.name.StartsWith("CasterWagon"))
                    {
                        foreach (var child in t.GetComponentsInChildren<Transform>(true))
                        {
                            GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
                            if (child.TryGetComponent<MeshRenderer>(out var mr))
                            {
                                mr.receiveGI = ReceiveGI.LightProbes;
                                mr.lightmapIndex = -1;
                            }
                        }
                    }
                }
            }
            EditorSceneManager.SaveScene(s);
        }

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
