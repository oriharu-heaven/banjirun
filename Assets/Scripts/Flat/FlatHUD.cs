using UnityEngine;

/// <summary>
/// 画面で遊ぶときの表示（照準・使える物の案内・操作説明・タイムアタックの経過時間とクリア画面）。
/// VR では世界の中の案内板（LedSign）だけを使い、これは出さない。
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
        "E・左クリック / X　使う（窓・ハーネス・リトライボタン）\n" +
        "R / Select　タイムアタックを最初からリトライ\n" +
        "M / Y　ミニキャラ ⇔ 等身大（ミニは段をよじ登れる）\n" +
        "Esc　カーソルを出す　　H　この説明を消す";

    FirstPersonControls controls;
    PlayerBody body;
    TimeAttackManager timeAttack;
    GUIStyle hintStyle, helpStyle, centerStyle, timerStyle, countStyle, clearTitleStyle;
    bool showHelp = true;
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
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.hKey.wasPressedThisFrame) showHelp = !showHelp;
        blockedTimer -= Time.deltaTime;
    }

    void OnGUI()
    {
        if (controls.Scripted) return;
        if (hintStyle == null) CreateStyles();

        float cx = Screen.width / 2f, cy = Screen.height / 2f;

        // 照準
        bool focused = controls.Focused != null;
        GUI.color = focused ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 1f, 1f, 0.8f);
        GUI.Label(new Rect(cx - 20, cy - 20, 40, 40), focused ? "◎" : "・", centerStyle);
        GUI.color = Color.white;

        if (focused)
            GUI.Box(new Rect(cx - 190, Screen.height * 0.6f, 380, 44), $"E / クリック：{controls.Focused.UseHint}", hintStyle);
        if (showHelp)
            GUI.Box(new Rect(16, 16, 540, 198), Help, helpStyle);
        if (body.IsMini)
            GUI.Box(new Rect(Screen.width - 196, 16, 180, 40), "ミニキャラ", hintStyle);
        if (blockedTimer > 0f)
            GUI.Box(new Rect(cx - 230, Screen.height * 0.6f - 54, 460, 44), body.Locked ? "カウントダウン中・飛行中は大きさを変えられない" : "ここでは大きくなれない（上がふさがっている）", hintStyle);
        if (!controls.CursorLocked)
            GUI.Box(new Rect(cx - 200, Screen.height - 64, 400, 44), "クリックで操作を始める", hintStyle);

        DrawTimeAttack(cx, cy);
    }

    void DrawTimeAttack(float cx, float cy)
    {
        if (timeAttack == null) return;

        // 画面上部の経過時間表示
        string topText = timeAttack.CurrentState switch
        {
            TimeAttackManager.State.Countdown => "教室タイムアタック ― まもなくスタート",
            TimeAttackManager.State.Running => $"TIME  {TimeAttackManager.FormatTime(timeAttack.ElapsedTime)}" +
                (timeAttack.BestTime > 0f ? $"   (BEST {TimeAttackManager.FormatTime(timeAttack.BestTime)})" : ""),
            TimeAttackManager.State.Cleared => $"CLEAR!  {TimeAttackManager.FormatTime(timeAttack.ClearTime)}" +
                (timeAttack.BestTime > 0f ? $"   (BEST {TimeAttackManager.FormatTime(timeAttack.BestTime)})" : ""),
            _ => "",
        };
        GUI.Box(new Rect(cx - 220, 16, 440, 48), topText, timerStyle);

        // カウントダウン「3, 2, 1, スタート！」
        if (timeAttack.CurrentState == TimeAttackManager.State.Countdown)
        {
            GUI.Box(new Rect(cx - 150, cy - 110, 300, 96), timeAttack.CountdownNumber.ToString(), countStyle);
        }
        else if (timeAttack.ShowStartBanner)
        {
            GUI.Box(new Rect(cx - 190, cy - 110, 380, 88), "スタート！", countStyle);
        }

        // クリア時の演出（タイム表示 + 「ばんじー誕生日おめでとう！」 + 一枚絵があれば表示）
        if (timeAttack.CurrentState == TimeAttackManager.State.Cleared)
        {
            bool hasCard = timeAttack.birthdayCard != null;
            float panelW = hasCard ? 560f : 480f;
            float cardH = hasCard ? 280f : 0f;
            float panelH = hasCard ? 440f : 168f;
            float top = cy - panelH / 2f - 20f;

            GUI.Box(new Rect(cx - panelW / 2f, top, panelW, panelH), "", hintStyle);
            GUI.Label(new Rect(cx - panelW / 2f + 16f, top + 14f, panelW - 32f, 44f), "ばんじー誕生日おめでとう！", clearTitleStyle);
            GUI.Label(new Rect(cx - panelW / 2f + 16f, top + 62f, panelW - 32f, 36f),
                $"クリアタイム：{TimeAttackManager.FormatTime(timeAttack.ClearTime)}", timerStyle);

            if (hasCard)
                GUI.DrawTexture(new Rect(cx - (panelW - 48f) / 2f, top + 106f, panelW - 48f, cardH), timeAttack.birthdayCard, ScaleMode.ScaleToFit);

            GUI.Label(new Rect(cx - panelW / 2f + 16f, top + panelH - 46f, panelW - 32f, 34f),
                "R キー / リトライボタンでスタートへ戻る", hintStyle);
        }
    }

    void CreateStyles()
    {
        var bg = new Texture2D(1, 1);
        bg.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.09f, 0.78f));
        bg.Apply();
        hintStyle = new GUIStyle(GUI.skin.box) { font = font, fontSize = 22, alignment = TextAnchor.MiddleCenter };
        hintStyle.normal.background = bg;
        hintStyle.normal.textColor = Color.white;

        helpStyle = new GUIStyle(hintStyle) { fontSize = 16, alignment = TextAnchor.UpperLeft, padding = new RectOffset(14, 14, 12, 12) };
        centerStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 26, alignment = TextAnchor.MiddleCenter };

        timerStyle = new GUIStyle(hintStyle) { fontSize = 24, fontStyle = FontStyle.Bold };
        timerStyle.normal.textColor = new Color(0.3f, 0.95f, 1f);

        countStyle = new GUIStyle(hintStyle) { fontSize = 52, fontStyle = FontStyle.Bold };
        countStyle.normal.textColor = new Color(1f, 0.92f, 0.25f);

        clearTitleStyle = new GUIStyle(centerStyle) { fontSize = 32, fontStyle = FontStyle.Bold };
        clearTitleStyle.normal.textColor = new Color(1f, 0.45f, 0.8f);
    }
}
