using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// VR 版・画面版のシーンに「エレベータースタート」と「バンジー着地時のお祝い一枚絵ボード」を作る（GameSceneBuilder から呼ぶ）。
/// 1. スタート: エレベーター内（PlayerSpawn_Elevator）にプレイヤーを配置し、正面扉に「START」の電光掲示板を出す
/// 2. 着地時の一枚絵: Assets/Bungee/Textures/BirthdayCard.png 用のワールド空間ボードを用意（バンジー着地時に表示）
/// 面どうしの重なり（Z ファイティング）が 0 組になるよう、すべての部品の面を 10mm 以上離して配置する。
/// </summary>
public static class TimeAttackBuilder
{
    public const string RootName = "TimeAttack";
    const string BirthdayCardPath = "Assets/Bungee/Textures/BirthdayCard.png";

    public static TimeAttackManager Create(GameObject room, out Vector3 spawn, out float spawnYaw)
    {
        spawn = new Vector3(-7.75f, 0.02f, 2.85f);
        spawnYaw = 180f;

        if (BungeeBuilder.font == null)
            BungeeBuilder.font = AssetDatabase.LoadAssetAtPath<Font>(BungeeBuilder.FontPath);

        var root = new GameObject(RootName).transform;
        var startPoint = BungeeBuilder.Marker(root, "StartPoint", spawn, Vector3.back);

        // エレベーター扉正面（Z=1.96 より 9cm 手前の Z=2.05）に「START」表示を配置
        Vector3 startSignPos = new Vector3(-7.75f, 1.48f, 2.05f);
        var startSign = BungeeBuilder.LedBoard(root, "TimeAttackStartSign", "START\n扉を開ける", startSignPos, Vector3.back, 0.06f,
            new BungeeBuilder.LedStyle
            {
                palette = new[] { BungeeBuilder.LedCyan, BungeeBuilder.LedGreen },
                bulbColor = new Color(0.3f, 0.95f, 1f),
                blinkTime = 0.35f,
            });

        // バンジー着地時の一枚絵ボード（着地するまで非表示）
        Vector3 cardPos = new Vector3(-11.45f, 1.28f, -4.20f);
        var cardBoard = CreatePictureBoard(root, "BirthdayCardBoard", cardPos, Vector3.right, 1.12f, 0.66f, BirthdayCardPath, out var cardRenderer);
        cardBoard.SetActive(false);

        var manager = root.gameObject.AddComponent<TimeAttackManager>();
        manager.startPoint = startPoint;
        manager.entranceDoor = room.GetComponentsInChildren<Door>(true).FirstOrDefault(d => d.name.StartsWith("Door_Entrance"));
        manager.startSign = startSign;
        manager.birthdayCard = AssetDatabase.LoadAssetAtPath<Texture2D>(BirthdayCardPath);
        manager.birthdayCardBoard = cardBoard;
        manager.birthdayCardRenderer = cardRenderer;
        manager.cheerSound = BungeeBuilder.LoadClip("Bungee_Cheer");
        manager.niceVoice = BungeeBuilder.LoadClip("Bungee_Voice_nice_jump");
        manager.retrySound = BungeeBuilder.LoadClip("Bungee_mode_lever");

        Debug.Log($"[エレベーター開始] 配置完了: スタート {spawn:F2} (yaw {spawnYaw:F0}°), BirthdayCard {(manager.birthdayCard != null ? "あり" : "なし")}");
        return manager;
    }

    static GameObject CreatePictureBoard(Transform parent, string name, Vector3 position, Vector3 facing, float width, float height, string texturePath, out Renderer quadRenderer)
    {
        var board = new GameObject(name);
        board.transform.SetParent(parent, false);
        board.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing));

        BungeeBuilder.Part(board.transform, PrimitiveType.Cube, new Vector3(0f, 0f, 0.020f), new Vector3(width + 0.05f, height + 0.05f, 0.030f), "SignFrame");

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
}
