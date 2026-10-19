using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// メニュー「BanjiRun/Bungee Showcase」: 窓の外のバンジー（BungeeBuilder が作る物）を決まった 10 か所から撮り、
/// Builds/Showcase/&lt;名前&gt;/ に PNG で保存する。見た目を直す前と後を同じ場所で比べる用（画面版のシーン・Mobile 画質）。
/// コマンドから: Unity -batchmode -projectPath &lt;project&gt; -executeMethod BungeeShowcase.RunBatch -showcaseLabel before
/// </summary>
public static class BungeeShowcase
{
    const string OutFolder = "Builds/Showcase";
    const int Width = 1280, Height = 720;

    [MenuItem("BanjiRun/Bungee Showcase")]
    public static void RunFromMenu()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Run("latest");
    }

    public static void RunBatch()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-showcaseLabel");
        Run(i >= 0 && i + 1 < args.Length ? args[i + 1] : "latest");
        EditorApplication.Exit(0);
    }

    static void Run(string label)
    {
        EditorSceneManager.OpenScene(FlatSceneBuilder.ScenePath);
        var bungee = Object.FindFirstObjectByType<BungeeJump>();
        if (bungee == null)
        {
            Debug.LogError("[Showcase] BungeeJump が見つかりません");
            return;
        }
        string dir = Path.Combine(OutFolder, label);
        Directory.CreateDirectory(dir);

        int qualityBefore = QualitySettings.GetQualityLevel();
        int mobile = System.Array.IndexOf(QualitySettings.names, "Mobile");
        if (mobile >= 0) QualitySettings.SetQualityLevel(mobile, true);

        var camera = new GameObject("ShowcaseCamera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
        camera.nearClipPlane = 0.02f;
        camera.farClipPlane = 400f;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        camera.targetTexture = target;
        if (bungee.countdownSign != null) bungee.countdownSign.text = "3";

        Vector3 outward = bungee.jumpEdge.forward, along = bungee.returnPoint.forward;
        Vector3 tip = bungee.jumpEdge.position;
        Vector3 up = Vector3.up;
        float deck = bungee.platformTop;
        Vector3 balcony = bungee.returnPoint.position;
        Vector3 cushionBase = bungee.cushion.position;
        Vector3 cushionTop = new Vector3(cushionBase.x, bungee.cushionTop, cushionBase.z);
        Vector3 onDeck = Flat(balcony + along * 1.95f + outward * 0.6f) + up * deck;
        var window = GameObject.Find("EastWin_Sash_02_Glass");
        Vector3 windowCenter = window != null ? window.GetComponent<Renderer>().bounds.center : balcony - outward;

        Shot(camera, target, dir, "01_air_platform", tip + outward * 6f - along * 3.5f + up * 0.8f, tip - outward * 1.5f + up * 0.6f, 55f);
        Shot(camera, target, dir, "02_street_up", cushionBase + outward * 9f - along * 7f + up * 1.6f, tip + up * 1f, 50f);
        Shot(camera, target, dir, "03_deck_to_plank", onDeck + up * 1.6f - outward * 0.3f, tip + up * 0.9f, 70f);
        Shot(camera, target, dir, "04_plank_down", tip - outward * 0.3f + up * 1.6f, cushionTop, 70f);
        Shot(camera, target, dir, "05_stairs", balcony + up * 1.6f - along * 0.6f, Flat(balcony + along * 1.4f) + up * (deck * 0.6f), 70f);
        Shot(camera, target, dir, "06_cushion_street", cushionBase + outward * 6f + along * 3f + up * 1.7f, cushionTop, 60f);
        Shot(camera, target, dir, "07_on_cushion", cushionTop + up * 1.6f + along * 1.2f, bungee.returnPad.position, 70f);
        Shot(camera, target, dir, "08_countdown", tip - outward * 0.25f + up * 1.6f, bungee.countdownSign.transform.position, 70f);
        Shot(camera, target, dir, "09_window_sign", windowCenter - outward * 2.6f + Vector3.up * (0.2f + 1.6f - windowCenter.y), windowCenter + along * 0.6f, 70f);
        Shot(camera, target, dir, "10_anchor_up", tip - outward * 0.2f + up * 1.6f, bungee.anchor.position + outward * 0.3f, 70f);

        QualitySettings.SetQualityLevel(qualityBefore, true);
        Object.DestroyImmediate(camera.gameObject);
        target.Release();
        Debug.Log($"[Showcase] 保存: {Path.GetFullPath(dir)}");
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static void Shot(Camera camera, RenderTexture target, string dir, string name, Vector3 position, Vector3 lookAt, float fov)
    {
        camera.fieldOfView = fov;
        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        RenderTexture.active = previous;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }
}
