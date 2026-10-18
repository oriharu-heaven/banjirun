using UnityEngine;

/// <summary>
/// 画面で遊ぶときの表示（照準・使える物の案内・操作説明）。VR では世界の中の案内板だけを使い、これは出さない。
/// H キーで操作説明を出したり消したりする。
/// </summary>
[RequireComponent(typeof(FirstPersonControls))]
public class FlatHUD : MonoBehaviour
{
    public Font font;

    const string Help =
        "WASD / 左スティック　移動（Shift・スティック押し込みで走る）\n" +
        "マウス / 右スティック　見回す\n" +
        "Space / A　ジャンプ\n" +
        "E・左クリック / X　使う（窓・ハーネス）\n" +
        "M / Y　ミニキャラ ⇔ 等身大（ミニは段をよじ登れる）\n" +
        "Esc　カーソルを出す　　H　この説明を消す";

    FirstPersonControls controls;
    PlayerBody body;
    GUIStyle hintStyle, helpStyle, centerStyle;
    bool showHelp = true;
    float blockedTimer;

    void Awake()
    {
        controls = GetComponent<FirstPersonControls>();
        body = GetComponent<PlayerBody>();
        body.ResizeBlocked += () => blockedTimer = 2f;
    }

    void Update()
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.hKey.wasPressedThisFrame) showHelp = !showHelp;
        blockedTimer -= Time.deltaTime;
    }

    void OnGUI()
    {
        if (controls.Scripted) return;
        if (hintStyle == null) CreateStyles();

        // 照準
        float cx = Screen.width / 2f, cy = Screen.height / 2f;
        bool focused = controls.Focused != null;
        GUI.color = focused ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 1f, 1f, 0.8f);
        GUI.Label(new Rect(cx - 20, cy - 20, 40, 40), focused ? "◎" : "・", centerStyle);
        GUI.color = Color.white;

        if (focused)
            GUI.Box(new Rect(cx - 170, Screen.height * 0.6f, 340, 44), $"E / クリック：{controls.Focused.UseHint}", hintStyle);
        if (showHelp)
            GUI.Box(new Rect(16, 16, 540, 172), Help, helpStyle);
        if (body.IsMini)
            GUI.Box(new Rect(Screen.width - 196, 16, 180, 40), "ミニキャラ", hintStyle);
        if (blockedTimer > 0f)
            GUI.Box(new Rect(cx - 230, Screen.height * 0.6f - 54, 460, 44), body.Locked ? "飛んでいる間は大きさを変えられない" : "ここでは大きくなれない（上がふさがっている）", hintStyle);
        if (!controls.CursorLocked)
            GUI.Box(new Rect(cx - 200, cy + 40, 400, 44), "クリックで操作を始める", hintStyle);
    }

    void CreateStyles()
    {
        var bg = new Texture2D(1, 1);
        bg.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.09f, 0.75f));
        bg.Apply();
        hintStyle = new GUIStyle(GUI.skin.box) { font = font, fontSize = 22, alignment = TextAnchor.MiddleCenter };
        hintStyle.normal.background = bg;
        hintStyle.normal.textColor = Color.white;
        helpStyle = new GUIStyle(hintStyle) { fontSize = 17, alignment = TextAnchor.UpperLeft, padding = new RectOffset(14, 14, 12, 12) };
        centerStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 26, alignment = TextAnchor.MiddleCenter };
    }
}
