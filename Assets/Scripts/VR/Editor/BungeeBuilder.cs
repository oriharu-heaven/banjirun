using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// VR シーンに、窓の外のバンジージャンプを作る（VRSceneBuilder から呼ぶ）。
/// ホール東の窓（EastWin_Sash_02）を開けてバルコニーへ出て、階段でジャンプ台に上がり、ハーネスを付けて台の先から飛ぶ。
/// 下（約 23 m 下の歩道）にはクッション、その上に戻る輪。位置は部屋モデルのバルコニー・窓・歩道から決める。
/// </summary>
public static class BungeeBuilder
{
    const string MaterialFolder = "Assets/VR/Materials";
    const string FontPath = "Assets/Fonts/MPLUSRounded1c-Bold.ttf";

    const float PlatformTop = 1.15f;    // バルコニーの手すり（1.12 m）を越える高さ
    const float PlatformV0 = 1.18f;     // 台の幅（窓の端から測る）
    const float PlatformV1 = 2.38f;
    const float PlatformReach = 3.14f;  // 壁から台の先まで（手すりから 1.8 m 張り出す）
    const float AnchorHeight = 2.63f;   // 台からロープの付け根まで
    const float CushionSize = 4f;
    const float CushionHeight = 1.6f;

    // 動かない物（ベイクはせず、ライトプローブで照らす）
    const StaticEditorFlags StaticFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

    static Vector3 origin;   // 窓の外の壁ぎわ・窓の端（階段側）の床
    static Vector3 outward;  // 壁から外へ
    static Vector3 along;    // 窓から階段・ジャンプ台の方へ（壁に沿って）
    static Font font;
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    /// <summary>u: 壁から外へ (m), v: 窓の端から階段の方へ (m), y: 高さ（ワールド）</summary>
    static Vector3 P(float u, float v, float y)
    {
        Vector3 p = origin + outward * u + along * v;
        p.y = y;
        return p;
    }

    public static BungeeJump Create(GameObject room)
    {
        var byName = new Dictionary<string, Renderer>();
        foreach (var r in room.GetComponentsInChildren<Renderer>(true)) byName.TryAdd(r.name, r);
        string[] needed = { "Balcony_Floor", "Balcony_Parapet", "EastWin_Sash_02_Glass", "AC_Outdoor_01_Body", "Road" };
        var missing = needed.Where(n => !byName.ContainsKey(n)).ToArray();
        if (missing.Length > 0)
        {
            Debug.LogError($"[VR] バンジーの場所が決められません。見つからない物: {string.Join(", ", missing)}");
            return null;
        }
        Bounds balcony = byName["Balcony_Floor"].bounds, parapet = byName["Balcony_Parapet"].bounds, window = byName["EastWin_Sash_02_Glass"].bounds;
        float balconyTop = balcony.max.y, ground = byName["Road"].bounds.max.y;
        outward = new Vector3(Mathf.Sign(parapet.center.x - balcony.center.x), 0f, 0f);
        along = new Vector3(0f, 0f, Mathf.Sign(byName["AC_Outdoor_01_Body"].bounds.center.z - window.center.z));
        origin = new Vector3(outward.x < 0f ? balcony.max.x : balcony.min.x, balconyTop, along.z < 0f ? window.min.z : window.max.z);
        float parapetOuter = outward.x < 0f ? parapet.min.x : parapet.max.x;
        float vMid = (PlatformV0 + PlatformV1) / 2f;

        font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        materials.Clear();
        var root = new GameObject("BungeeJump").transform;

        // 階段（3 段）とジャンプ台
        for (int i = 0; i < 3; i++)
            Box(root, $"Step_{i}", P(0.05f, 0.13f + 0.35f * i, balconyTop), P(1.15f, 0.48f + 0.35f * i, balconyTop + 0.29f * (i + 1)), "Metal");
        var deck = Box(root, "Deck", P(0.04f, PlatformV0, PlatformTop - 0.1f), P(PlatformReach, PlatformV1, PlatformTop), "Metal");
        Box(root, "EdgeStripe", P(PlatformReach - 0.14f, PlatformV0, PlatformTop), P(PlatformReach, PlatformV1, PlatformTop + 0.006f), "Safety", collider: false);
        Beam(root, "Brace_A", P(1.33f, PlatformV0 + 0.07f, balconyTop - 0.8f), P(2.85f, PlatformV0 + 0.07f, PlatformTop - 0.1f), 0.08f, "Metal");
        Beam(root, "Brace_B", P(1.33f, PlatformV1 - 0.07f, balconyTop - 0.8f), P(2.85f, PlatformV1 - 0.07f, PlatformTop - 0.1f), 0.08f, "Metal");

        // 手すり（階段側はバルコニーの上だけ開けておく。台の先は開いている）
        Railing(root, "Rail_A", 1.3f, 3.0f, PlatformV0 + 0.03f);
        Railing(root, "Rail_B", 0.1f, 3.0f, PlatformV1 - 0.03f);

        // ロープを吊るす門
        float gantryTop = PlatformTop + AnchorHeight + 0.12f;
        Cylinder(root, "Gantry_PostA", P(2.95f, PlatformV0 + 0.06f, PlatformTop), gantryTop - PlatformTop, 0.06f, "Safety");
        Cylinder(root, "Gantry_PostB", P(2.95f, PlatformV1 - 0.06f, PlatformTop), gantryTop - PlatformTop, 0.06f, "Safety");
        Beam(root, "Gantry_Bar", P(2.95f, PlatformV0 + 0.06f, gantryTop), P(2.95f, PlatformV1 - 0.06f, gantryTop), 0.1f, "Safety");
        Beam(root, "Gantry_Arm", P(2.95f, vMid, gantryTop), P(3.6f, vMid, gantryTop), 0.1f, "Safety");
        var anchor = Sphere(root, "Anchor", P(3.5f, vMid, gantryTop - 0.12f), 0.12f, "Metal").transform;
        var platformSign = Sign(root, "PlatformSign", "ハーネスを付けてから\n飛ぼう", P(2.92f, vMid, PlatformTop + 2.35f), outward, 0.1f);
        // カウントダウンは、台の先に立って前を見たときに目に入る空中に大きく出す
        var countdownSign = Sign(root, "CountdownSign", "", P(PlatformReach + 1.4f, vMid, PlatformTop + 1.75f), outward, 0.42f, board: false);
        countdownSign.color = new Color(1f, 0.85f, 0.2f);

        // ハーネスを掛けるフック
        Cylinder(root, "HookPost", P(1.5f, PlatformV1 - 0.08f, PlatformTop), 1.9f, 0.05f, "Metal");
        Beam(root, "HookArm", P(1.5f, PlatformV1 - 0.08f, PlatformTop + 1.85f), P(1.5f, PlatformV1 - 0.3f, PlatformTop + 1.85f), 0.05f, "Metal");
        var harness = CreateHarness(root, P(1.5f, PlatformV1 - 0.26f, PlatformTop + 1.32f));
        Sign(root, "HarnessSign", "ハーネスをつかんで付ける\n（グリップ / E キー）", P(1.5f, PlatformV1 - 0.45f, PlatformTop + 2.2f), outward, 0.07f);

        // 部屋の中の案内（窓のとなり）
        float right = Vector3.Dot(Vector3.Cross(Vector3.up, outward), along);
        Sign(root, "WindowSign", $"バンジージャンプ\n{(right > 0f ? "この窓を開けて外へ →" : "← この窓を開けて外へ")}",
            new Vector3(window.center.x - outward.x * 0.17f, 1.55f, window.center.z - along.z * 1.05f), outward, 0.09f);

        // 下のクッション（エアマット）。底が原点で、着地すると BungeeJump が弾ませる
        var cushion = CreateCushion(root, P(PlatformReach, vMid, ground));
        float cushionTop = ground + CushionHeight;
        var returnPad = Cylinder(root, "ReturnPad", P(PlatformReach, vMid - 1.25f, cushionTop - 0.02f), 0.02f, 0.5f, "ReturnPad", collider: false);
        var glow = Cylinder(root, "ReturnPad_Glow", P(PlatformReach, vMid - 1.25f, cushionTop), 1.4f, 0.45f, "ReturnGlow", collider: false);
        GameObjectUtility.SetStaticEditorFlags(glow, 0);
        var groundSign = Sign(root, "GroundSign", "着地成功！\n光る輪で上に戻る", P(PlatformReach + 2.4f, vMid + 1.1f, cushionTop + 1.4f), outward, 0.14f);
        Cylinder(root, "GroundSign_Pole", P(PlatformReach + 2.4f, vMid + 1.1f, ground), cushionTop + 1.0f - ground, 0.05f, "Metal");

        var returnPoint = new GameObject("ReturnPoint").transform;
        returnPoint.SetParent(root, false);
        returnPoint.SetPositionAndRotation(P(0.55f, -0.45f, balconyTop), Quaternion.LookRotation(along));

        var jumpEdge = Marker(root, "JumpEdge", P(PlatformReach, vMid, PlatformTop), outward);
        var dropLine = Marker(root, "DropLine", new Vector3(parapetOuter, PlatformTop, P(0f, vMid, 0f).z), outward);

        var cord = new GameObject("Cord").AddComponent<LineRenderer>();
        cord.transform.SetParent(root, false);
        cord.widthMultiplier = 0.035f;
        cord.numCapVertices = 2;
        cord.sharedMaterial = GetMaterial("Cord");
        cord.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        AddLightProbes(root, ground, cushionTop, vMid);

        // テレポートでもジャンプ台に乗れるように
        var area = deck.AddComponent<TeleportationArea>();
        area.interactionLayers = InteractionLayerMask.GetMask("Teleport");
        area.filterSelectionByHitNormal = true;

        var bungee = root.gameObject.AddComponent<BungeeJump>();
        bungee.anchor = anchor;
        bungee.jumpEdge = jumpEdge;
        bungee.dropLine = dropLine;
        bungee.platformTop = PlatformTop;
        bungee.cushionTop = cushionTop;
        bungee.cushion = cushion;
        bungee.returnPad = returnPad.transform;
        bungee.returnPoint = returnPoint;
        bungee.harness = harness;
        bungee.cord = cord;
        bungee.platformSign = platformSign;
        bungee.countdownSign = countdownSign;
        bungee.groundSign = groundSign;
        harness.bungee = bungee;

        Debug.Log($"[VR] バンジー: 台の高さ {PlatformTop} m, クッションの上 {cushionTop:F2} m（落差 {PlatformTop - cushionTop:F1} m）, 付け根 {anchor.position}");
        return bungee;
    }

    static BungeeHarness CreateHarness(Transform parent, Vector3 position)
    {
        var root = new GameObject("Harness");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-outward));
        var box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(0.42f, 0.75f, 0.24f);

        var model = new GameObject("Model");
        model.transform.SetParent(root.transform, false);
        Part(model.transform, PrimitiveType.Cube, new Vector3(0f, -0.12f, 0f), new Vector3(0.36f, 0.08f, 0.2f), "Harness");      // 腰ベルト
        Part(model.transform, PrimitiveType.Cube, new Vector3(-0.09f, 0.1f, 0f), new Vector3(0.05f, 0.42f, 0.03f), "Harness");  // 肩ベルト
        Part(model.transform, PrimitiveType.Cube, new Vector3(0.09f, 0.1f, 0f), new Vector3(0.05f, 0.42f, 0.03f), "Harness");
        Part(model.transform, PrimitiveType.Cube, new Vector3(-0.08f, -0.28f, 0f), new Vector3(0.12f, 0.06f, 0.16f), "Harness"); // 脚ベルト
        Part(model.transform, PrimitiveType.Cube, new Vector3(0.08f, -0.28f, 0f), new Vector3(0.12f, 0.06f, 0.16f), "Harness");
        Part(model.transform, PrimitiveType.Capsule, new Vector3(0f, 0.33f, 0f), new Vector3(0.05f, 0.06f, 0.05f), "Metal");   // カラビナ

        var cordPoint = new GameObject("CordPoint").transform;
        cordPoint.SetParent(root.transform, false);
        cordPoint.localPosition = new Vector3(0f, 0.36f, 0f);

        var harness = root.AddComponent<BungeeHarness>();
        harness.model = model;
        harness.cordPoint = cordPoint;
        return harness;
    }

    static Transform CreateCushion(Transform parent, Vector3 basePosition)
    {
        var root = new GameObject("Cushion");
        root.transform.SetParent(parent, false);
        root.transform.position = basePosition;
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, CushionHeight / 2f, 0f);
        box.size = new Vector3(CushionSize, CushionHeight, CushionSize);

        float half = CushionSize / 2f;
        Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.5f, 0f), new Vector3(CushionSize - 0.3f, 1f, CushionSize - 0.3f), "Red");
        Part(root.transform, PrimitiveType.Sphere, new Vector3(0f, CushionHeight - 0.5f, 0f), new Vector3(CushionSize + 0.2f, 1f, CushionSize + 0.2f), "CushionTop");
        Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.8f, 0f), new Vector3(CushionSize - 0.26f, 0.12f, CushionSize - 0.26f), "White");
        // ふくらんだ縁（4 辺の筒と 4 隅の柱）
        foreach (var (pos, scale) in new[]
        {
            (new Vector3(0f, 0.45f, half - 0.2f), new Vector3(CushionSize - 0.2f, 0.45f, 0.45f)),
            (new Vector3(0f, 0.45f, -half + 0.2f), new Vector3(CushionSize - 0.2f, 0.45f, 0.45f)),
            (new Vector3(half - 0.2f, 0.45f, 0f), new Vector3(0.45f, 0.45f, CushionSize - 0.2f)),
            (new Vector3(-half + 0.2f, 0.45f, 0f), new Vector3(0.45f, 0.45f, CushionSize - 0.2f)),
        })
            Part(root.transform, PrimitiveType.Cube, pos, scale, "Red");
        foreach (float x in new[] { -1f, 1f })
        foreach (float z in new[] { -1f, 1f })
            Part(root.transform, PrimitiveType.Cylinder, new Vector3(x * (half - 0.2f), 0.6f, z * (half - 0.2f)), new Vector3(0.5f, 0.6f, 0.5f), "Red");
        // 的
        Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, CushionHeight + 0.004f, 0f), new Vector3(1.8f, 0.004f, 1.8f), "White");
        Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, CushionHeight + 0.008f, 0f), new Vector3(0.9f, 0.004f, 0.9f), "Red");
        return root.transform;
    }

    static void Railing(Transform parent, string name, float u0, float u1, float v)
    {
        int posts = Mathf.CeilToInt((u1 - u0) / 0.7f);
        for (int i = 0; i <= posts; i++)
            Cylinder(parent, $"{name}_Post{i}", P(Mathf.Lerp(u0, u1, i / (float)posts), v, PlatformTop), 1f, 0.025f, "Safety");
        Beam(parent, $"{name}_Top", P(u0, v, PlatformTop + 1f), P(u1, v, PlatformTop + 1f), 0.045f, "Safety");
        Beam(parent, $"{name}_Mid", P(u0, v, PlatformTop + 0.5f), P(u1, v, PlatformTop + 0.5f), 0.03f, "Safety");
    }

    static void AddLightProbes(Transform parent, float ground, float cushionTop, float vMid)
    {
        var probes = new List<Vector3>();
        foreach (float u in new[] { 0.2f, 1.6f, 3.4f })
        foreach (float v in new[] { PlatformV0 - 0.9f, PlatformV1 + 0.4f })
        foreach (float y in new[] { 0.3f, PlatformTop + 0.4f, PlatformTop + 2.6f })
            probes.Add(P(u, v, y));
        for (float y = PlatformTop; y > cushionTop; y -= 3f)
        {
            probes.Add(P(PlatformReach - 1.2f, vMid, y));
            probes.Add(P(PlatformReach + 1.2f, vMid, y));
        }
        foreach (float u in new[] { PlatformReach - 3f, PlatformReach + 3f })
        foreach (float v in new[] { vMid - 3f, vMid + 3f })
        foreach (float y in new[] { ground + 0.3f, cushionTop + 1.5f })
            probes.Add(P(u, v, y));
        var group = new GameObject("LightProbes").AddComponent<LightProbeGroup>();
        group.transform.SetParent(parent, false);
        group.probePositions = probes.ToArray();
    }

    // ---- 形を作る小道具 ----

    static GameObject Box(Transform parent, string name, Vector3 a, Vector3 b, string material, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = (a + b) / 2f;
        go.transform.localScale = new Vector3(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), Mathf.Abs(b.z - a.z));
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        Finish(go, material);
        return go;
    }

    static GameObject Beam(Transform parent, string name, Vector3 from, Vector3 to, float thickness, string material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation((from + to) / 2f, Quaternion.LookRotation(to - from));
        go.transform.localScale = new Vector3(thickness, thickness, Vector3.Distance(from, to));
        Finish(go, material);
        return go;
    }

    static GameObject Cylinder(Transform parent, string name, Vector3 bottom, float height, float radius, string material, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = bottom + Vector3.up * height / 2f;
        go.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        if (collider) go.AddComponent<CapsuleCollider>();
        Finish(go, material);
        return go;
    }

    static GameObject Sphere(Transform parent, string name, Vector3 center, float radius, string material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.transform.localScale = Vector3.one * radius * 2f;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        Finish(go, material);
        return go;
    }

    /// <summary>動く物の部品（当たり判定なし、Static にしない）。</summary>
    static GameObject Part(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 localScale, string material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = GetMaterial(material);
        return go;
    }

    static void Finish(GameObject go, string material)
    {
        go.GetComponent<Renderer>().sharedMaterial = GetMaterial(material);
        GameObjectUtility.SetStaticEditorFlags(go, StaticFlags);
    }

    static Transform Marker(Transform parent, string name, Vector3 position, Vector3 forward)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
        return t;
    }

    /// <summary>案内板。facing は読む人の向く方向。board の大きさは文の長さから決める。</summary>
    static TextMesh Sign(Transform parent, string name, string text, Vector3 position, Vector3 facing, float em, bool board = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing));
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = font;
        tm.fontSize = 64;
        tm.characterSize = em * 10f / tm.fontSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = font.material;

        if (!board) return tm;
        string[] lines = text.Split('\n');
        float width = lines.Max(l => l.Sum(ch => ch < 128 ? 0.55f : 1f)) * em + em;
        float height = lines.Length * em * 1.25f + em * 0.8f;
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = name + "_Board";
        plate.transform.SetParent(parent, false);
        plate.transform.SetPositionAndRotation(position + facing * 0.03f, go.transform.rotation);
        plate.transform.localScale = new Vector3(width, height, 0.03f);
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        Finish(plate, "SignBoard");
        return tm;
    }

    static Material GetMaterial(string name)
    {
        if (materials.TryGetValue(name, out var cached)) return cached;
        string path = $"{MaterialFolder}/Bungee_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/VR")) AssetDatabase.CreateFolder("Assets", "VR");
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/VR", "Materials");
            mat = CreateMaterial(name);
            AssetDatabase.CreateAsset(mat, path);
        }
        materials[name] = mat;
        return mat;
    }

    static Material CreateMaterial(string name)
    {
        switch (name)
        {
            case "Cord": return Unlit(new Color(0.95f, 0.35f, 0.1f));
            case "ReturnPad": return Unlit(new Color(0.25f, 0.95f, 1f));
            case "ReturnGlow":
            {
                var glow = Unlit(new Color(0.25f, 0.95f, 1f, 0.22f));
                glow.SetFloat("_Surface", 1f);
                glow.SetFloat("_Blend", 0f);
                glow.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                glow.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                glow.SetFloat("_ZWrite", 0f);
                glow.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                glow.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                return glow;
            }
        }
        var (color, smoothness, metallic) = name switch
        {
            "Metal" => (new Color(0.32f, 0.34f, 0.38f), 0.45f, 0.6f),
            "Safety" => (new Color(1f, 0.78f, 0.08f), 0.4f, 0.1f),
            "Red" => (new Color(0.86f, 0.13f, 0.1f), 0.35f, 0f),
            "CushionTop" => (new Color(1f, 0.83f, 0.18f), 0.35f, 0f),
            "White" => (new Color(0.95f, 0.95f, 0.95f), 0.3f, 0f),
            "Harness" => (new Color(1f, 0.45f, 0.05f), 0.3f, 0f),
            "SignBoard" => (new Color(0.07f, 0.09f, 0.15f), 0.2f, 0f),
            _ => (Color.magenta, 0.5f, 0f),
        };
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        return mat;
    }

    static Material Unlit(Color color)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", color);
        return mat;
    }
}
