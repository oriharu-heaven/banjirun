using UnityEngine;

/// <summary>
/// 画面で遊ぶときの表示（照準・触れられる物のガイド・バンジー着地時のお祝い一枚絵）。
/// 常時の操作説明は出さず、ドア・窓・ハーネス・エレベーター扉などに近づいたり見たりしたときだけ直感的にガイドを出す。
/// </summary>
[RequireComponent(typeof(FirstPersonControls))]
public class FlatHUD : MonoBehaviour
{
    public Font font;

    FirstPersonControls controls;
    PlayerBody body;
    TimeAttackManager timeAttack;
    GUIStyle hintStyle, centerStyle, clearTitleStyle, subStyle;
    float blockedTimer;

    void Awake()
    {
        controls = GetComponent<FirstPersonControls>();
        body = GetComponent<PlayerBody>();
        body.ResizeBlocked += () => blockedTimer = 2f;
    }

    void Start()
    {
        timeAttack = FindFirstObjectByType<TimeAttackManager>();
    }

    void Update()
    {
        blockedTimer -= Time.deltaTime;
    }

    void OnGUI()
    {
        if (controls.Scripted) return;
        if (hintStyle == null) CreateStyles();

        float cx = Screen.width / 2f, cy = Screen.height / 2f;

        // 照準（触れられる物を見ているときは黄色い二重丸に変わる）
        bool focused = controls.Focused != null;
        GUI.color = focused ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 1f, 1f, 0.75f);
        GUI.Label(new Rect(cx - 20, cy - 20, 40, 40), focused ? "◎" : "・", centerStyle);
        GUI.color = Color.white;

        // 触れられるときだけ直感的なガイドを表示
        if (focused)
        {
            GUI.Box(new Rect(cx - 220, Screen.height * 0.62f, 440, 46), $"[E / クリック]  {controls.Focused.UseHint}", hintStyle);
        }
        else if (timeAttack != null && timeAttack.IsElevatorWaiting)
        {
            GUI.Box(new Rect(cx - 220, Screen.height * 0.62f, 440, 46), "[E / クリック]  エレベーターを止めて扉を開ける", hintStyle);
        }

        if (body.IsMini)
            GUI.Box(new Rect(Screen.width - 196, 16, 180, 40), "ミニキャラ", hintStyle);
        if (blockedTimer > 0f)
            GUI.Box(new Rect(cx - 230, Screen.height * 0.62f - 54, 460, 44), body.Locked ? "飛行中は大きさを変えられない" : "ここでは大きくなれない（上がふさがっている）", hintStyle);
        if (!controls.CursorLocked)
            GUI.Box(new Rect(cx - 200, Screen.height - 64, 400, 44), "画面をクリックしてスタート", hintStyle);

        DrawBungeeLandingCard(cx, cy);
    }

    void DrawBungeeLandingCard(float cx, float cy)
    {
        if (timeAttack == null || !timeAttack.ShowBirthdayCard) return;

        bool hasCard = timeAttack.birthdayCard != null;
        float panelW = hasCard ? 560f : 480f;
        float cardH = hasCard ? 290f : 0f;
        float panelH = hasCard ? 420f : 140f;
        float top = cy - panelH / 2f - 20f;

        GUI.Box(new Rect(cx - panelW / 2f, top, panelW, panelH), "", hintStyle);
        GUI.Label(new Rect(cx - panelW / 2f + 16f, top + 16f, panelW - 32f, 44f), "ばんじー誕生日おめでとう！", clearTitleStyle);

        if (hasCard)
            GUI.DrawTexture(new Rect(cx - (panelW - 48f) / 2f, top + 70f, panelW - 48f, cardH), timeAttack.birthdayCard, ScaleMode.ScaleToFit);

        GUI.Label(new Rect(cx - panelW / 2f + 16f, top + panelH - 46f, panelW - 32f, 34f),
            "足元の光る輪に入ると上へ戻れます", subStyle);
    }

    void CreateStyles()
    {
        var bg = new Texture2D(1, 1);
        bg.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.09f, 0.82f));
        bg.Apply();
        hintStyle = new GUIStyle(GUI.skin.box) { font = font, fontSize = 22, alignment = TextAnchor.MiddleCenter };
        hintStyle.normal.background = bg;
        hintStyle.normal.textColor = Color.white;

        centerStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 26, alignment = TextAnchor.MiddleCenter };

        clearTitleStyle = new GUIStyle(centerStyle) { fontSize = 32, fontStyle = FontStyle.Bold };
        clearTitleStyle.normal.textColor = new Color(1f, 0.45f, 0.8f);

        subStyle = new GUIStyle(centerStyle) { fontSize = 18 };
        subStyle.normal.textColor = new Color(0.8f, 0.95f, 1f);
    }
}
