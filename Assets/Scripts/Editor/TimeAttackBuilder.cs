using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// VR 版・画面版のシーンに「教室タイムアタック」のスタート・ゴール・表示・リトライボタンを作る（GameSceneBuilder から呼ぶ）。
/// 1. スタート: 入口ドア（Door_Entrance）すぐ内側にスタート地点と電光掲示板（StartSign）、リトライボタンを配置
/// 2. ゴール: 部屋の奥（ホールのステージ上）に「ばんじー」目印（BanjiFace.png パネル＋カプセル体＋光る輪）と電光掲示板（GoalSign）、リトライボタンを配置
/// 3. クリア時の一枚絵: Assets/Bungee/Textures/BirthdayCard.png 用のワールド空間ボードをゴール横に用意（画像があるときだけクリア時に表示）
/// 面どうしの重なり（Z ファイティング）が 0 組になるよう、すべての部品の面を 6〜12mm 以上離して配置する。
/// </summary>
public static class TimeAttackBuilder
{
    public const string RootName = "TimeAttack";
    const string BanjiFacePath = "Assets/Bungee/Textures/BanjiFace.png";
    const string BirthdayCardPath = "Assets/Bungee/Textures/BirthdayCard.png";

    public static TimeAttackManager Create(GameObject room, out Vector3 spawn, out float spawnYaw)
    {
        spawn = new Vector3(-5.80f, 0.02f, 0.65f);
        spawnYaw = 135f;

        if (BungeeBuilder.font == null)
            BungeeBuilder.font = AssetDatabase.LoadAssetAtPath<Font>(BungeeBuilder.FontPath);

        var byName = new Dictionary<string, Renderer>();
        foreach (var r in room.GetComponentsInChildren<Renderer>(true)) byName.TryAdd(r.name, r);

        float floorTop = byName.TryGetValue("Floor_Hall", out var hallFloor) ? hallFloor.bounds.max.y : 0f;
        float intoRoomX = 1f;
        float intoHallZ = -1f;
        if (byName.TryGetValue("Floor_Hall", out var hallR) && byName.TryGetValue("Floor_Entrance", out var entR))
        {
            intoRoomX = Mathf.Sign(hallR.bounds.center.x - entR.bounds.center.x);
            intoHallZ = Mathf.Sign(hallR.bounds.center.z - entR.bounds.center.z);
        }

        // 1. スタート地点: Door_Entrance のすぐ内側、ホール入口の開けた方向を向く
        if (byName.TryGetValue("Door_Entrance_Leaf", out var doorLeaf))
        {
            Bounds db = doorLeaf.bounds;
            spawn = new Vector3(db.center.x + intoRoomX * 0.85f, floorTop + 0.02f, db.center.z + intoHallZ * 0.10f);
        }
        Vector3 lookTarget = new Vector3(spawn.x + intoRoomX * 2.0f, spawn.y, spawn.z + intoHallZ * 1.85f);
        Vector3 startForward = Vector3.ProjectOnPlane(lookTarget - spawn, Vector3.up).normalized;
        Vector3 startRight = Vector3.Cross(Vector3.up, startForward).normalized;
        spawnYaw = Quaternion.LookRotation(startForward).eulerAngles.y;

        // 2. ゴール地点: 部屋の奥（ホールのステージ中央）
        Vector3 goalPos = new Vector3(-11.45f, floorTop + 0.20f, -4.20f);
        float stageTop = floorTop + 0.20f;
        if (byName.TryGetValue("Stage_Module1_Top", out var stage1) && byName.TryGetValue("Stage_Module2_Top", out var stage2))
        {
            Bounds sb = stage1.bounds;
            sb.Encapsulate(stage2.bounds);
            stageTop = sb.max.y;
            goalPos = new Vector3(sb.center.x + intoRoomX * 0.15f, stageTop, sb.center.z);
        }
        Vector3 goalFacing = new Vector3(intoRoomX, 0f, 0f); // ステージからホール側を向く
        Vector3 goalRight = Vector3.Cross(Vector3.up, goalFacing).normalized;

        var root = new GameObject(RootName).transform;

        // スタート地点マーカー・足元の光る輪・頭上の電光掲示板・リトライボタン
        var startPoint = BungeeBuilder.Marker(root, "StartPoint", spawn, startForward);
        BungeeBuilder.ReturnPadEffect(root, new Vector3(spawn.x, floorTop, spawn.z));
        Vector3 startSignPos = new Vector3(spawn.x, floorTop, spawn.z) + startForward * 2.05f + Vector3.up * 1.92f;
        var startSign = BungeeBuilder.LedBoard(root, "TimeAttackStartSign", "タイムアタック\n3", startSignPos, -startForward, 0.09f,
            new BungeeBuilder.LedStyle
            {
                palette = new[] { BungeeBuilder.LedCyan, BungeeBuilder.LedGreen },
                bulbColor = new Color(0.3f, 0.95f, 1f),
                blinkTime = 0.35f,
            });
        Vector3 startRetryPos = new Vector3(spawn.x + intoRoomX * 0.75f, floorTop, spawn.z - intoHallZ * 0.30f);
        var startRetry = CreateRetryPedestal(root, "StartRetryButton", startRetryPos, (spawn - startRetryPos).normalized);

        // ゴール地点マーカー・足元の光る輪・ばんじー目印・頭上の電光掲示板・リトライボタン
        var goalPoint = BungeeBuilder.Marker(root, "GoalPoint", goalPos, goalFacing);
        BungeeBuilder.ReturnPadEffect(root, goalPos);
        var goalFigure = CreateGoalBanji(root, goalPos, goalFacing);
        Vector3 goalSignPos = goalPos + goalFacing * 0.30f + Vector3.up * 1.68f;
        var goalSign = BungeeBuilder.LedBoard(root, "TimeAttackGoalSign", "★ GOAL ばんじー ★\nタッチでゴール！", goalSignPos, goalFacing, 0.09f,
            new BungeeBuilder.LedStyle
            {
                palette = new[] { BungeeBuilder.LedPink, BungeeBuilder.LedYellow, BungeeBuilder.LedCyan },
                bulbColor = new Color(1f, 0.55f, 0.85f),
            });
        Vector3 goalRetryPos = goalPos + goalFacing * 0.15f - goalRight * 1.05f;
        var goalRetry = CreateRetryPedestal(root, "GoalRetryButton", goalRetryPos, goalFacing);

        // クリア時の一枚絵ボード（ワールド空間用。Assets/Bungee/Textures/BirthdayCard.png があるときだけクリア時に出す）
        Vector3 cardPos = goalPos + goalFacing * 0.15f + goalRight * 1.18f + Vector3.up * 1.08f;
        var cardBoard = CreatePictureBoard(root, "BirthdayCardBoard", cardPos, goalFacing, 1.12f, 0.66f, BirthdayCardPath, out var cardRenderer);
        cardBoard.SetActive(false);

        var manager = root.gameObject.AddComponent<TimeAttackManager>();
        manager.startPoint = startPoint;
        manager.goalPoint = goalPoint;
        manager.goalFigure = goalFigure;
        manager.entranceDoor = room.GetComponentsInChildren<Door>(true).FirstOrDefault(d => d.name.StartsWith("Door_Entrance"));
        manager.startSign = startSign;
        manager.goalSign = goalSign;
        manager.birthdayCard = AssetDatabase.LoadAssetAtPath<Texture2D>(BirthdayCardPath);
        manager.birthdayCardBoard = cardBoard;
        manager.birthdayCardRenderer = cardRenderer;
        manager.voiceCount = new[] { "count_3", "count_2", "count_1", "count_bungee" }.Select(n => BungeeBuilder.LoadClip("Bungee_Voice_" + n)).ToArray();
        manager.cheerSound = BungeeBuilder.LoadClip("Bungee_Cheer");
        manager.niceVoice = BungeeBuilder.LoadClip("Bungee_Voice_nice_jump");
        manager.retrySound = BungeeBuilder.LoadClip("Bungee_mode_lever");

        startRetry.manager = manager;
        goalRetry.manager = manager;

        Debug.Log($"[タイムアタック] 配置完了: スタート {spawn:F2} (yaw {spawnYaw:F0}°), ゴール {goalPos:F2}, BirthdayCard {(manager.birthdayCard != null ? "あり" : "なし（未配置）")}");
        return manager;
    }

    /// <summary>
    /// ゴール用の「ばんじー」目印オブジェクト。
    /// 台座・カプセルの体・顔イラストパネル（BanjiFace.png があれば貼る）を、面が重ならないよう 10mm 以上離して組む。
    /// </summary>
    static Transform CreateGoalBanji(Transform parent, Vector3 basePos, Vector3 facing)
    {
        var holder = new GameObject("BanjiGoal").transform;
        holder.SetParent(parent, false);
        holder.SetPositionAndRotation(basePos, Quaternion.LookRotation(facing));

        // 台座（底面をステージに 12mm 埋め、上面は +0.128m）
        var pedestal = BungeeBuilder.Part(holder, PrimitiveType.Cylinder, new Vector3(0f, 0.058f, 0f), new Vector3(0.68f, 0.07f, 0.68f), "Safety");
        GameObjectUtility.SetStaticEditorFlags(pedestal, BungeeBuilder.StaticFlags);

        // 動く本体（クリア時にジャンプ・回転する）
        var figure = new GameObject("Figure").transform;
        figure.SetParent(holder, false);
        figure.localPosition = Vector3.zero;

        // ばんじーの胴体（カプセル）
        BungeeBuilder.Part(figure, PrimitiveType.Capsule, new Vector3(0f, 0.48f, 0f), new Vector3(0.36f, 0.32f, 0.36f), "Harness");

        // ばんじーの顔パネル（高さ 1.08m、胴体の上端 0.80m から離す）
        EnsureTextureImported(BanjiFacePath);
        CreatePictureBoard(figure, "BanjiFaceBoard", new Vector3(0f, 1.08f, 0f), Vector3.forward, 0.76f, 0.45f, BanjiFacePath, out _, localSpace: true);

        return figure;
    }

    /// <summary>
    /// 枠付きの画像パネルを作る。裏の箱の前面と、表面の Quad を 10mm 離して Z ファイティングを防ぐ。
    /// </summary>
    static GameObject CreatePictureBoard(Transform parent, string name, Vector3 position, Vector3 facing, float width, float height, string texturePath, out Renderer quadRenderer, bool localSpace = false)
    {
        var board = new GameObject(name);
        board.transform.SetParent(parent, false);
        if (localSpace)
            board.transform.SetLocalPositionAndRotation(position, Quaternion.LookRotation(facing));
        else
            board.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing));

        // 裏の黒いフレーム箱（前面が z = +0.005、背面が z = +0.035）
        BungeeBuilder.Part(board.transform, PrimitiveType.Cube, new Vector3(0f, 0f, 0.020f), new Vector3(width + 0.05f, height + 0.05f, 0.030f), "SignFrame");

        // 表の絵（Quad。z = -0.005 でフレーム前面から 10mm 手前に離す。読む人は -Z 側から見る）
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name + "_Picture";
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(board.transform, false);
        quad.transform.localPosition = new Vector3(0f, 0f, -0.005f);
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(width, height, 1f);
        quadRenderer = quad.GetComponent<Renderer>();
        quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
        quadRenderer.sharedMaterial = GetPictureMaterial(name, texturePath);
        return board;
    }

    static Material GetPictureMaterial(string key, string texturePath)
    {
        EnsureTextureImported(texturePath);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", Color.white);
        if (tex != null) mat.SetTexture("_BaseMap", tex);
        return BungeeBuilder.SaveMaterial("TimeAttack_" + key, mat);
    }

    static void EnsureTextureImported(string path)
    {
        if (!System.IO.File.Exists(path)) return;
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            AssetDatabase.ImportAsset(path);
    }

    /// <summary>
    /// リトライ用の赤いボタンスタンド（IUsable）。
    /// 支柱・台箱・赤いボタンキャップのすべての水平面を 10mm ずつずらして重なりを 0 にする。
    /// </summary>
    static TimeAttackRetryButton CreateRetryPedestal(Transform parent, string name, Vector3 basePos, Vector3 facing)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(basePos, Quaternion.LookRotation(facing));

        // 支柱: 底面 y = -0.012, 上面 y = +0.888
        var post = BungeeBuilder.Part(go.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.438f, 0f), new Vector3(0.08f, 0.45f, 0.08f), "Metal");
        GameObjectUtility.SetStaticEditorFlags(post, BungeeBuilder.StaticFlags);

        // 台箱: 底面 y = +0.878（支柱上面より 10mm 下）、上面 y = +0.958
        var box = BungeeBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(0f, 0.918f, 0f), new Vector3(0.22f, 0.08f, 0.22f), "SignFrame");
        GameObjectUtility.SetStaticEditorFlags(box, BungeeBuilder.StaticFlags);

        // 赤いボタンキャップ: 底面 y = +0.948（台箱上面より 10mm 下）、上面 y = +0.998
        var cap = BungeeBuilder.Part(go.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.973f, 0f), new Vector3(0.13f, 0.025f, 0.13f), "Red");

        // 小さな案内板（ボタンの少し奥・上、どの面とも接しない位置 y = 1.24m）
        BungeeBuilder.LedBoard(go.transform, name + "_Sign", "リトライ\n（E / グリップ / R）",
            basePos + Vector3.up * 1.24f - facing * 0.08f, facing, 0.042f,
            new BungeeBuilder.LedStyle { palette = new[] { BungeeBuilder.LedYellow, BungeeBuilder.LedWhite } });

        var col = go.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.94f, 0f);
        col.size = new Vector3(0.28f, 0.22f, 0.28f);

        var retry = go.AddComponent<TimeAttackRetryButton>();
        retry.cap = cap.transform;
        return retry;
    }
}
