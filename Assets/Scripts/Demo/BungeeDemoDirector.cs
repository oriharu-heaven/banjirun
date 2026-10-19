#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

/// <summary>
/// エディター専用。VR シーンでも画面版のシーンでも、「窓を開ける → バルコニー → ハーネス → バンジー → クッションに着地 → 戻る」を自動で操作し、
/// 1 人称の映像（右上に外から見た様子、下に字幕）と音を mp4 にする。ffmpeg が要る。
/// BungeeDemoRecorder（メニュー「BanjiRun/VR/6. Record Bungee Demo」「BanjiRun/画面版/3. Record Bungee Demo」）が再生前にシーンへ置く。
/// 操作は FirstPersonControls を通すので、移動・当たり判定・ドア・ハーネス・バンジーは実際のゲームと同じ動き。
/// mini を true にすると、最初に M キーでミニキャラになってから同じ流れを通り（段はよじ登る）、最後に等身大に戻る。
/// </summary>
[DefaultExecutionOrder(1000)]
public class BungeeDemoDirector : MonoBehaviour
{
    public string outputPath = "Recordings/BungeeDemo.mp4";
    public string ffmpegPath = "/opt/homebrew/bin/ffmpeg";
    public int width = 1280;
    public int height = 720;
    public int fps = 30;
    public float timeLimit = 150f;
    public bool mini;

    const int AvatarLayer = 29;   // 外からのカメラにだけ映すプレイヤーの体
    const int CaptionLayer = 28;  // 字幕用カメラにだけ映す文字
    const int FirstPersonLayer = 27; // 1 人称のカメラにだけ映す物（風の筋）
    const int CaptionHeight = 64;
    const int InsetWidth = 448, InsetHeight = 252, InsetMargin = 16;

    PlayerBody body;
    FirstPersonControls controls;
    BungeeJump bungee;
    Camera cam, spectator, captionCamera;
    TextMesh captionText;
    Transform avatar;
    RenderTexture frameRT, insetRT, captionRT, labelRT;
    Texture2D readback;
    byte[] frameBytes;
    Process ffmpeg;
    Stream ffmpegInput;
    // 音: バッチモードでは Unity の音が鳴らない（スピーカーにも出ない）ので、鳴らした音とループの音量を記録して、あとで同じ音のファイルから重ねる
    const int AudioRate = 44100;
    class LoopTrack
    {
        public AudioSource source;
        public float spatialMin;  // 0 なら 2D
        public readonly List<float> volume = new List<float>(), pitch = new List<float>();
    }
    struct OneShot { public int frame; public AudioClip clip; public float volume; }
    readonly List<OneShot> oneShots = new List<OneShot>();
    readonly List<LoopTrack> loops = new List<LoopTrack>();
    FirstPersonControls.Commands commands;
    int frames;
    bool finished;
    bool jumped;

    void Awake() => Time.captureFramerate = fps;
    void OnEnable() => BungeeJump.SoundPlayed += OnSound;
    void OnDisable() => BungeeJump.SoundPlayed -= OnSound;

    void OnSound(AudioClip clip, float volume, Vector3? position)
    {
        if (ffmpegInput == null || finished) return;
        if (position.HasValue)
        {
            // 係の人の声は半分 3D（BungeeBuilder の staffVoice）、それ以外の 3D の音は PlayClipAtPoint の既定（最小距離 1m）
            bool staff = bungee.staffVoice != null && position.Value == bungee.staffVoice.transform.position;
            float attenuation = Rolloff(Vector3.Distance(cam.transform.position, position.Value), staff ? bungee.staffVoice.minDistance : 1f);
            volume *= staff ? Mathf.Lerp(1f, attenuation, bungee.staffVoice.spatialBlend) : attenuation;
        }
        oneShots.Add(new OneShot { frame = frames, clip = clip, volume = volume });
    }

    static float Rolloff(float distance, float minDistance) => distance <= minDistance ? 1f : minDistance / distance;

    IEnumerator Start()
    {
        body = FindFirstObjectByType<PlayerBody>();
        controls = FindFirstObjectByType<FirstPersonControls>();
        bungee = FindFirstObjectByType<BungeeJump>();
        if (body == null || controls == null || bungee == null)
        {
            Finish(false, "PlayerBody / FirstPersonControls / BungeeJump が見つかりません");
            yield break;
        }
        Application.runInBackground = true;
        yield return null; // 他の Start（FirstPersonControls など）を先に済ませる
        controls.Scripted = true;
        cam = body.Camera;
        cam.fieldOfView = 72f;
        cam.cullingMask &= ~((1 << AvatarLayer) | (1 << CaptionLayer));
        SetupRecording();
        yield return Run();
        Finish(jumped && bungee.CurrentPhase == BungeeJump.Phase.Idle, jumped ? "完了" : "バンジーまでたどり着かなかった");
    }

    // ---- 台本 ----

    IEnumerator Run()
    {
        Vector3 outward = bungee.jumpEdge.forward;
        Vector3 along = bungee.returnPoint.forward;
        Transform glass = GameObject.Find("EastWin_Sash_02_Glass")?.transform;
        Vector3 glassCenter = glass != null ? glass.GetComponent<Renderer>().bounds.center : bungee.returnPoint.position - outward;
        Vector3 floor = new Vector3(0f, body.Feet.y, 0f);
        // 机の間の通路を通り、窓の手前の舞台（高さ 20cm）には縁から 25cm 内側で上がって窓へ（縁ぎりぎりは体が段に引っかかる）
        Vector3 aisle = Flat(glassCenter - outward * 0.75f + along * 0.1f) + floor;
        Vector3 beforeStage = aisle - outward * 1.0f;
        Vector3 windowInside = Flat(glassCenter - outward * 0.75f + along * 0.28f) + floor;
        Vector3 balcony = bungee.returnPoint.position;
        Vector3 deck = Vector3.up * bungee.platformTop;

        // ホールの中、窓から 4 m ほどの所から
        body.TeleportFeetTo(aisle - outward * 3.6f, Quaternion.LookRotation(outward).eulerAngles.y);
        string version = body is VRPlayerBody ? "VR 版" : "画面版";
        Caption(mini ? $"BanjiRun ― ミニキャラでバンジー（{version}）" : $"BanjiRun ― 窓の外でバンジージャンプ（{version}）");
        yield return LookAt(glassCenter + Vector3.up * 0.4f, 1.5f);
        if (mini)
        {
            Caption("M キーでミニキャラに（身長 25cm）");
            yield return Resize();
            yield return LookAt(glassCenter + Vector3.up * 0.4f, 1.0f);
        }

        Caption(mini ? "① 舞台によじ登って、窓を開ける" : "① 窓まで歩いて、窓を開ける");
        yield return WalkTo(beforeStage);
        yield return WalkTo(windowInside);
        yield return LookAt(glassCenter, 0.7f);
        yield return Use();
        yield return Wait(1.2f);

        Caption(mini ? "② バルコニーへ出て、階段を 1 段ずつよじ登る" : "② バルコニーへ出て、階段でジャンプ台へ");
        yield return WalkTo(Flat(windowInside + outward * 1.3f) + floor);
        yield return WalkTo(balcony);
        yield return WalkTo(Flat(balcony + along * 0.6f) + Vector3.up * balcony.y);
        yield return WalkTo(Flat(balcony + along * 1.95f) + deck);
        yield return WalkTo(Flat(balcony + along * 1.95f + outward * 0.6f) + deck);

        Caption("③ ハーネスをつかんで、飛ぶ準備");
        yield return LookAt(bungee.harness.transform.position, 0.9f);
        yield return Use();
        yield return Wait(0.8f);

        Caption("④ 手すりの外は、床が透けている…");
        float vMid = Vector3.Dot(bungee.jumpEdge.position - balcony, along);
        Vector3 onGrating = Flat(balcony + along * vMid + outward * 2.2f) + deck;
        yield return WalkTo(onGrating);
        yield return LookAt(onGrating + outward * 0.6f + Vector3.down * 3f, 1.6f);

        Caption(mini ? "⑤ 細い板の先へ。下まで 23m（ミニの目には 150m）" : "⑤ 細い板の先へ。下まで 23m");
        Vector3 edge = bungee.jumpEdge.position - outward * 0.25f;
        yield return WalkTo(Flat(edge) + deck);
        yield return LookAt(bungee.cushion.position, 1.8f);

        Caption("⑥ 3・2・1…");
        yield return LookAt(bungee.countdownSign.transform.position, 0.8f);
        for (float w = 0f; w < 4f && bungee.countdownSign.text != "バンジー！"; w += Time.deltaTime) yield return null;
        yield return Wait(0.25f);

        Caption("⑦ バンジー！");
        float t = 0f;
        while (bungee.CurrentPhase == BungeeJump.Phase.Harnessed && t < 3f)
        {
            commands.move = Vector2.up;
            yield return LookStep(bungee.cushion.position, 4f);
            t += Time.deltaTime;
        }
        commands = default;
        jumped = bungee.CurrentPhase == BungeeJump.Phase.Falling;
        // 落ちる間は下のクッションを見る。地面すれすれで弾んだら、空の方を見上げる
        while (bungee.CurrentPhase == BungeeJump.Phase.Falling)
        {
            if (bungee.Bounces == 1 && captionText.text.StartsWith("⑦")) Caption("⑧ 地面すれすれで、ぐいっと引き戻される！");
            Vector3 target = bungee.Bounces == 0 ? bungee.cushion.position : bungee.cushion.position + outward * 6f + Vector3.up * 12f;
            yield return LookStep(target, 3f);
        }

        Caption("⑨ 何度か弾んだら、ゆっくり降ろしてもらう");
        while (bungee.CurrentPhase == BungeeJump.Phase.Lowering)
            yield return LookStep(bungee.cushion.position, 2f);

        Caption("⑩ クッションに着地！");
        Debug.Log($"[Demo]   着地した足 {body.Feet:F2} / クッションの中心 {bungee.cushion.position:F2}（ずれ {Flat(body.Feet - bungee.cushion.position).magnitude:F2}m）/ 光る輪 {bungee.returnPad.position:F2}");
        yield return LookAt(bungee.returnPad.position + outward * 3f, 1.4f);
        yield return Wait(0.8f);

        Caption("⑪ 光る輪に入ると、バルコニーへ戻る");
        yield return LookAt(bungee.returnPad.position, 0.8f);
        t = 0f;
        while (bungee.CurrentPhase == BungeeJump.Phase.Landed && t < 15f)
        {
            yield return WalkStep(bungee.returnPad.position);
            t += Time.deltaTime;
        }
        if (bungee.CurrentPhase == BungeeJump.Phase.Landed)
            Debug.LogWarning($"[Demo] 光る輪に入れなかった: 足 {body.Feet:F2} / 光る輪 {bungee.returnPad.position:F2}");
        commands = default;

        Caption("おかえり！ もう一回飛べる");
        yield return LookAt(Flat(balcony + along * 3f + outward * 2f) + deck + Vector3.up * 1.2f, 1.5f);
        yield return Wait(1.5f);
        if (mini)
        {
            Caption("M キーで等身大に戻る");
            yield return Resize();
            yield return Wait(0.6f);
        }
    }

    // ---- 操作の小道具（FirstPersonControls に入力を渡す） ----

    void Update()
    {
        if (Time.time > timeLimit && !finished) Finish(false, "時間切れ");
    }

    /// <summary>
    /// 台本（コルーチン）がこのフレームで決めた入力を、次のフレームの FirstPersonControls に渡す。
    /// コルーチンは Update のあと・LateUpdate の前に動くので、ここで渡すと遅れが 1 コマで済む
    /// （Update で渡すと 2 コマ遅れ、向きを直すたびに行き過ぎて、映像が左右に細かく揺れていた）。
    /// </summary>
    void PushCommands()
    {
        if (controls == null) return;
        controls.ScriptedCommands = commands;
        commands.use = false;
        commands.resize = false;
        commands.jump = false;
        commands.look = Vector2.zero;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
    }

    IEnumerator Resize()
    {
        commands.resize = true;
        yield return null;
        yield return Wait(1.2f);
    }

    IEnumerator Use()
    {
        commands.use = true;
        yield return null;
        yield return null;
    }

    IEnumerator WalkTo(Vector3 target, float timeout = 12f)
    {
        float t = 0f;
        for (; t < timeout; t += Time.deltaTime)
        {
            if (Flat(target - body.Feet).magnitude < 0.12f) break;
            yield return WalkStep(target);
        }
        if (t >= timeout)
        {
            // 何に引っかかったか（体の少し先にある当たり判定）
            var cc = body.GetComponent<CharacterController>();
            Vector3 dir = Flat(target - body.Feet).normalized;
            Vector3 center = body.transform.TransformPoint(cc.center) + dir * 0.08f;
            float half = Mathf.Max(0f, cc.height / 2f - cc.radius);
            var near = Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half, cc.radius + 0.03f, ~0, QueryTriggerInteraction.Ignore);
            string names = string.Join(", ", System.Array.ConvertAll(near, c => $"{c.name}({c.bounds.min.y:F2}〜{c.bounds.max.y:F2})"));
            Debug.LogWarning($"[Demo] 目標まで歩けなかった: 足 {body.Feet:F2} → 目標 {target:F2} / 体の高さ {cc.height:F2} 半径 {cc.radius:F2} / 近くの当たり判定: {names}");
            Vector3 before = body.transform.position;
            var flags = cc.enabled ? cc.Move(dir * 0.05f) : CollisionFlags.None;
            Debug.LogWarning($"[Demo]   試しに 5cm 動かす: 動いた {(body.transform.position - before).magnitude * 100f:F1}cm, 当たり {flags}, 接地 {cc.isGrounded}, CC 有効 {cc.enabled}, 入力 {controls.ScriptedCommands.move}, 向きのずれ {Vector3.SignedAngle(Flat(body.transform.forward), dir, Vector3.up):F0}°");
            if (Physics.Raycast(center, dir, out var ahead, 1.5f, ~0, QueryTriggerInteraction.Ignore))
                Debug.LogWarning($"[Demo]   前方 {ahead.distance:F2}m に {ahead.collider.name}（{ahead.collider.GetType().Name}, {ahead.collider.bounds.min:F2}〜{ahead.collider.bounds.max:F2}）");
        }
        commands.move = Vector2.zero;
        yield return null;
    }

    /// <summary>
    /// 目標へ 1 コマ歩く。歩く向きは顔の向きと別に決め（目標へまっすぐ）、顔はなめらかに進む向きへ向ける。
    /// 顔の向きを一気に合わせると、入力の 1 コマ遅れで行き過ぎて左右に揺れる。目標のすぐ近くでは向きを変えない（近いほど方向がぶれる）。
    /// </summary>
    IEnumerator WalkStep(Vector3 target)
    {
        Vector3 to = Flat(target - body.Feet);
        float distance = to.magnitude;
        Vector3 forward = Flat(body.transform.forward).normalized, right = Flat(body.transform.right).normalized;
        float yawError = distance > 1e-4f ? Vector3.SignedAngle(forward, to, Vector3.up) : 0f;
        if (distance > 0.3f)
        {
            float maxTurn = 120f * Time.deltaTime;
            commands.look.x = Mathf.Clamp(yawError * (1f - Mathf.Exp(-5f * Time.deltaTime)), -maxTurn, maxTurn);
        }
        commands.look.y = (CurrentPitch() - 8f) * 0.1f; // 少し下を見ながら歩く
        // 大きく向きが違う間は、ゆっくり歩きながら向き直る（後ろ向きに歩かない）
        float speed = Mathf.Abs(yawError) > 70f && distance > 0.3f ? 0.15f : Mathf.Clamp(distance / 0.6f, 0.25f, 1f);
        Vector3 dir = distance > 1e-4f ? to / distance : Vector3.zero;
        commands.move = new Vector2(Vector3.Dot(dir, right), Vector3.Dot(dir, forward)) * speed;
        yield return null;
    }

    IEnumerator LookAt(Vector3 target, float duration)
    {
        commands.move = Vector2.zero;
        for (float t = 0f; t < duration; t += Time.deltaTime) yield return LookStep(target, 6f);
    }

    IEnumerator LookStep(Vector3 target, float sharpness)
    {
        Vector3 dir = target - cam.transform.position;
        float yawError = Vector3.SignedAngle(Flat(body.transform.forward), Flat(dir), Vector3.up);
        float desiredPitch = -Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        float f = 1f - Mathf.Exp(-sharpness * Time.deltaTime);
        commands.look = new Vector2(yawError * f, (CurrentPitch() - Mathf.Clamp(desiredPitch, -80f, 80f)) * f);
        yield return null;
    }

    float CurrentPitch() => Mathf.DeltaAngle(0f, cam.transform.localEulerAngles.x);

    // ---- 録画 ----

    void SetupRecording()
    {
        frameRT = NewRT(width, height);
        insetRT = NewRT(InsetWidth, InsetHeight);
        captionRT = NewRT(width, CaptionHeight);
        labelRT = NewRT(150, 30);
        readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
        frameBytes = new byte[width * height * 4];

        // 外から見たカメラ（通りの向かい側から、プレイヤーを追う）
        Vector3 outward = bungee.jumpEdge.forward, along = bungee.returnPoint.forward;
        spectator = new GameObject("DemoSpectatorCamera").AddComponent<Camera>();
        spectator.transform.position = bungee.jumpEdge.position + outward * 7f - along * 4.5f + Vector3.down * 3f;
        spectator.fieldOfView = 42f;
        spectator.nearClipPlane = 0.1f;
        spectator.farClipPlane = 300f;
        spectator.cullingMask = ~((1 << CaptionLayer) | (1 << FirstPersonLayer));
        // 風の筋は 1 人称の映像だけに映す（外からの映像では雨のように見えて邪魔なので）
        var streaks = GameObject.Find("WindStreaks");
        if (streaks != null) streaks.layer = FirstPersonLayer;
        spectator.rect = new Rect(3f / InsetWidth, 3f / InsetHeight, 1f - 6f / InsetWidth, 1f - 6f / InsetHeight);
        spectator.enabled = false;
        spectator.transform.LookAt(body.Head);
        CreateAvatar();

        // 字幕用カメラ（文字だけを映す）
        var captionRoot = new GameObject("DemoCaption");
        captionRoot.transform.position = new Vector3(0f, 2000f, 0f);
        captionCamera = captionRoot.AddComponent<Camera>();
        captionCamera.orthographic = true;
        captionCamera.orthographicSize = 0.5f;
        captionCamera.nearClipPlane = 0.1f;
        captionCamera.farClipPlane = 10f;
        captionCamera.clearFlags = CameraClearFlags.SolidColor;
        captionCamera.backgroundColor = new Color(0.04f, 0.05f, 0.09f);
        captionCamera.cullingMask = 1 << CaptionLayer;
        captionCamera.enabled = false;
        var textGo = new GameObject("Text") { layer = CaptionLayer };
        textGo.transform.SetParent(captionRoot.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, 2f);
        captionText = textGo.AddComponent<TextMesh>();
        var sign = bungee.platformSign;
        captionText.font = sign.font;
        textGo.GetComponent<MeshRenderer>().sharedMaterial = sign.GetComponent<MeshRenderer>().sharedMaterial;
        captionText.fontSize = 64;
        captionText.characterSize = 0.55f * 10f / 64f;
        captionText.anchor = TextAnchor.MiddleCenter;
        captionText.alignment = TextAlignment.Center;
        captionText.color = Color.white;

        Caption("外からの視点");
        RenderCamera(captionCamera, labelRT);
        Caption("");

        string fullPath = VideoOnlyPath;
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        foreach (var source in cam.GetComponents<AudioSource>())
            if (source.loop && source.clip != null) loops.Add(new LoopTrack { source = source });
        if (bungee.ambientWind != null) loops.Add(new LoopTrack { source = bungee.ambientWind, spatialMin = bungee.ambientWind.minDistance });
        var info = new ProcessStartInfo(ffmpegPath,
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - " +
            $"-vf vflip -c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -movflags +faststart \"{fullPath}\"")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        ffmpeg = Process.Start(info);
        ffmpeg.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning($"[Demo] ffmpeg: {e.Data}"); };
        ffmpeg.BeginErrorReadLine();
        ffmpegInput = ffmpeg.StandardInput.BaseStream;
        Debug.Log($"[Demo] 録画開始: {fullPath}");
    }

    const string JumperModelPath = "Assets/Bungee/Models/Bungee_Jumper/Bungee_Jumper.fbx"; // Tripo で作った飛ぶ人（無ければ筒と球で作る）

    void CreateAvatar()
    {
        avatar = new GameObject("DemoAvatar").transform;
        var jumper = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(JumperModelPath);
        if (jumper != null)
        {
            // 身長 1.65m、足の真ん中を原点に、前（+Z）を体の向きに
            var model = Instantiate(jumper, avatar);
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
            Bounds b = AvatarBounds(model);
            model.transform.localScale *= 1.65f / Mathf.Max(b.size.y, 0.01f);
            b = AvatarBounds(model);
            model.transform.position += avatar.position - new Vector3(b.center.x, b.min.y, b.center.z);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AvatarLayer;
            return;
        }
        var shirt = Part(PrimitiveType.Capsule, avatar, new Vector3(0f, 1.05f, 0f), new Vector3(0.42f, 0.42f, 0.3f), new Color(0.2f, 0.45f, 0.95f));
        Part(PrimitiveType.Capsule, avatar, new Vector3(-0.11f, 0.42f, 0f), new Vector3(0.16f, 0.42f, 0.16f), new Color(0.15f, 0.15f, 0.2f));
        Part(PrimitiveType.Capsule, avatar, new Vector3(0.11f, 0.42f, 0f), new Vector3(0.16f, 0.42f, 0.16f), new Color(0.15f, 0.15f, 0.2f));
        Part(PrimitiveType.Sphere, avatar, new Vector3(0f, 1.62f, 0f), Vector3.one * 0.24f, new Color(1f, 0.82f, 0.68f));
        shirt.name = "Body";
    }

    static Bounds AvatarBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    GameObject Part(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.layer = AvatarLayer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    void Caption(string text)
    {
        if (captionText != null) captionText.text = text;
        if (!string.IsNullOrEmpty(text) && text != "外からの視点") Debug.Log($"[Demo] {Time.time:F1}s {text}");
    }

    void LateUpdate()
    {
        PushCommands();
        if (ffmpegInput == null || finished) return;

        // 外からのカメラは、クレーンのようにプレイヤーの高さへついていく
        avatar.SetPositionAndRotation(body.Feet, Quaternion.Euler(0f, body.transform.eulerAngles.y, 0f));
        avatar.localScale = Vector3.one * body.Scale;
        spectator.fieldOfView = 42f * Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(PlayerBody.MiniScale, 1f, body.Scale)); // ミニのときは寄って映す
        float follow = 1f - Mathf.Exp(-3f * Time.deltaTime);
        Vector3 spectatorPosition = spectator.transform.position;
        spectatorPosition.y = frames == 0 ? body.Feet.y + 2.5f : Mathf.Lerp(spectatorPosition.y, body.Feet.y + 2.5f, follow);
        spectator.transform.position = spectatorPosition;
        Quaternion look = Quaternion.LookRotation(body.Feet + Vector3.up * body.Scale - spectatorPosition);
        spectator.transform.rotation = frames == 0 ? look : Quaternion.Slerp(spectator.transform.rotation, look, 1f - Mathf.Exp(-4f * Time.deltaTime));

        // 1 人称 → 右上に外からの映像 → 下に字幕
        RenderCamera(cam, frameRT);
        Clear(insetRT, Color.white);
        RenderCamera(spectator, insetRT);
        RenderCamera(captionCamera, captionRT);
        int insetX = width - InsetWidth - InsetMargin, insetY = height - InsetHeight - InsetMargin;
        Graphics.CopyTexture(insetRT, 0, 0, 0, 0, InsetWidth, InsetHeight, frameRT, 0, 0, insetX, insetY);
        Graphics.CopyTexture(labelRT, 0, 0, 0, 0, labelRT.width, labelRT.height, frameRT, 0, 0, insetX + 3, insetY + InsetHeight - labelRT.height - 3);
        if (!string.IsNullOrEmpty(captionText.text))
            Graphics.CopyTexture(captionRT, 0, 0, 0, 0, width, CaptionHeight, frameRT, 0, 0, 0, 24);

        var previous = RenderTexture.active;
        RenderTexture.active = frameRT;
        readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
        RenderTexture.active = previous;
        readback.GetRawTextureData<byte>().CopyTo(frameBytes);
        ffmpegInput.Write(frameBytes, 0, frameBytes.Length);
        foreach (var loop in loops)
        {
            float volume = loop.source.enabled ? loop.source.volume : 0f; // バッチモードでは isPlaying が立たないので見ない
            if (loop.spatialMin > 0f) volume *= Rolloff(Vector3.Distance(cam.transform.position, loop.source.transform.position), loop.spatialMin);
            loop.volume.Add(volume);
            loop.pitch.Add(loop.source.pitch);
        }
        frames++;
    }

    static void RenderCamera(Camera camera, RenderTexture target)
    {
        camera.aspect = target.width * camera.rect.width / (target.height * camera.rect.height);
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        if (!RenderPipeline.SupportsRenderRequest(camera, request)) return;
        RenderPipeline.SubmitRenderRequest(camera, request);
    }

    static void Clear(RenderTexture target, Color color)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        GL.Clear(true, true, color);
        RenderTexture.active = previous;
    }

    static RenderTexture NewRT(int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        rt.Create();
        return rt;
    }

    string VideoOnlyPath => Path.ChangeExtension(Path.GetFullPath(outputPath), null) + "_video.mp4";
    string AudioPath => Path.ChangeExtension(Path.GetFullPath(outputPath), null) + "_audio.wav";

    /// <summary>記録した音を、同じ音のファイル（ffmpeg で読む）から重ねて WAV にする。</summary>
    void MixAudio()
    {
        int total = (int)((long)frames * AudioRate / fps);
        var mix = new float[total * 2];
        var cache = new Dictionary<AudioClip, float[]>();
        float[] Decode(AudioClip clip)
        {
            if (cache.TryGetValue(clip, out var cached)) return cached;
            string path = Path.GetFullPath(UnityEditor.AssetDatabase.GetAssetPath(clip));
            var decoder = Process.Start(new ProcessStartInfo(ffmpegPath, $"-v error -i \"{path}\" -f f32le -ac 2 -ar {AudioRate} -")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            using var bytes = new MemoryStream();
            decoder.StandardOutput.BaseStream.CopyTo(bytes);
            decoder.WaitForExit();
            var data = new float[bytes.Length / 4];
            System.Buffer.BlockCopy(bytes.GetBuffer(), 0, data, 0, data.Length * 4);
            return cache[clip] = data;
        }

        foreach (var shot in oneShots)
        {
            var data = Decode(shot.clip);
            int start = (int)((long)shot.frame * AudioRate / fps) * 2;
            for (int i = 0; i < data.Length && start + i < mix.Length; i++) mix[start + i] += data[i] * shot.volume;
        }
        foreach (var loop in loops)
        {
            var data = Decode(loop.source.clip);
            int length = data.Length / 2;
            if (length == 0 || loop.volume.Count == 0) continue;
            double phase = 0;
            for (int s = 0; s < total; s++)
            {
                double f = (double)s * fps / AudioRate;
                int f0 = Mathf.Min((int)f, loop.volume.Count - 1), f1 = Mathf.Min(f0 + 1, loop.volume.Count - 1);
                float k = (float)(f - (int)f);
                float volume = Mathf.Lerp(loop.volume[f0], loop.volume[f1], k);
                float pitch = Mathf.Lerp(loop.pitch[f0], loop.pitch[f1], k);
                int a = (int)phase % length, b = (a + 1) % length;
                float t = (float)(phase - System.Math.Floor(phase));
                mix[s * 2] += Mathf.Lerp(data[a * 2], data[b * 2], t) * volume;
                mix[s * 2 + 1] += Mathf.Lerp(data[a * 2 + 1], data[b * 2 + 1], t) * volume;
                phase += pitch;
            }
        }

        float peak = 0f;
        foreach (float v in mix) peak = Mathf.Max(peak, Mathf.Abs(v));
        float gain = peak > 0f ? 0.95f / peak : 1f; // 一番大きい所が 0.95 になるようにそろえる
        using var writer = new BinaryWriter(File.Create(AudioPath));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + mix.Length * 2);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(AudioRate);
        writer.Write(AudioRate * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(mix.Length * 2);
        foreach (float v in mix) writer.Write((short)(Mathf.Clamp(v * gain, -1f, 1f) * short.MaxValue));
        Debug.Log($"[Demo] 音: 鳴らした音 {oneShots.Count} 回, ループ {loops.Count} 本, 最大 {peak:F2}（{gain:F2} 倍にして書き出し）");
    }

    /// <summary>映像と音を 1 つの mp4 にまとめる。</summary>
    bool Mux()
    {
        string output = Path.GetFullPath(outputPath);
        var mux = Process.Start(new ProcessStartInfo(ffmpegPath,
            $"-y -loglevel error -i \"{VideoOnlyPath}\" -i \"{AudioPath}\" -c:v copy -c:a aac -b:a 192k -shortest -movflags +faststart \"{output}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        mux.WaitForExit(120000);
        if (mux.ExitCode != 0) return false;
        File.Delete(VideoOnlyPath);
        File.Delete(AudioPath);
        return true;
    }

    void Finish(bool ok, string reason)
    {
        if (finished) return;
        finished = true;
        if (ffmpegInput != null)
        {
            ffmpegInput.Close();
            ffmpeg.WaitForExit(120000);
            Debug.Log($"[Demo] 録画終了: {frames} コマ（{frames / (float)fps:F1} 秒）, ffmpeg 終了コード {ffmpeg.ExitCode}");
            ok &= ffmpeg.ExitCode == 0;
            MixAudio();
            ok &= Mux();
        }
        Debug.Log($"[Demo] {(ok ? "成功" : "失敗")}: {reason} / バンジーの状態 {bungee?.CurrentPhase}, 弾んだ回数 {bungee?.Bounces}");
        Time.captureFramerate = 0;
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(ok ? 0 : 1);
        else UnityEditor.EditorApplication.ExitPlaymode();
    }
}
#endif
