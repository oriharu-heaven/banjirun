using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 教室タイムアタックの進行管理（VR 版・画面版共通、XR 非依存）。
/// 1. スタート: 入口ドア（Door_Entrance）付近にプレイヤーを配置し、「3, 2, 1, スタート！」のカウントダウンで計測開始
/// 2. ゴール: 部屋の奥（ステージ上）の「ばんじー」オブジェクトに触れたら計測停止
/// 3. 表示: 画面版は FlatHUD、VR 版はワールド空間の LedSign（スタート頭上・ゴール頭上）で経過時間を表示
/// 4. クリア時: タイム表示 + 「ばんじー誕生日おめでとう！」 + 一枚絵（Assets/Bungee/Textures/BirthdayCard.png があれば表示、なければスキップ）
/// 5. リトライ: R キー / ゲームパッド Select / VR 左手 X・メニューボタン / ワールド内のリトライボタン（IUsable）でスタートへ戻す
/// </summary>
public class TimeAttackManager : MonoBehaviour
{
    public enum State { Countdown, Running, Cleared }

    [Header("場所・目印")]
    public Transform startPoint;
    public Transform goalPoint;
    public Transform goalFigure;
    public float goalRadius = 1.25f;
    public float goalHeightTolerance = 1.5f;
    public MonoBehaviour entranceDoor;

    [Header("ワールド空間の表示（LedSign）")]
    public TextMesh startSign;
    public TextMesh goalSign;

    [Header("クリア時の一枚絵（Assets/Bungee/Textures/BirthdayCard.png があれば表示）")]
    public Texture2D birthdayCard;
    public GameObject birthdayCardBoard;
    public Renderer birthdayCardRenderer;

    [Header("音")]
    public AudioClip[] voiceCount; // 3, 2, 1, スタート
    public AudioClip cheerSound;
    public AudioClip niceVoice;
    public AudioClip retrySound;

    public State CurrentState { get; private set; }
    public float CountdownRemaining => Mathf.Max(0f, countdownTimer);
    public int CountdownNumber => Mathf.Clamp(Mathf.CeilToInt(countdownTimer), 1, 3);
    public float ElapsedTime { get; private set; }
    public float ClearTime { get; private set; }
    public float BestTime { get; private set; }
    public bool ShowStartBanner => CurrentState == State.Running && startBannerTimer > 0f;

    PlayerBody body;
    FirstPersonControls controls;
    BungeeJump bungee;
    AudioSource sfx;
    LedSign startLed, goalLed;
    InputAction vrRetryAction;
    Vector3 goalFigureRestPos;
    float countdownTimer;
    int lastCountShown = -1;
    float startBannerTimer;
    bool lockedByCountdown;

    static readonly Color[] RunningColors = { new Color(0.2f, 0.9f, 1f), new Color(0.25f, 1f, 0.35f) };
    static readonly Color[] ClearColors =
    {
        new Color(1f, 0.3f, 0.75f),
        new Color(1f, 0.92f, 0.2f),
        new Color(0.2f, 0.9f, 1f),
        new Color(0.25f, 1f, 0.35f),
    };

    void Awake()
    {
        vrRetryAction = new InputAction("TimeAttackRetry", InputActionType.Button);
        vrRetryAction.AddBinding("<XRController>{LeftHand}/primaryButton");
        vrRetryAction.AddBinding("<XRController>{LeftHand}/menu");
    }

    void OnEnable() => vrRetryAction?.Enable();
    void OnDisable()
    {
        vrRetryAction?.Disable();
        ReleaseCountdownLock();
    }
    void OnDestroy() => vrRetryAction?.Dispose();

    void Start()
    {
        body = FindFirstObjectByType<PlayerBody>();
        controls = FindFirstObjectByType<FirstPersonControls>();
        bungee = FindFirstObjectByType<BungeeJump>();
        if (startSign != null) startLed = startSign.GetComponentInParent<LedSign>();
        if (goalSign != null) goalLed = goalSign.GetComponentInParent<LedSign>();
        if (goalFigure != null) goalFigureRestPos = goalFigure.localPosition;

        if (body != null && body.Camera != null)
        {
            sfx = body.Camera.gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
        }

#if UNITY_EDITOR
        if (birthdayCard == null)
            birthdayCard = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Bungee/Textures/BirthdayCard.png");
#endif
        ApplyBirthdayCardTexture();
        BeginRun(teleportToStart: true);
    }

    void Update()
    {
        if (body == null) return;

        // バンジーの自動デモ録画中（BungeeDemoDirector）は干渉しないようロックを解除して待機する
        if (controls != null && controls.Scripted)
        {
            ReleaseCountdownLock();
            return;
        }

        if (RetryPressed() && !IsBungeeBusy())
        {
            Retry();
            return;
        }

        float dt = Time.deltaTime;
        switch (CurrentState)
        {
            case State.Countdown:
                UpdateCountdown(dt);
                break;
            case State.Running:
                UpdateRunning(dt);
                break;
            case State.Cleared:
                AnimateGoalCelebration(dt);
                break;
        }
    }

    public void Retry()
    {
        if (body == null || IsBungeeBusy()) return;
        Play(retrySound, 0.8f);
        BeginRun(teleportToStart: true);
    }

    void BeginRun(bool teleportToStart)
    {
        ReleaseCountdownLock();
        if (entranceDoor is IUsable doorUsable && doorUsable.UseHint == "開ける")
            doorUsable.Use(startPoint != null ? startPoint.position - startPoint.forward : transform.position);

        if (teleportToStart && startPoint != null && body != null)
            body.TeleportFeetTo(startPoint.position, startPoint.eulerAngles.y);

        if (body != null && !body.Locked)
        {
            body.BeginExternalMotion();
            lockedByCountdown = true;
        }

        ElapsedTime = 0f;
        countdownTimer = 3f;
        lastCountShown = -1;
        startBannerTimer = 0f;
        CurrentState = State.Countdown;

        if (birthdayCardBoard != null) birthdayCardBoard.SetActive(false);
        if (goalFigure != null)
        {
            goalFigure.localPosition = goalFigureRestPos;
            goalFigure.localRotation = Quaternion.identity;
        }
        if (startLed != null) startLed.SetPalette(RunningColors);
        if (goalLed != null) goalLed.SetPalette(RunningColors);
        SetText(goalSign, "★ GOAL ばんじー ★\nタッチでゴール！");
    }

    void UpdateCountdown(float dt)
    {
        countdownTimer -= dt;
        int n = Mathf.CeilToInt(countdownTimer);
        if (n >= 1 && n <= 3 && n != lastCountShown)
        {
            lastCountShown = n;
            SetText(startSign, $"タイムアタック\n{n}");
            SetText(goalSign, $"★ GOAL ばんじー ★\nカウントダウン {n}");
            if (voiceCount != null && 3 - n < voiceCount.Length)
                Play(voiceCount[3 - n], 0.9f);
        }

        if (countdownTimer <= 0f)
        {
            ReleaseCountdownLock();
            CurrentState = State.Running;
            ElapsedTime = 0f;
            startBannerTimer = 1.1f;
            SetText(startSign, "スタート！\n奥のばんじーへ走れ！");
            SetText(goalSign, "★ GOAL ばんじー ★\nTIME  00.00");
            if (voiceCount != null && voiceCount.Length > 3)
                Play(voiceCount[3], 0.95f);
        }
    }

    void UpdateRunning(float dt)
    {
        ElapsedTime += dt;
        if (startBannerTimer > 0f) startBannerTimer -= dt;

        string formatted = FormatTime(ElapsedTime);
        if (startBannerTimer <= 0f)
            SetText(startSign, $"TIME  {formatted}\nR キー / ボタンでリトライ");
        SetText(goalSign, $"★ GOAL ばんじー ★\nTIME  {formatted}");

        if (ReachedGoal())
            Clear();
    }

    bool ReachedGoal()
    {
        if (goalPoint == null || body == null) return false;
        Vector3 feet = body.Feet;
        Vector3 diff = feet - goalPoint.position;
        float horizontal = new Vector2(diff.x, diff.z).magnitude;
        if (horizontal <= goalRadius && Mathf.Abs(diff.y) <= goalHeightTolerance) return true;

        Vector3 headDiff = body.Head - (goalPoint.position + Vector3.up * 0.9f);
        return new Vector2(headDiff.x, headDiff.z).magnitude <= goalRadius && Mathf.Abs(headDiff.y) <= goalHeightTolerance;
    }

    void Clear()
    {
        CurrentState = State.Cleared;
        ClearTime = ElapsedTime;
        if (BestTime <= 0f || ClearTime < BestTime) BestTime = ClearTime;

        Play(cheerSound, 0.85f);
        Play(niceVoice, 0.9f);

        string clearMsg = $"ばんじー誕生日おめでとう！\nCLEAR!  {FormatTime(ClearTime)}";
        if (startLed != null) startLed.SetPalette(ClearColors);
        if (goalLed != null) goalLed.SetPalette(ClearColors);
        SetText(startSign, clearMsg);
        SetText(goalSign, clearMsg);

        if (birthdayCard != null && birthdayCardBoard != null)
        {
            ApplyBirthdayCardTexture();
            birthdayCardBoard.SetActive(true);
        }
    }

    void AnimateGoalCelebration(float dt)
    {
        if (goalFigure == null) return;
        float hop = Mathf.Abs(Mathf.Sin(Time.time * 5.5f)) * 0.14f;
        goalFigure.localPosition = goalFigureRestPos + Vector3.up * hop;
        goalFigure.Rotate(0f, 90f * dt, 0f, Space.Self);
    }

    void ApplyBirthdayCardTexture()
    {
        if (birthdayCard == null || birthdayCardRenderer == null) return;
        var block = new MaterialPropertyBlock();
        birthdayCardRenderer.GetPropertyBlock(block);
        block.SetTexture("_BaseMap", birthdayCard);
        block.SetTexture("_MainTex", birthdayCard);
        birthdayCardRenderer.SetPropertyBlock(block);
    }

    bool RetryPressed()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.rKey.wasPressedThisFrame) return true;
        var pad = Gamepad.current;
        if (pad != null && pad.selectButton.wasPressedThisFrame) return true;
        if (vrRetryAction != null && vrRetryAction.WasPressedThisFrame()) return true;
        return false;
    }

    bool IsBungeeBusy() =>
        bungee != null && (bungee.CurrentPhase == BungeeJump.Phase.Falling || bungee.CurrentPhase == BungeeJump.Phase.Lowering);

    void ReleaseCountdownLock()
    {
        if (!lockedByCountdown) return;
        lockedByCountdown = false;
        if (body != null && body.Locked) body.EndExternalMotion();
    }

    void Play(AudioClip clip, float volume)
    {
        if (clip == null || sfx == null) return;
        sfx.PlayOneShot(clip, volume);
    }

    static void SetText(TextMesh tm, string text)
    {
        if (tm != null && tm.text != text) tm.text = text;
    }

    public static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int mins = Mathf.FloorToInt(seconds / 60f);
        float rem = seconds - mins * 60f;
        return mins > 0 ? $"{mins}:{rem:00.00}" : $"{rem:00.00} 秒";
    }
}
