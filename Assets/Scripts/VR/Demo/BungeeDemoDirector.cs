#if UNITY_EDITOR
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

/// <summary>
/// エディター専用。VR シーンで「窓を開ける → バルコニー → ハーネス → バンジー → クッションに着地 → 戻る」を自動で操作し、
/// 1 人称の映像（右上に外から見た様子、下に字幕）を mp4 にする。ffmpeg が要る。
/// BungeeDemoRecorder（メニュー「BanjiRun/VR/6. Record Bungee Demo」）が再生前にシーンへ置く。
/// 操作は VRDesktopControls を通すので、移動・当たり判定・ドア・ハーネス・バンジーは実際のゲームと同じ動き。
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

    const int AvatarLayer = 29;   // 外からのカメラにだけ映すプレイヤーの体
    const int CaptionLayer = 28;  // 字幕用カメラにだけ映す文字
    const int CaptionHeight = 64;
    const int InsetWidth = 448, InsetHeight = 252, InsetMargin = 16;

    VRPlayerBody body;
    VRDesktopControls controls;
    BungeeJump bungee;
    Camera cam, spectator, captionCamera;
    TextMesh captionText;
    Transform avatar, avatarHarness;
    RenderTexture frameRT, insetRT, captionRT, labelRT;
    Texture2D readback;
    byte[] frameBytes;
    Process ffmpeg;
    Stream ffmpegInput;
    VRDesktopControls.Commands commands;
    int frames;
    bool finished;

    IEnumerator Start()
    {
        body = FindFirstObjectByType<VRPlayerBody>();
        controls = FindFirstObjectByType<VRDesktopControls>();
        bungee = FindFirstObjectByType<BungeeJump>();
        if (body == null || controls == null || bungee == null)
        {
            Finish(false, "VRPlayerBody / VRDesktopControls / BungeeJump が見つかりません");
            yield break;
        }
        Time.captureFramerate = fps;
        Application.runInBackground = true;
        yield return null; // 他の Start（VRDesktopControls など）を先に済ませる
        controls.Scripted = true;
        cam = body.Camera;
        cam.fieldOfView = 72f;
        cam.cullingMask &= ~((1 << AvatarLayer) | (1 << CaptionLayer));
        SetupRecording();
        yield return Run();
        Finish(bungee.CurrentPhase == BungeeJump.Phase.Idle, "完了");
    }

    // ---- 台本 ----

    IEnumerator Run()
    {
        Vector3 outward = bungee.jumpEdge.forward;
        Vector3 along = bungee.returnPoint.forward;
        Transform glass = GameObject.Find("EastWin_Sash_02_Glass")?.transform;
        Vector3 glassCenter = glass != null ? glass.GetComponent<Renderer>().bounds.center : bungee.returnPoint.position - outward;
        Vector3 floor = new Vector3(0f, body.Feet.y, 0f);
        // 机の間の通路（窓の中央より少し階段側）を通って窓へ
        Vector3 windowInside = Flat(glassCenter - outward * 0.75f + along * 0.1f) + floor;
        Vector3 balcony = bungee.returnPoint.position;
        Vector3 deck = Vector3.up * bungee.platformTop;

        // ホールの中、窓から 4 m ほどの所から
        body.TeleportFeetTo(windowInside - outward * 3.6f, Quaternion.LookRotation(outward).eulerAngles.y);
        Caption("BanjiRun VR ― 窓の外でバンジージャンプ");
        yield return LookAt(glassCenter + Vector3.up * 0.4f, 1.5f);

        Caption("① 窓まで歩いて、窓を開ける");
        yield return WalkTo(windowInside);
        yield return LookAt(glassCenter, 0.7f);
        yield return Use();
        yield return Wait(1.2f);

        Caption("② バルコニーへ出て、階段でジャンプ台へ");
        yield return WalkTo(balcony);
        yield return WalkTo(Flat(balcony + along * 0.6f) + Vector3.up * balcony.y);
        yield return WalkTo(Flat(balcony + along * 1.95f) + deck);
        yield return WalkTo(Flat(balcony + along * 1.95f + outward * 0.6f) + deck);

        Caption("③ ハーネスをつかんで、飛ぶ準備");
        yield return LookAt(bungee.harness.transform.position, 0.9f);
        yield return Use();
        yield return Wait(0.8f);

        Caption("④ 台の先に立つと、カウントダウン");
        Vector3 edge = bungee.jumpEdge.position - outward * 0.3f;
        yield return WalkTo(Flat(edge) + deck);
        yield return LookAt(bungee.countdownSign.transform.position, 1.0f);
        yield return Wait(2.0f);
        yield return LookAt(bungee.cushion.position, 0.9f);

        Caption("⑤ バンジー！");
        float t = 0f;
        while (bungee.CurrentPhase == BungeeJump.Phase.Harnessed && t < 3f)
        {
            commands.move = Vector2.up;
            t += Time.deltaTime;
            yield return null;
        }
        commands = default;
        // 落ちる間は下のクッションを見て、弾んだら少し上を見る
        while (bungee.CurrentPhase == BungeeJump.Phase.Falling)
        {
            Vector3 target = bungee.Bounces == 0 ? bungee.cushion.position : bungee.cushion.position + outward * 6f + Vector3.up * 10f;
            yield return LookStep(target, 3f);
        }

        Caption("⑥ 何度か弾んだら、ゆっくり降ろしてもらう");
        while (bungee.CurrentPhase == BungeeJump.Phase.Lowering)
            yield return LookStep(bungee.cushion.position, 2f);

        Caption("⑦ クッションに着地！");
        yield return LookAt(bungee.returnPad.position + outward * 3f, 1.2f);
        yield return Wait(0.8f);

        Caption("⑧ 光る輪に入ると、バルコニーへ戻る");
        yield return LookAt(bungee.returnPad.position, 0.8f);
        t = 0f;
        while (bungee.CurrentPhase == BungeeJump.Phase.Landed && t < 6f)
        {
            yield return WalkStep(bungee.returnPad.position);
            t += Time.deltaTime;
        }
        commands = default;

        Caption("おかえり！ もう一回飛べる");
        yield return LookAt(Flat(balcony + along * 3f + outward * 2f) + deck + Vector3.up * 1.2f, 1.5f);
        yield return Wait(1.5f);
    }

    // ---- 操作の小道具（VRDesktopControls に入力を渡す） ----

    void Update()
    {
        if (controls == null) return;
        controls.ScriptedCommands = commands;
        commands.use = false;
        commands.jump = false;
        commands.look = Vector2.zero;
        if (Time.time > timeLimit && !finished) Finish(false, "時間切れ");
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
    }

    IEnumerator Use()
    {
        commands.use = true;
        yield return null;
        yield return null;
    }

    IEnumerator WalkTo(Vector3 target, float timeout = 12f)
    {
        for (float t = 0f; t < timeout; t += Time.deltaTime)
        {
            if (Flat(target - body.Feet).magnitude < 0.12f) break;
            yield return WalkStep(target);
        }
        commands.move = Vector2.zero;
        yield return null;
    }

    IEnumerator WalkStep(Vector3 target)
    {
        Vector3 to = Flat(target - body.Feet);
        float yawError = Vector3.SignedAngle(Flat(body.transform.forward), to, Vector3.up);
        float maxTurn = 140f * Time.deltaTime;
        commands.look.x = Mathf.Clamp(yawError, -maxTurn, maxTurn);
        commands.look.y = (CurrentPitch() - 8f) * 0.1f; // 少し下を見ながら歩く
        float speed = Mathf.Clamp01(to.magnitude / 0.6f);
        commands.move = new Vector2(0f, Mathf.Abs(yawError) < 40f ? Mathf.Max(speed, 0.25f) : 0.1f);
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
        spectator.cullingMask = ~(1 << CaptionLayer);
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

        string fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
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

    void CreateAvatar()
    {
        avatar = new GameObject("DemoAvatar").transform;
        var shirt = Part(PrimitiveType.Capsule, avatar, new Vector3(0f, 1.05f, 0f), new Vector3(0.42f, 0.42f, 0.3f), new Color(0.2f, 0.45f, 0.95f));
        Part(PrimitiveType.Capsule, avatar, new Vector3(-0.11f, 0.42f, 0f), new Vector3(0.16f, 0.42f, 0.16f), new Color(0.15f, 0.15f, 0.2f));
        Part(PrimitiveType.Capsule, avatar, new Vector3(0.11f, 0.42f, 0f), new Vector3(0.16f, 0.42f, 0.16f), new Color(0.15f, 0.15f, 0.2f));
        Part(PrimitiveType.Sphere, avatar, new Vector3(0f, 1.62f, 0f), Vector3.one * 0.24f, new Color(1f, 0.82f, 0.68f));
        avatarHarness = Part(PrimitiveType.Cube, avatar, new Vector3(0f, 0.95f, 0f), new Vector3(0.46f, 0.1f, 0.34f), new Color(1f, 0.45f, 0.05f)).transform;
        shirt.name = "Body";
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
        if (ffmpegInput == null || finished) return;

        // 外からのカメラは、クレーンのようにプレイヤーの高さへついていく
        avatar.SetPositionAndRotation(body.Feet, Quaternion.Euler(0f, body.transform.eulerAngles.y, 0f));
        avatarHarness.gameObject.SetActive(bungee.CurrentPhase is BungeeJump.Phase.Harnessed or BungeeJump.Phase.Falling or BungeeJump.Phase.Lowering);
        float follow = 1f - Mathf.Exp(-3f * Time.deltaTime);
        Vector3 spectatorPosition = spectator.transform.position;
        spectatorPosition.y = frames == 0 ? body.Feet.y + 2.5f : Mathf.Lerp(spectatorPosition.y, body.Feet.y + 2.5f, follow);
        spectator.transform.position = spectatorPosition;
        Quaternion look = Quaternion.LookRotation(body.Feet + Vector3.up * 1f - spectatorPosition);
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
        }
        Debug.Log($"[Demo] {(ok ? "成功" : "失敗")}: {reason} / バンジーの状態 {bungee?.CurrentPhase}, 弾んだ回数 {bungee?.Bounces}");
        Time.captureFramerate = 0;
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(ok ? 0 : 1);
        else UnityEditor.EditorApplication.ExitPlaymode();
    }
}
#endif
