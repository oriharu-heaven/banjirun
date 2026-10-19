using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// VR・画面版のシーンに、窓の外のバンジージャンプを作る（GameSceneBuilder から呼ぶ。VR だけの物＝テレポート先・つかむ仕組みは VRSceneBuilder が足す）。
/// ホール東の窓（EastWin_Sash_02）を開けてバルコニーへ出て、鉄骨の階段でジャンプ台に上がり、ハーネスを付けて台の先の細い板から飛ぶ。
/// 下（約 23 m 下の歩道）にはエアクッション、その上に戻る輪。位置は部屋モデルのバルコニー・窓・歩道から決める。
/// 見た目: 鉄骨のトラスの門と腕・縞鋼板の床・電光掲示板（案内・カウントダウン・看板）・Tripo のモデル（ハーネス・クッション・板・滑車・係の人）。
/// 部品作りは BungeeBuilder.Props.cs。Tripo のモデルが無いときは、箱や筒で代わりを作る。
/// </summary>
public static partial class BungeeBuilder
{
    const string AssetFolder = "Assets/Bungee";
    const string MaterialFolder = AssetFolder + "/Materials";
    const string AudioFolder = AssetFolder + "/Audio";
    const string GratingTexturePath = AssetFolder + "/Textures/Bungee_Grating.png";
    const string HarnessModelPath = AssetFolder + "/Models/Harness/Bungee_Harness.fbx"; // Tripo で作った全身用ハーネス
    const string FontPath = "Assets/Fonts/MPLUSRounded1c-Bold.ttf";

    const float PlatformTop = 1.15f;    // バルコニーの手すり（1.12 m）を越える高さ
    const float PlatformV0 = 1.18f;     // 台の幅（窓の端から測る）
    const float PlatformV1 = 2.38f;
    const float PlatformReach = 3.14f;  // 壁から台の先まで（手すりから 1.8 m 張り出す。手すりより外は床が透けて下が見える）
    const float PlankLength = 1.35f;    // 台の先の、手すりのない細い板
    const float PlankWidth = 0.5f;
    const float GratingStart = 1.31f;   // ここから先の床は格子（バルコニーの手すりの外）
    const float AnchorHeight = 2.63f;   // 台からロープの付け根まで（ロープの強さはこの高さで合わせてあるので変えない）
    const float PulleyHeight = 0.35f;   // 付け根の滑車（腕の下に吊る）
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
            Debug.LogError($"[バンジー] バンジーの場所が決められません。見つからない物: {string.Join(", ", missing)}");
            return null;
        }
        Bounds balcony = byName["Balcony_Floor"].bounds, parapet = byName["Balcony_Parapet"].bounds, window = byName["EastWin_Sash_02_Glass"].bounds;
        float balconyTop = balcony.max.y, ground = byName["Road"].bounds.max.y;
        outward = new Vector3(Mathf.Sign(parapet.center.x - balcony.center.x), 0f, 0f);
        along = new Vector3(0f, 0f, Mathf.Sign(byName["AC_Outdoor_01_Body"].bounds.center.z - window.center.z));
        origin = new Vector3(outward.x < 0f ? balcony.max.x : balcony.min.x, balconyTop, along.z < 0f ? window.min.z : window.max.z);
        float parapetOuter = outward.x < 0f ? parapet.min.x : parapet.max.x;
        float vMid = (PlatformV0 + PlatformV1) / 2f;
        float tip = PlatformReach + PlankLength;
        float anchorY = PlatformTop + AnchorHeight;
        float armY = anchorY + PulleyHeight + 0.11f; // 腕（トラス）の真ん中の高さ。滑車の上が腕の下に付く

        font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        materials.Clear();
        var root = new GameObject("BungeeJump").transform;

        var yellow = new Parts();   // 黄色く塗った鉄骨（門・腕・手すり）
        var steel = new Parts();    // 灰色の鉄（桁・階段の側板・吊り棒・ワイヤー）
        var stripes = new Parts();  // 黄と黒の縞（段の先・足元の板・柱の根元）
        var treads = new Parts();   // 階段の踏み板（格子）

        // ---- 鉄骨の階段（3 段）。当たり判定は今までと同じ箱、見た目は格子の踏み板・側板・手すり ----
        for (int i = 0; i < 3; i++)
        {
            float top = balconyTop + 0.29f * (i + 1), v0 = 0.13f + 0.35f * i, v1 = 0.48f + 0.35f * i;
            ColliderBox(root, $"Step_{i}", P(0.05f, v0, balconyTop), P(1.15f, v1, top));
            treads.Box(P(0.6f, (v0 + v1) / 2f, top - 0.02f), new Vector3(1.06f, 0.04f, v1 - v0), Quaternion.LookRotation(along));
            stripes.Box(P(0.6f, v0 + 0.025f, top - 0.019f), new Vector3(1.08f, 0.042f, 0.05f), Quaternion.LookRotation(along));
            yellow.Beam(P(1.12f, v0 + 0.08f, top), P(1.12f, v0 + 0.08f, top + 0.95f), 0.04f);
        }
        foreach (float u in new[] { 0.08f, 1.12f })
            steel.Beam(P(u, 0.03f, balconyTop + 0.08f), P(u, PlatformV0 + 0.02f, PlatformTop - 0.13f), 0.05f, 0.22f);
        yellow.Beam(P(1.12f, 0.21f, balconyTop + 0.29f + 0.95f), P(1.12f, PlatformV0 - 0.02f, PlatformTop + 1.02f), 0.045f);
        yellow.Beam(P(1.12f, 0.21f, balconyTop + 0.29f + 0.5f), P(1.12f, PlatformV0 - 0.02f, PlatformTop + 0.55f), 0.035f);
        yellow.Beam(P(1.12f, PlatformV0 - 0.02f, PlatformTop + 1.02f), P(1.3f, PlatformV0 + 0.03f, PlatformTop + 1.05f), 0.045f);

        // ---- ジャンプ台: 縞鋼板の床と、手すりの外の格子の床。下に桁と斜めの支え ----
        Box(root, "Deck", P(0.04f, PlatformV0, PlatformTop - 0.1f), P(GratingStart, PlatformV1, PlatformTop), "DiamondPlate");
        Box(root, "Deck_Grating", P(GratingStart, PlatformV0, PlatformTop - 0.05f), P(PlatformReach, PlatformV1, PlatformTop), "Grating");
        foreach (float v in new[] { PlatformV0 + 0.03f, PlatformV1 - 0.03f })
            yellow.Beam(P(0.04f, v, PlatformTop - 0.065f), P(PlatformReach, v, PlatformTop - 0.065f), 0.06f, 0.12f);
        stripes.Beam(P(PlatformReach - 0.03f, PlatformV0, PlatformTop - 0.065f), P(PlatformReach - 0.03f, PlatformV1, PlatformTop - 0.065f), 0.06f, 0.12f);
        foreach (float v in new[] { PlatformV0 + 0.16f, PlatformV1 - 0.16f })
        {
            steel.Beam(P(0.04f, v, PlatformTop - 0.21f), P(PlatformReach, v, PlatformTop - 0.21f), 0.08f, 0.16f);
            steel.Beam(P(1.33f, v, balconyTop - 0.8f), P(2.85f, v, PlatformTop - 0.27f), 0.09f);
        }
        foreach (float u in new[] { 0.6f, 1.3f, 2.0f, 2.7f })
            steel.Beam(P(u, PlatformV0 + 0.05f, PlatformTop - 0.15f), P(u, PlatformV1 - 0.05f, PlatformTop - 0.15f), 0.06f, 0.08f);
        steel.Beam(P(2.85f, PlatformV0 + 0.16f, PlatformTop - 0.27f), P(1.33f, PlatformV1 - 0.16f, balconyTop - 0.8f), 0.07f);

        var plank = CreatePlank(root, P(PlatformReach, vMid, PlatformTop));

        // 手すり（階段側はバルコニーの上だけ開けておく）。台の先は、板の両わきにも手すりを付けて、板からだけ飛べるようにする
        Railing(yellow, stripes, root, "Rail_A", P(1.3f, PlatformV0 + 0.03f, PlatformTop), P(PlatformReach - 0.02f, PlatformV0 + 0.03f, PlatformTop));
        Railing(yellow, stripes, root, "Rail_B", P(0.1f, PlatformV1 - 0.03f, PlatformTop), P(PlatformReach - 0.02f, PlatformV1 - 0.03f, PlatformTop));
        Railing(yellow, stripes, root, "Rail_FrontA", P(PlatformReach - 0.02f, PlatformV0 + 0.03f, PlatformTop), P(PlatformReach - 0.02f, vMid - PlankWidth / 2f, PlatformTop));
        Railing(yellow, stripes, root, "Rail_FrontB", P(PlatformReach - 0.02f, vMid + PlankWidth / 2f, PlatformTop), P(PlatformReach - 0.02f, PlatformV1 - 0.03f, PlatformTop));

        // ---- ロープを吊るす門: トラスの柱 2 本・横の梁・外へ張り出す腕（先にカウントダウンの掲示板を吊る）・腕を支える柱とワイヤー ----
        foreach (float v in new[] { PlatformV0 + 0.1f, PlatformV1 - 0.1f })
        {
            Truss(yellow, P(2.95f, v, PlatformTop), P(2.95f, v, armY + 0.1f), 0.2f, along);
            stripes.Box(P(2.95f, v, PlatformTop + 0.25f), new Vector3(0.23f, 0.5f, 0.23f), Quaternion.LookRotation(outward));
            steel.Box(P(2.95f, v, PlatformTop + 0.01f), new Vector3(0.34f, 0.02f, 0.34f), Quaternion.LookRotation(outward));
            ColliderBox(root, $"Gantry_Post_{v:F2}", P(2.84f, v - 0.11f, PlatformTop), P(3.06f, v + 0.11f, armY));
        }
        Truss(yellow, P(2.95f, PlatformV0 + 0.1f, armY), P(2.95f, PlatformV1 - 0.1f, armY), 0.2f, Vector3.up);
        Truss(yellow, P(2.95f, vMid, armY), P(tip + 1.6f, vMid, armY), 0.22f, Vector3.up);
        Truss(yellow, P(2.95f, vMid, armY + 0.11f), P(2.95f, vMid, armY + 1.05f), 0.16f, outward);
        steel.Rod(P(2.95f, vMid, armY + 1.05f), P(tip + 0.1f, vMid, armY + 0.11f), 0.012f);
        steel.Rod(P(2.95f, vMid, armY + 1.05f), P(tip + 1.55f, vMid, armY + 0.11f), 0.012f);

        // ロープの付け根（滑車。Tripo のモデルが無ければ鉄の塊）
        var anchor = Marker(root, "Anchor", P(tip - 0.04f, vMid, anchorY), outward);
        if (PlaceModel(root, "Bungee_Pulley", anchor.position, outward, new Vector3(0f, PulleyHeight, 0f), false) == null)
        {
            steel.Box(anchor.position + Vector3.up * 0.18f, new Vector3(0.1f, 0.2f, 0.16f), Quaternion.LookRotation(outward));
            steel.Rod(anchor.position + Vector3.up * 0.28f, P(tip - 0.04f, vMid, armY - 0.1f), 0.015f);
        }

        // ---- 電光掲示板 ----
        // 台の上の案内は、門の柱（間が 0.8m）より幅が広いので、柱より手前（デッキ側）に吊る
        var platformCenter = P(2.72f, vMid, PlatformTop + 2.35f);
        var platformSign = LedBoard(root, "PlatformSign", "ハーネスを付けてから\n飛ぼう", platformCenter, outward, 0.1f,
            new LedStyle { palette = new[] { LedGreen, LedYellow, LedCyan } });
        foreach (float dv in new[] { -0.4f, 0.4f })
            steel.Rod(platformCenter + along * dv + Vector3.up * 0.21f, P(2.88f, vMid + dv, armY - 0.1f), 0.01f);

        // カウントダウンは、板の先に立って前を見たときに目に入るよう、腕の先に吊った大きな掲示板に出す（裏にも出して、通りからも見える）
        var countdownCenter = P(tip + 1.4f, vMid, PlatformTop + 1.75f);
        var countdownSign = LedBoard(root, "CountdownSign", "", countdownCenter, outward, 0.5f,
            new LedStyle { doubleSided = true, palette = new[] { LedRed }, width = 1.3f, height = 0.85f, idle = "JUMP!", bulbColor = new Color(1f, 0.35f, 0.2f), blinkTime = 0.35f });
        HangSign(steel, countdownCenter, 0.5f, 0.85f / 2f + 0.06f, armY - 0.11f);

        // 門の上の看板（通りからも、台の上からも見える。文字が流れる）
        var bannerCenter = P(3.17f, vMid, armY + 0.4f);
        LedBoard(root, "Banner", "", bannerCenter, -outward, 0.17f, new LedStyle
        {
            doubleSided = true,
            marquee = "★ BUNGEE JUMP 23m ★ 窓の外でバンジー！ ",
            marqueeWidth = 9,
            palette = new[] { LedPink, LedYellow, LedCyan, LedGreen },
            bulbColor = new Color(1f, 0.55f, 0.85f),
        });
        foreach (float dv in new[] { -0.5f, 0.5f })
            steel.Beam(bannerCenter + along * dv + Vector3.down * 0.2f, P(2.95f, vMid + dv, armY + 0.1f), 0.03f);

        // ハーネスを掛けるフック（柱を伸ばして、上に案内を付ける）
        Cylinder(root, "HookPost", P(1.5f, PlatformV1 - 0.08f, PlatformTop), 2.45f, 0.05f, "Metal");
        Beam(root, "HookArm", P(1.5f, PlatformV1 - 0.08f, PlatformTop + 1.85f), P(1.5f, PlatformV1 - 0.3f, PlatformTop + 1.85f), 0.05f, "Metal");
        var harness = CreateHarness(root, P(1.5f, PlatformV1 - 0.26f, PlatformTop + 1.32f));
        LedBoard(root, "HarnessSign", "ハーネスをつかんで付ける\n（グリップ / E キー）", P(1.5f, PlatformV1 - 0.45f, PlatformTop + 2.2f), outward, 0.07f,
            new LedStyle { palette = new[] { LedCyan, LedWhite } });

        // 係の人（Tripo のモデル。フックの側に立ち、板へ歩いていく人の方を向く。声もここから）
        Vector3 staffFeet = P(2.45f, PlatformV1 - 0.24f, PlatformTop);
        var staff = PlaceModel(root, "Bungee_Staff", staffFeet, (outward * 0.35f - along).normalized, new Vector3(0f, 1.72f, 0f), false);
        if (staff != null)
        {
            var body = staff.gameObject.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0f, 0.86f, 0f);
            body.height = 1.72f;
            body.radius = 0.18f;
        }

        // 部屋の中の案内（窓のとなり）
        float right = Vector3.Dot(Vector3.Cross(Vector3.up, outward), along);
        LedBoard(root, "WindowSign", $"バンジージャンプ\n{(right > 0f ? "この窓を開けて外へ →" : "← この窓を開けて外へ")}",
            new Vector3(window.center.x - outward.x * 0.17f, 1.55f, window.center.z - along.z * 1.05f), outward, 0.09f,
            new LedStyle { palette = new[] { LedYellow, LedPink, LedCyan } });

        // ---- 下のエアクッション。底が原点で、着地すると BungeeJump が弾ませる ----
        float cushionU = tip + 0.1f;
        MoveCarsAway(room, P(cushionU, vMid, ground));
        var cushion = CreateCushion(root, P(cushionU, vMid, ground), out var cushionModel);
        float cushionTop = ground + CushionHeight;
        Vector3 padCenter = P(cushionU, vMid - 1.25f, cushionTop);
        var returnPad = Marker(root, "ReturnPad", padCenter + Vector3.down * 0.02f, outward);
        float padSurface = cushionModel != null ? Mathf.Max(cushionTop, SurfaceHeight(cushionModel, padCenter, cushionTop)) : cushionTop;
        ReturnPadEffect(root, new Vector3(padCenter.x, padSurface, padCenter.z));
        var groundCenter = P(cushionU + 2.4f, vMid + 1.1f, cushionTop + 1.4f);
        var groundSign = LedBoard(root, "GroundSign", "着地成功！\n光る輪で上に戻る", groundCenter, outward, 0.14f,
            new LedStyle { doubleSided = true, palette = new[] { LedGreen, LedYellow, LedPink } });
        Cylinder(root, "GroundSign_Pole", P(cushionU + 2.4f, vMid + 1.1f, ground), cushionTop + 1.05f - ground, 0.05f, "Metal");

        var returnPoint = new GameObject("ReturnPoint").transform;
        returnPoint.SetParent(root, false);
        returnPoint.SetPositionAndRotation(P(0.55f, -0.45f, balconyTop), Quaternion.LookRotation(along));

        var jumpEdge = Marker(root, "JumpEdge", P(tip, vMid, PlatformTop), outward);
        var dropLine = Marker(root, "DropLine", new Vector3(parapetOuter, PlatformTop, P(0f, vMid, 0f).z), outward);

        var cord = new GameObject("Cord").AddComponent<LineRenderer>();
        cord.transform.SetParent(root, false);
        cord.widthMultiplier = 0.04f;
        cord.numCapVertices = 2;
        cord.textureMode = LineTextureMode.Tile;
        cord.sharedMaterial = GetMaterial("Cord");
        cord.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // まとめた部品を置く
        yellow.Build(root, "Steel_Yellow", "Safety");
        steel.Build(root, "Steel_Grey", "Metal");
        stripes.Build(root, "Stripes", "Hazard");
        treads.Build(root, "Stair_Treads", "StepGrating");

        AddLightProbes(root, ground, cushionTop, vMid);

        // 音: 台のまわりの風（近づくほど聞こえる）と、係の人の声
        var ambient = new GameObject("AmbientWind").AddComponent<AudioSource>();
        ambient.transform.SetParent(root, false);
        ambient.transform.position = P(tip, vMid, PlatformTop + 1.5f);
        ambient.clip = LoadClip("Bungee_WindAmbient");
        ambient.loop = true;
        ambient.volume = 0.7f;
        ambient.spatialBlend = 1f;
        ambient.minDistance = 3f;
        ambient.maxDistance = 30f;
        var staffVoice = new GameObject("StaffVoice").AddComponent<AudioSource>();
        staffVoice.transform.SetParent(root, false);
        staffVoice.transform.position = staff != null ? staffFeet + Vector3.up * 1.6f : P(2.6f, vMid, PlatformTop + 1.6f);
        staffVoice.playOnAwake = false;
        staffVoice.spatialBlend = 0.6f;
        staffVoice.minDistance = 4f;

        var bungee = root.gameObject.AddComponent<BungeeJump>();
        bungee.anchor = anchor;
        bungee.jumpEdge = jumpEdge;
        bungee.dropLine = dropLine;
        bungee.platformTop = PlatformTop;
        bungee.cushionTop = cushionTop;
        bungee.cushion = cushion;
        bungee.returnPad = returnPad;
        bungee.returnPoint = returnPoint;
        bungee.harness = harness;
        bungee.cord = cord;
        bungee.platformSign = platformSign;
        bungee.countdownSign = countdownSign;
        bungee.groundSign = groundSign;
        bungee.plank = plank;
        bungee.plankLength = PlankLength;
        bungee.plankHalfWidth = PlankWidth / 2f;
        bungee.ambientWind = ambient;
        bungee.staffVoice = staffVoice;
        bungee.heartbeat = LoadClip("Bungee_heartbeat");
        bungee.fallWind = LoadClip("Bungee_fall_wind");
        bungee.jumpWhoosh = LoadClip("Bungee_JumpWhoosh");
        bungee.cordStretch = LoadClip("Bungee_cord_stretch");
        bungee.plankCreak = LoadClip("Bungee_plank_creak");
        bungee.carabiner = LoadClip("Bungee_carabiner");
        bungee.cushionLand = LoadClip("Bungee_cushion_land");
        bungee.cheer = LoadClip("Bungee_Cheer");
        bungee.voiceNice = LoadClip("Bungee_Voice_nice_jump");
        bungee.shrinkSound = LoadClip("Mini_Shrink");
        bungee.growSound = LoadClip("Mini_Grow");
        bungee.voiceCount = new[] { "count_3", "count_2", "count_1", "count_bungee" }.Select(n => LoadClip("Bungee_Voice_" + n)).ToArray();
        bungee.windStreakMaterial = GetMaterial("WindStreak");
        bungee.wornHarnessModel = AssetDatabase.LoadAssetAtPath<GameObject>(HarnessModelPath);
        harness.bungee = bungee;

        string models = string.Join(", ", new[] { "Bungee_Cushion", "Bungee_Plank", "Bungee_Pulley", "Bungee_Staff" }
            .Select(n => $"{n} {(AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelFolder}/{n}/{n}.fbx") != null ? "あり" : "なし（代わりの形）")}"));
        Debug.Log($"[バンジー] バンジー: 台の高さ {PlatformTop} m, クッションの上 {cushionTop:F2} m（落差 {PlatformTop - cushionTop:F1} m）, 付け根 {anchor.position} / モデル: {models}");
        return bungee;
    }

    /// <summary>掲示板を上の梁から 2 本の棒で吊る。</summary>
    static void HangSign(Parts steel, Vector3 center, float halfSpan, float halfHeight, float beamBottom)
    {
        foreach (float dv in new[] { -halfSpan, halfSpan })
        {
            Vector3 top = center + along * dv + Vector3.up * halfHeight;
            steel.Rod(top, new Vector3(top.x, beamBottom, top.z), 0.01f);
        }
    }

    /// <summary>手すり（柱・上の横棒・中の横棒・足元の縞の板）と、すり抜けないための当たり判定。a・b は床の高さの両端。</summary>
    static void Railing(Parts yellow, Parts stripes, Transform root, string name, Vector3 a, Vector3 b)
    {
        int posts = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.8f));
        for (int i = 0; i <= posts; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)posts);
            yellow.Beam(p, p + Vector3.up * 1.05f, 0.045f);
        }
        yellow.Beam(a + Vector3.up * 1.05f, b + Vector3.up * 1.05f, 0.05f);
        yellow.Beam(a + Vector3.up * 0.52f, b + Vector3.up * 0.52f, 0.035f);
        stripes.Beam(a + Vector3.up * 0.06f, b + Vector3.up * 0.06f, 0.014f, 0.12f);
        Vector3 min = Vector3.Min(a, b) - new Vector3(0.03f, 0f, 0.03f), max = Vector3.Max(a, b) + new Vector3(0.03f, 1.1f, 0.03f);
        ColliderBox(root, name + "_Collider", min, max);
    }

    /// <summary>台の先の、手すりのない細い板。付け根が原点で、BungeeJump が乗った人に合わせてしならせる。</summary>
    static Transform CreatePlank(Transform parent, Vector3 basePosition)
    {
        var pivot = new GameObject("Plank").transform;
        pivot.SetParent(parent, false);
        pivot.SetPositionAndRotation(basePosition, Quaternion.LookRotation(outward));
        // 乗る面（当たり判定）は今までと同じ箱
        var board = new GameObject("Board");
        board.transform.SetParent(pivot, false);
        board.transform.localPosition = new Vector3(0f, -0.03f, PlankLength / 2f);
        board.AddComponent<BoxCollider>().size = new Vector3(PlankWidth, 0.06f, PlankLength);

        // 見た目: Tripo の板（上面を台の高さにそろえる）。無ければ木の箱と先の縞
        var model = PlaceModel(pivot, "Bungee_Plank", basePosition, outward, new Vector3(PlankWidth, 0f, PlankLength), true);
        if (model != null)
        {
            var b = LocalBounds(pivot, model.gameObject);
            model.localPosition = new Vector3(-b.center.x, -b.max.y + model.localPosition.y, -b.min.z + model.localPosition.z);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0); // しなるので動かす
        }
        else
        {
            Part(pivot, PrimitiveType.Cube, new Vector3(0f, -0.03f, PlankLength / 2f), new Vector3(PlankWidth, 0.06f, PlankLength), "Wood");
            Part(pivot, PrimitiveType.Cube, new Vector3(0f, 0.002f, PlankLength - 0.06f), new Vector3(PlankWidth, 0.004f, 0.1f), "Hazard");
        }
        return pivot;
    }

    /// <summary>クッションを置く所に止まっている車を、道路に沿ってずらす。</summary>
    static void MoveCarsAway(GameObject room, Vector3 cushionBase)
    {
        var area = new Bounds(cushionBase, new Vector3(CushionSize + 0.6f, 50f, CushionSize + 0.6f));
        foreach (var car in room.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Car_") && t.parent != null && t.parent.name == "Ext_Street"))
        {
            var renderers = car.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            if (!b.Intersects(area)) continue;
            car.position -= along * 6f;
            Debug.Log($"[バンジー] クッションの場所にあった {car.name} を 6m ずらした");
        }
    }

    static AudioClip LoadClip(string name)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioFolder}/{name}.mp3");
        if (clip == null) Debug.LogWarning($"[バンジー] 音が見つかりません: {AudioFolder}/{name}.mp3");
        return clip;
    }

    /// <summary>格子の床の模様（棒は不透明、すき間は透明）。</summary>
    static Texture2D GratingTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(GratingTexturePath);
        if (existing != null) return existing;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool bar = x % 16 < 3 || y % 32 < 3; // 細かい縦の棒と、間隔の広い横の棒
            tex.SetPixel(x, y, bar ? Color.white : new Color(1f, 1f, 1f, 0f));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(GratingTexturePath));
        File.WriteAllBytes(GratingTexturePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(GratingTexturePath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(GratingTexturePath);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.5f;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(GratingTexturePath);
    }

    static BungeeHarness CreateHarness(Transform parent, Vector3 position)
    {
        // 前（モデルの +Z）を、台の真ん中を歩いてくる人の方へ向けて掛ける
        var root = new GameObject("Harness");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-along));
        var box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(0.42f, 0.75f, 0.24f);

        GameObject model;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(HarnessModelPath);
        if (asset != null)
        {
            // Tripo で作った全身用ハーネス（高さ約 0.72m、前が +Z）。真ん中をフックの位置にそろえる
            model = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            model.name = "Model";
            var bounds = RendererBounds(model);
            model.transform.position += root.transform.position - bounds.center;
            box.size = new Vector3(0.42f, bounds.size.y + 0.03f, 0.24f);
        }
        else
        {
            Debug.LogWarning($"[バンジー] ハーネスのモデルが見つからないので、箱で代わりを作る: {HarnessModelPath}");
            model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            Part(model.transform, PrimitiveType.Cube, new Vector3(0f, -0.12f, 0f), new Vector3(0.36f, 0.08f, 0.2f), "Harness");      // 腰ベルト
            Part(model.transform, PrimitiveType.Cube, new Vector3(-0.09f, 0.1f, 0f), new Vector3(0.05f, 0.42f, 0.03f), "Harness");  // 肩ベルト
            Part(model.transform, PrimitiveType.Cube, new Vector3(0.09f, 0.1f, 0f), new Vector3(0.05f, 0.42f, 0.03f), "Harness");
            Part(model.transform, PrimitiveType.Cube, new Vector3(-0.08f, -0.28f, 0f), new Vector3(0.12f, 0.06f, 0.16f), "Harness"); // 脚ベルト
            Part(model.transform, PrimitiveType.Cube, new Vector3(0.08f, -0.28f, 0f), new Vector3(0.12f, 0.06f, 0.16f), "Harness");
            Part(model.transform, PrimitiveType.Capsule, new Vector3(0f, 0.33f, 0f), new Vector3(0.05f, 0.06f, 0.05f), "Metal");   // カラビナ
        }

        // ロープは背中の上（フックに掛ける所）に付ける
        var cordPoint = new GameObject("CordPoint").transform;
        cordPoint.SetParent(root.transform, false);
        cordPoint.localPosition = new Vector3(0f, box.size.y / 2f, -0.06f);

        var harness = root.AddComponent<BungeeHarness>();
        harness.model = model;
        harness.cordPoint = cordPoint;
        return harness;
    }

    static Bounds RendererBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>エアクッション。見た目（root）は着地で弾ませ、当たり判定は別にして動かさない。model は Tripo のモデル（無ければ null）。</summary>
    static Transform CreateCushion(Transform parent, Vector3 basePosition, out Transform model)
    {
        var root = new GameObject("Cushion");
        root.transform.SetParent(parent, false);
        root.transform.position = basePosition;
        // 当たり判定は弾ませる見た目（root）とは別にして動かさない。面が上下すると、ミニキャラの小さな体がクッションに沈んで抜けてしまう
        var collision = new GameObject("Cushion_Collider");
        collision.transform.SetParent(parent, false);
        collision.transform.position = basePosition;
        var box = collision.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, CushionHeight / 2f, 0f);
        box.size = new Vector3(CushionSize, CushionHeight, CushionSize);

        model = PlaceModel(root.transform, "Bungee_Cushion", basePosition, outward, new Vector3(CushionSize + 0.1f, CushionHeight + 0.02f, CushionSize + 0.1f), true);
        if (model != null)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0); // 弾むので動かす
            return root.transform;
        }

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

    static void AddLightProbes(Transform parent, float ground, float cushionTop, float vMid)
    {
        var probes = new List<Vector3>();
        float tip = PlatformReach + PlankLength;
        foreach (float u in new[] { 0.2f, 1.6f, 3.4f, tip + 0.3f })
        foreach (float v in new[] { PlatformV0 - 0.9f, PlatformV1 + 0.4f })
        foreach (float y in new[] { 0.3f, PlatformTop + 0.4f, PlatformTop + 2.6f, PlatformTop + 3.6f })
            probes.Add(P(u, v, y));
        for (float y = PlatformTop; y > cushionTop; y -= 3f)
        {
            probes.Add(P(tip - 1.2f, vMid, y));
            probes.Add(P(tip + 1.2f, vMid, y));
        }
        foreach (float u in new[] { tip - 3f, tip + 3f })
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

    /// <summary>素材を Assets/Bungee/Materials/Bungee_&lt;name&gt;.mat から取る。中身は毎回ここで作り直して上書きする（作り方を変えたら次にシーンを作るときに反映される）。</summary>
    static Material GetMaterial(string name)
    {
        if (materials.TryGetValue(name, out var cached)) return cached;
        return SaveMaterial(name, CreateMaterial(name));
    }

    static Material SaveMaterial(string name, Material fresh)
    {
        string path = $"{MaterialFolder}/Bungee_{name}.mat";
        if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets", "Bungee");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder(AssetFolder, "Materials");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            AssetDatabase.CreateAsset(fresh, path);
            mat = fresh;
        }
        else
        {
            mat.shader = fresh.shader;
            mat.CopyPropertiesFromMaterial(fresh);
            mat.shaderKeywords = fresh.shaderKeywords;
            mat.renderQueue = fresh.renderQueue;
            EditorUtility.SetDirty(mat);
            Object.DestroyImmediate(fresh);
        }
        materials[name] = mat;
        return mat;
    }

    static Material CreateMaterial(string name)
    {
        switch (name)
        {
            case "Grating":
            case "StepGrating":
            {
                // 格子の床。すき間から下の通りが見える
                var grating = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                grating.SetTexture("_BaseMap", GratingTexture());
                grating.SetColor("_BaseColor", new Color(0.42f, 0.44f, 0.48f));
                grating.SetFloat("_Metallic", 0.7f);
                grating.SetFloat("_Smoothness", 0.4f);
                grating.SetFloat("_AlphaClip", 1f);
                grating.SetFloat("_Cutoff", 0.5f);
                grating.SetFloat("_Cull", 0f);
                grating.EnableKeyword("_ALPHATEST_ON");
                grating.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                grating.mainTextureScale = name == "Grating"
                    ? new Vector2((PlatformReach - GratingStart) / 0.2f, (PlatformV1 - PlatformV0) / 0.2f)
                    : new Vector2(1.06f / 0.12f, 0.35f / 0.12f);
                return grating;
            }
            case "WindStreak":
            case "ReturnSpark":
            {
                // 落ちる間の風の筋・戻る輪の光の粒（加算で光る）
                bool spark = name == "ReturnSpark";
                var streak = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                streak.SetColor("_BaseColor", spark ? new Color(0.45f, 1f, 1f, 1f) : new Color(0.9f, 0.93f, 1f, 0.75f));
                if (spark) streak.SetTexture("_BaseMap", BulbTexture());
                streak.SetFloat("_Surface", 1f);
                streak.SetFloat("_Blend", 2f);
                streak.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                streak.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                streak.SetFloat("_ZWrite", 0f);
                streak.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                streak.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                return streak;
            }
            case "Cord":
            {
                // 編み目の模様。目のすぐ近くは透かす（BanjiRun/Rope）
                var rope = new Material(Shader.Find("BanjiRun/Rope"));
                rope.SetTexture("_BaseMap", RopeTexture());
                rope.SetFloat("_FadeNear", 0.4f);
                rope.SetFloat("_FadeFar", 1f);
                rope.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                return rope;
            }
            case "DiamondPlate": return Lit(new Color(0.8f, 0.82f, 0.86f), 0.55f, 0.75f, DiamondPlateAlbedo(), DiamondPlateNormal(), new Vector2(6f, 6f));
            case "Hazard": return Lit(Color.white, 0.35f, 0.1f, HazardTexture(), null, new Vector2(8f, 1f));
            case "SignFrame": return Lit(new Color(0.07f, 0.075f, 0.09f), 0.45f, 0.6f);
            case "LEDPanel": return Glow(Color.white, LedPanelTexture(), Vector2.one, "opaque", false);
            case "LEDGrid": return Glow(new Color(0f, 0f, 0f, 0.93f), LedGridTexture(), Vector2.one, "alpha", false, 10);
            case "ReturnRing": return Glow(new Color(0.35f, 1f, 1f, 1f), null, Vector2.one, "add", true);
            case "ReturnBeam": return Glow(new Color(0.25f, 0.85f, 1f, 0.6f), BeamTexture(), Vector2.one, "add", true);
        }
        var (color, smoothness, metallic) = name switch
        {
            "Metal" => (new Color(0.32f, 0.34f, 0.38f), 0.45f, 0.6f),
            "Wood" => (new Color(0.55f, 0.38f, 0.22f), 0.25f, 0f),
            "Safety" => (new Color(1f, 0.74f, 0.05f), 0.5f, 0.15f),
            "Red" => (new Color(0.86f, 0.13f, 0.1f), 0.35f, 0f),
            "CushionTop" => (new Color(1f, 0.83f, 0.18f), 0.35f, 0f),
            "White" => (new Color(0.95f, 0.95f, 0.95f), 0.3f, 0f),
            "Harness" => (new Color(1f, 0.45f, 0.05f), 0.3f, 0f),
            _ => (Color.magenta, 0.5f, 0f),
        };
        return Lit(color, smoothness, metallic);
    }
}
