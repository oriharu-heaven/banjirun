using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

/// <summary>
/// メニュー「BanjiRun/VR/3. Build VR Scene」で、試作と同じ部屋に VR プレイヤー（XRI の XR Origin）を置いたシーンを生成する。
/// 移動: 左スティック, 向き: 右スティック左右, テレポート: 右スティックを前に倒して離す, ジャンプ: A, ドア: 触れるかレイを当ててグリップ
/// ヘッドセットが無いとき（Mac のエディター）は VRDesktopControls のキーボード・マウス操作になる。
/// ホール東の窓から外へ出ると、バルコニーにバンジージャンプ（BungeeBuilder）。
/// </summary>
public static class VRSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/VR_Prototype.unity";
    const string DesktopScenePath = "Assets/Scenes/Prototype.unity";

    // プレイヤーの大きさ。1 = 等身大（自分の背の高さ）。小さくすると部屋が大きく見える（0.15 で身長 25cm ほど）
    const float PlayerScale = 1f;

    // テレポートで降りられる床
    static readonly string[] TeleportFloors =
    {
        "Floor_Hall", "Floor_Entrance", "Floor_Annex", "Balcony_Floor", "Stage_Module1_Top", "Stage_Module2_Top",
        "Car_Floor", "EV_Gap_Floor", "Sidewalk_Near", "Sidewalk_Far", "Road",
    };

    [MenuItem("BanjiRun/VR/3. Build VR Scene")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    public static void Build()
    {
        var rigPrefab = VRProjectSetup.FindSampleAsset<GameObject>("XR Origin (XR Rig)");
        if (rigPrefab == null)
        {
            Debug.LogError("[VR] XR Origin (XR Rig) が見つかりません。先に「1. Import XRI Samples」を実行してください");
            return;
        }
        VRSceneOptimizer.EnsureLightmapUVs();
        var modelAsset = PrototypeSceneBuilder.LoadRoomModel();
        if (modelAsset == null) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var room = PrototypeSceneBuilder.CreateRoom(modelAsset, out Vector3 spawn);

        // ドアをコントローラーで開け閉めできるように（XRSimpleInteractable は RequireComponent で付く）
        var doors = room.GetComponentsInChildren<Door>(true);
        foreach (var door in doors) door.gameObject.AddComponent<VRDoorHandle>();

        // Quest 向けの軽量化（Static・コライダー・ライト）。床のコライダーが箱に変わるので、テレポート先より先に
        VRSceneOptimizer.PrepareRoom(room);

        // テレポート先の床（上向きの面だけ）
        InteractionLayerMask teleportLayer = InteractionLayerMask.GetMask("Teleport");
        if (teleportLayer.value == 0) Debug.LogWarning("[VR] Interaction Layer「Teleport」がありません。「2. Setup Quest Project」を先に実行してください");
        int floors = 0;
        foreach (var t in room.GetComponentsInChildren<Transform>(true))
        {
            if (!TeleportFloors.Contains(t.name) || t.GetComponent<Collider>() == null) continue;
            var area = t.gameObject.AddComponent<TeleportationArea>();
            area.interactionLayers = teleportLayer;
            area.filterSelectionByHitNormal = true;
            floors++;
        }

        VRSceneOptimizer.AddProbes(room);
        var bungee = BungeeBuilder.Create(room);
        new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
        var rig = CreateRig(rigPrefab, spawn);

        // ベイクは保存したシーンにしかできないので、保存 → ベイク → もう一度保存
        EditorSceneManager.SaveScene(scene, ScenePath);
        VRSceneOptimizer.Bake();
        VRSceneOptimizer.BakeOcclusion();
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(DesktopScenePath, false), // パソコン版の試作。Quest のビルドには入れない
        };
        Selection.activeGameObject = rig;
        Debug.Log($"[VR] シーン生成完了: {ScenePath} / ドア・窓 {doors.Length} 枚, テレポート床 {floors} 枚, バンジー {(bungee != null ? "あり" : "なし")}, 大きさ {PlayerScale}, spawn {spawn}");
    }

    static GameObject CreateRig(GameObject rigPrefab, Vector3 spawn)
    {
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
        rig.transform.SetPositionAndRotation(spawn, Quaternion.identity);
        rig.transform.localScale = Vector3.one * PlayerScale;

        // 床を基準にして、自分の実際の背の高さで見る
        var origin = rig.GetComponent<XROrigin>();
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        PrefabUtility.RecordPrefabInstancePropertyModifications(origin);

        // 体の窓口（バンジー中に移動を止める等）と、ヘッドセットが無いときのキーボード・マウス操作
        rig.AddComponent<VRPlayerBody>();
        var desktop = rig.AddComponent<VRDesktopControls>();
        var transforms = rig.GetComponentsInChildren<Transform>(true);
        desktop.leftHand = transforms.FirstOrDefault(t => t.name == "Left Controller");
        desktop.rightHand = transforms.FirstOrDefault(t => t.name == "Right Controller");

        var moves = rig.GetComponentsInChildren<ContinuousMoveProvider>(true);
        foreach (var move in moves)
        {
            move.moveSpeed *= PlayerScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(move);
        }

        // 酔い対策: スティックで動く・回る間だけ視界の端を暗くする
        var vignettePrefab = VRProjectSetup.FindSampleAsset<GameObject>("TunnelingVignette");
        if (vignettePrefab != null)
        {
            var vignette = (GameObject)PrefabUtility.InstantiatePrefab(vignettePrefab, origin.Camera.transform);
            var controller = vignette.GetComponent<TunnelingVignetteController>();
            var providers = rig.GetComponentsInChildren<LocomotionProvider>(true).Where(p => p is ContinuousMoveProvider || p is ContinuousTurnProvider);
            foreach (var provider in providers)
                controller.locomotionVignetteProviders.Add(new LocomotionVignetteProvider { locomotionProvider = provider, enabled = true });
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        }
        return rig;
    }
}
