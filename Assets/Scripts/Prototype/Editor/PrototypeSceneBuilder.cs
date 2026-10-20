using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// メニュー「BanjiRun/Build Prototype Scene」で、部屋モデル + 小人プレイヤー + ドアの試遊シーンを生成する。
/// </summary>
public static class PrototypeSceneBuilder
{
    const string ModelPath = "Assets/akiba_school_room.blend";
    const string ScenePath = "Assets/Scenes/Prototype.unity";
    const string MaterialFolder = "Assets/Prototype/Materials";

    const float PlayerHeight = 0.25f; // 小人サイズ (25cm)
    const float PlayerRadius = 0.06f;

    // 開閉させるドア: ピボット名, 一緒に回すパーツ（親名を指定した場合はその子すべて）, ドア板, 取っ手
    struct DoorSpec
    {
        public string name, carryParent, leaf, handle;
        public string[] carry;
    }

    static readonly DoorSpec[] Doors =
    {
        new DoorSpec { name = "Door_Entrance", carryParent = "Door_Entrance_Hinge", leaf = "Door_Entrance_Leaf", handle = "Door_Entrance_LeverIn" },
        new DoorSpec { name = "WC_Stall_1_Door", carryParent = "WC_Stall_1_DoorHinge", leaf = "WC_Stall_1_DoorLeaf", handle = "WC_Stall_1_DoorPullIn" },
        new DoorSpec { name = "WC_Stall_2_Door", carryParent = "WC_Stall_2_DoorHinge", leaf = "WC_Stall_2_DoorLeaf", handle = "WC_Stall_2_DoorPullIn" },
        new DoorSpec { name = "Door_Hall_South", carry = new[] { "Door_Hall_South_Leaf", "Door_Hall_South_Handle" }, leaf = "Door_Hall_South_Leaf", handle = "Door_Hall_South_Handle" },
        new DoorSpec { name = "Lobby_StairDoor", carry = new[] { "Lobby_StairDoor_Leaf", "Lobby_StairDoor_Handle", "Lobby_StairDoor_Sign" }, leaf = "Lobby_StairDoor_Leaf", handle = "Lobby_StairDoor_Handle" },
        // ホール東の窓（南から 2 枚目）。開けるとバルコニーへ出られる（VR 版はその先にバンジージャンプ）
        new DoorSpec
        {
            name = "EastWin_Sash_02",
            carry = new[] { "EastWin_Sash_02_FrameB", "EastWin_Sash_02_FrameL", "EastWin_Sash_02_FrameR", "EastWin_Sash_02_FrameT", "EastWin_Sash_02_Glass", "EastWin_Sash_02_Handle", "EastWin_Sash_02_HandleBase" },
            leaf = "EastWin_Sash_02_Glass", handle = "EastWin_Sash_02_Handle",
        },
    };

    [MenuItem("BanjiRun/Build Prototype Scene")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    public static void Build()
    {
        var modelAsset = LoadRoomModel();
        if (modelAsset == null) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CreateRoom(modelAsset, out Vector3 spawn);

        // プレイヤー
        var player = CreatePlayer(spawn);

        // カメラ
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var camera = camGo.AddComponent<Camera>();
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 100f;
        camGo.AddComponent<AudioListener>();
        var follow = camGo.AddComponent<ThirdPersonCamera>();
        follow.target = player.transform;
        camGo.transform.position = spawn + new Vector3(0f, 0.4f, -0.9f);
        player.GetComponent<TinyPlayerController>().cameraTransform = camGo.transform;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = player;
        Debug.Log($"[Prototype] シーン生成完了: {ScenePath} / spawn {spawn}");
    }

    public static GameObject LoadRoomModel()
    {
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null) Debug.LogError($"[Prototype] モデルが読み込めません: {ModelPath}（Blender がインストールされているか確認）");
        return modelAsset;
    }

    /// <summary>
    /// 開いているシーンに部屋を置き、ドア・コライダー・ライト調整を付ける。VR シーン（VRSceneBuilder）からも使う。
    /// </summary>
    public static GameObject CreateRoom(GameObject modelAsset, out Vector3 spawn)
    {
        var room = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        PrefabUtility.UnpackPrefabInstance(room, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        room.name = "AkibaSchoolRoom";
        room.isStatic = false;
        foreach (var cam in room.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(cam.gameObject);

        var byName = new Dictionary<string, Transform>();
        foreach (var t in room.GetComponentsInChildren<Transform>(true)) byName.TryAdd(t.name, t);

        var doorParts = new HashSet<Transform>();
        foreach (var spec in Doors) SetupDoor(spec, byName, doorParts);

        int colliders = 0;
        foreach (var mf in room.GetComponentsInChildren<MeshFilter>(true))
        {
            if (doorParts.Contains(mf.transform) || mf.sharedMesh == null) continue;
            if (mf.GetComponent<Collider>() == null) { mf.gameObject.AddComponent<MeshCollider>(); colliders++; }
        }

        // 室内ライトを暗めに
        // Apply は初回のみ元の強さを記録するので、AddComponent 時の OnEnable と重複しても二重に暗くならない
        room.AddComponent<RoomLightDimmer>().Apply();

        spawn = FindSpawnPoint(byName);
        Debug.Log($"[Prototype] 部屋を配置: ライト {room.GetComponentsInChildren<Light>(true).Length} 個, MeshCollider {colliders} 個, ドア {Doors.Length} 枚");
        return room;
    }

    static void SetupDoor(DoorSpec spec, Dictionary<string, Transform> byName, HashSet<Transform> doorParts)
    {
        if (!byName.TryGetValue(spec.leaf, out var leaf))
        {
            Debug.LogWarning($"[Prototype] ドアが見つかりません: {spec.leaf}");
            return;
        }

        List<Transform> carry = spec.carryParent != null && byName.TryGetValue(spec.carryParent, out var parent)
            ? parent.Cast<Transform>().ToList()
            : spec.carry.Where(byName.ContainsKey).Select(n => byName[n]).ToList();

        // ヒンジ位置 = ドア板の取っ手と反対側の端
        Bounds b = leaf.GetComponent<Renderer>().bounds;
        Vector3 handleCenter = byName.TryGetValue(spec.handle, out var handle) ? handle.GetComponent<Renderer>().bounds.center : b.max;
        Vector3 hinge = new Vector3(b.center.x, b.min.y, b.center.z);
        if (b.size.x >= b.size.z) hinge.x = handleCenter.x > b.center.x ? b.min.x : b.max.x;
        else hinge.z = handleCenter.z > b.center.z ? b.min.z : b.max.z;

        var pivot = new GameObject(spec.name + "_Pivot").transform;
        pivot.SetParent(leaf.parent, false);
        pivot.SetPositionAndRotation(hinge, Quaternion.identity);
        foreach (var part in carry)
        {
            part.SetParent(pivot, true);
            foreach (var t in part.GetComponentsInChildren<Transform>(true)) doorParts.Add(t);
        }
        pivot.gameObject.AddComponent<Door>();
        leaf.gameObject.AddComponent<BoxCollider>();
    }

    static Vector3 FindSpawnPoint(Dictionary<string, Transform> byName)
    {
        Vector3 start = Vector3.up;
        if (byName.TryGetValue("Floor_Hall", out var floor) && floor.TryGetComponent<Renderer>(out var r))
            start = new Vector3(r.bounds.center.x, r.bounds.max.y + 1.5f, r.bounds.center.z);
        Physics.SyncTransforms();
        return Physics.Raycast(start, Vector3.down, out var hit, 5f) ? hit.point + Vector3.up * 0.02f : start;
    }

    static GameObject CreatePlayer(Vector3 position)
    {
        var player = new GameObject("Player");
        player.transform.position = position;
        var cc = player.AddComponent<CharacterController>();
        cc.height = PlayerHeight;
        cc.radius = PlayerRadius;
        cc.center = new Vector3(0f, PlayerHeight / 2f, 0f);
        cc.stepOffset = 0.04f;
        cc.skinWidth = 0.006f;
        cc.minMoveDistance = 0f;
        cc.slopeLimit = 50f;
        player.AddComponent<TinyPlayerController>();

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(player.transform, false);
        body.transform.localPosition = cc.center;
        body.transform.localScale = new Vector3(PlayerRadius * 2f, PlayerHeight / 2f, PlayerRadius * 2f);
        body.GetComponent<Renderer>().sharedMaterial = GetMaterial("Player_Body", new Color(0.95f, 0.45f, 0.2f));

        // 向きが分かるように顔パーツ
        var face = GameObject.CreatePrimitive(PrimitiveType.Cube);
        face.name = "Face";
        Object.DestroyImmediate(face.GetComponent<Collider>());
        face.transform.SetParent(player.transform, false);
        face.transform.localPosition = new Vector3(0f, PlayerHeight * 0.78f, PlayerRadius * 0.9f);
        face.transform.localScale = new Vector3(0.07f, 0.025f, 0.03f);
        face.GetComponent<Renderer>().sharedMaterial = GetMaterial("Player_Face", new Color(0.1f, 0.1f, 0.12f));

        return player;
    }

    static Material GetMaterial(string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        if (!AssetDatabase.IsValidFolder("Assets/Prototype")) AssetDatabase.CreateFolder("Assets", "Prototype");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Prototype", "Materials");
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
