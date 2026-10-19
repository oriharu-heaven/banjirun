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
/// メニュー「BanjiRun/VR/3. Build VR Scene」で、VR 版のシーン（VR_Prototype）を作る。
/// 部屋とバンジーは GameSceneBuilder（画面版と共通）。ここで足すのは VR だけの物:
/// XR Origin（XRI）・つかんで使う仕組み（VRUseOnGrab）・テレポート先・ヘッドセットが無いときのキーボード操作（酔い対策のビネットは今は付けない: ComfortVignette）。
/// 移動: 左スティック, 向き: 右スティック左右, テレポート: 右スティックを前に倒して離す, ジャンプ: A, 使う: 触れるかレイを当ててグリップ,
/// ミニキャラ ⇔ 等身大: B・Y（VRPlayerBody が XR Origin ごと縮める。ミニのときは段をよじ登る: ClimbAssist）
/// </summary>
public static class VRSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/VR_Prototype.unity";

    // 酔い対策のビネット（スティックで動く・回る間だけ視界の端を暗くする）。Quest で「周りが暗くなる」のが気になるとのことで外した（2026-10-19）。true にすると戻る
    const bool ComfortVignette = false;

    // テレポートで降りられる床（部屋のモデルとバンジーの台）
    static readonly string[] TeleportFloors =
    {
        "Floor_Hall", "Floor_Entrance", "Floor_Annex", "Balcony_Floor", "Stage_Module1_Top", "Stage_Module2_Top",
        "Car_Floor", "EV_Gap_Floor", "Sidewalk_Near", "Sidewalk_Far", "Road", "Deck", "Deck_Grating",
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
        if (!GameSceneBuilder.CreateWorld(out var world)) return;

        // ドア・窓・ハーネスを、コントローラーでつかんで使えるように（XRSimpleInteractable は RequireComponent で付く）
        var usables = world.scene.GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true))
            .Where(m => m is IUsable)
            .ToArray();
        foreach (var usable in usables) usable.gameObject.AddComponent<VRUseOnGrab>();

        // テレポート先の床（上向きの面だけ）
        InteractionLayerMask teleportLayer = InteractionLayerMask.GetMask("Teleport");
        if (teleportLayer.value == 0) Debug.LogWarning("[VR] Interaction Layer「Teleport」がありません。「2. Setup Quest Project」を先に実行してください");
        int floors = 0;
        foreach (var t in world.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
        {
            if (!TeleportFloors.Contains(t.name) || t.GetComponent<Collider>() == null) continue;
            var area = t.gameObject.AddComponent<TeleportationArea>();
            area.interactionLayers = teleportLayer;
            area.filterSelectionByHitNormal = true;
            floors++;
        }

        new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
        var rig = CreateRig(rigPrefab, world.spawn);

        GameSceneBuilder.SaveAndBake(world.scene, ScenePath);
        Selection.activeGameObject = rig;
        Debug.Log($"[VR] シーン生成完了: {ScenePath} / つかんで使える物 {usables.Length} 個, テレポート床 {floors} 枚, バンジー {(world.bungee != null ? "あり" : "なし")}, spawn {world.spawn}");
    }

    static GameObject CreateRig(GameObject rigPrefab, Vector3 spawn)
    {
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
        rig.transform.SetPositionAndRotation(spawn, Quaternion.identity);

        // 床を基準にして、自分の実際の背の高さで見る
        var origin = rig.GetComponent<XROrigin>();
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        PrefabUtility.RecordPrefabInstancePropertyModifications(origin);

        // 体の窓口（バンジー中に移動を止める等）。ヘッドセットが無いときだけ、キーボード・マウス操作（FirstPersonControls）を有効にする
        rig.AddComponent<VRPlayerBody>();
        rig.AddComponent<ClimbAssist>();
        rig.AddComponent<FirstPersonControls>().enabled = false;
        var fallback = rig.AddComponent<VRNoHeadsetFallback>();
        var transforms = rig.GetComponentsInChildren<Transform>(true);
        fallback.leftHand = transforms.FirstOrDefault(t => t.name == "Left Controller");
        fallback.rightHand = transforms.FirstOrDefault(t => t.name == "Right Controller");

        // 酔い対策: スティックで動く・回る間だけ視界の端を暗くする（ComfortVignette が true のときだけ）
        var vignettePrefab = ComfortVignette ? VRProjectSetup.FindSampleAsset<GameObject>("TunnelingVignette") : null;
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
