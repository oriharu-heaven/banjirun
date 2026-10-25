using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// エレベーターからのスタート演出と、バンジー着地時のお祝い画像表示を管理する（VR 版・画面版共通、XR 非依存）。
/// 1. スタート: エレベーター内（PlayerSpawn_Elevator）に配置。正面の扉に「START」表示が出て、E キー / クリック / VR グリップでエレベーターが止まり扉が開く
/// 2. 移動: エレベーターを出て入口ドアを開け、教室を歩いて窓から外（バンジー台）へ出る
/// 3. 着地時: バンジーで地面のクッションに着地したとき（BungeeJump.Phase.Landed）、「ばんじー誕生日おめでとう！」と画像（BirthdayCard.png）を表示する
/// </summary>
public class TimeAttackManager : MonoBehaviour, IUsable
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

    [Header("着地時の一枚絵（Assets/Bungee/Textures/BirthdayCard.png）")]
    public Texture2D birthdayCard;
    public GameObject birthdayCardBoard;
    public Renderer birthdayCardRenderer;

    [Header("音")]
    public AudioClip[] voiceCount;
    public AudioClip cheerSound;
    public AudioClip niceVoice;
    public AudioClip retrySound;

    public State CurrentState { get; private set; } = State.Running;
    public float CountdownRemaining => 0f;
    public int CountdownNumber => 0;
    public float ElapsedTime => 0f;
    public float ClearTime => 0f;
    public float BestTime => 0f;
    public bool ShowStartBanner => false;

    public bool IsElevatorWaiting => !elevatorOpening && !elevatorOpened;
    public bool IsElevatorOpen => elevatorOpened;
    public bool ShowBirthdayCard => bungee != null && bungee.CurrentPhase == BungeeJump.Phase.Landed;
    public string UseHint => "エレベーターを止めて扉を開ける（スタート）";

    public static readonly Vector3 ElevatorSpawn = new Vector3(-7.75f, 0.02f, 2.85f);
    public const float ElevatorYaw = 180f;
    const float DoorSlideDistance = 0.52f;
    const float DoorSlideDuration = 0.9f;

    PlayerBody body;
    FirstPersonControls controls;
    BungeeJump bungee;
    AudioSource sfx;
    BoxCollider doorTriggerCollider;
    Transform carDoorL, carDoorR, landingDoorL, landingDoorR;
    Vector3 carDoorLStart, carDoorRStart, landingDoorLStart, landingDoorRStart;
    float carDoorLSign = -1f, carDoorRSign = 1f, landingDoorLSign = -1f, landingDoorRSign = 1f;
    bool elevatorOpening;
    bool elevatorOpened;
    float doorOpenProgress;

    public void Use(Vector3 actor) => OpenElevator();
    public void Retry() => OpenElevator();

    void Start()
    {
        body = FindFirstObjectByType<PlayerBody>();
        controls = FindFirstObjectByType<FirstPersonControls>();
        bungee = FindFirstObjectByType<BungeeJump>();

        if (body != null && body.Camera != null)
        {
            // 既存シーンの静的オクルージョンでエレベーター扉の奥が消えるのを防ぐ
            body.Camera.useOcclusionCulling = false;
            sfx = body.Camera.gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
        }

#if UNITY_EDITOR
        if (birthdayCard == null)
            birthdayCard = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Bungee/Textures/BirthdayCard.png");
#endif

        CleanupOldTimeAttackProps();
        ClearWalkObstacles();
        SetupElevatorDoors();
        SetupLandingBirthdayBoard();

        if (body != null)
            body.TeleportFeetTo(ElevatorSpawn, ElevatorYaw);
    }

    void Update()
    {
        if (body == null) return;
        if (controls != null && controls.Scripted)
        {
            if (elevatorOpening && !elevatorOpened)
                AnimateElevatorDoors(Time.deltaTime);
            UpdateLandingBirthdayBoard();
            return;
        }

        // エレベーター内でスタート待ちのとき、E キーまたはクリックでエレベーターを止めて扉を開ける
        if (IsElevatorWaiting && StartPressed())
            OpenElevator();

        if (elevatorOpening && !elevatorOpened)
            AnimateElevatorDoors(Time.deltaTime);

        UpdateLandingBirthdayBoard();
    }

    public void OpenElevator()
    {
        if (elevatorOpening || elevatorOpened) return;
        elevatorOpening = true;
        doorOpenProgress = 0f;
        Play(retrySound, 0.9f);

        // 注意: startSign は TimeAttackStartSign 自身のコンポーネントなので startSign.gameObject を消す（parent は TimeAttack 自身）
        if (startSign != null)
            startSign.gameObject.SetActive(false);
        if (doorTriggerCollider != null)
            doorTriggerCollider.enabled = false;

        // 扉が開き始めた瞬間に扉のコライダーを無効化し、Static Collider が残って外に出られなくなるのを確実に防ぐ
        SetDoorColliders(carDoorL, false);
        SetDoorColliders(carDoorR, false);
        SetDoorColliders(landingDoorL, false);
        SetDoorColliders(landingDoorR, false);
        Physics.SyncTransforms();
    }

    void AnimateElevatorDoors(float dt)
    {
        doorOpenProgress = Mathf.MoveTowards(doorOpenProgress, 1f, dt / DoorSlideDuration);
        float t = Mathf.SmoothStep(0f, 1f, doorOpenProgress);
        float dist = DoorSlideDistance * t;

        if (carDoorL != null) carDoorL.position = carDoorLStart + new Vector3(carDoorLSign * dist, 0f, 0f);
        if (carDoorR != null) carDoorR.position = carDoorRStart + new Vector3(carDoorRSign * dist, 0f, 0f);
        if (landingDoorL != null) landingDoorL.position = landingDoorLStart + new Vector3(landingDoorLSign * dist, 0f, 0f);
        if (landingDoorR != null) landingDoorR.position = landingDoorRStart + new Vector3(landingDoorRSign * dist, 0f, 0f);

        if (doorOpenProgress >= 1f)
            elevatorOpened = true;
    }

    void SetupElevatorDoors()
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            switch (t.name)
            {
                case "EV_CarDoors_L":
                    carDoorL = t;
                    carDoorLStart = t.position;
                    MakeDynamicRecursive(t);
                    break;
                case "EV_CarDoors_R":
                    carDoorR = t;
                    carDoorRStart = t.position;
                    MakeDynamicRecursive(t);
                    break;
                case "EV_LandingDoors_L":
                    landingDoorL = t;
                    landingDoorLStart = t.position;
                    MakeDynamicRecursive(t);
                    break;
                case "EV_LandingDoors_R":
                    landingDoorR = t;
                    landingDoorRStart = t.position;
                    MakeDynamicRecursive(t);
                    break;
            }
        }

        const float doorwayCenterX = -7.75f;
        carDoorLSign = GetDoorOutwardSign(carDoorL, doorwayCenterX, -1f);
        carDoorRSign = GetDoorOutwardSign(carDoorR, doorwayCenterX, 1f);
        if (Mathf.Approximately(carDoorLSign, carDoorRSign))
        {
            carDoorLSign = -1f;
            carDoorRSign = 1f;
        }
        landingDoorLSign = GetDoorOutwardSign(landingDoorL, doorwayCenterX, carDoorLSign);
        landingDoorRSign = GetDoorOutwardSign(landingDoorR, doorwayCenterX, carDoorRSign);
        if (Mathf.Approximately(landingDoorLSign, landingDoorRSign))
        {
            landingDoorLSign = carDoorLSign;
            landingDoorRSign = carDoorRSign;
        }

        // エレベーターとロビーの間の隙間を滑らかに歩けるよう透明な床コライダーを敷く
        var bridge = new GameObject("ElevatorFloorBridge");
        bridge.transform.SetParent(transform, false);
        bridge.transform.position = new Vector3(-7.75f, -0.05f, 1.75f);
        var bridgeCol = bridge.AddComponent<BoxCollider>();
        bridgeCol.size = new Vector3(1.3f, 0.1f, 0.65f);

        // エレベーター扉全体のトリガー判定
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        doorTriggerCollider = gameObject.GetComponent<BoxCollider>();
        if (doorTriggerCollider == null) doorTriggerCollider = gameObject.AddComponent<BoxCollider>();
        doorTriggerCollider.center = new Vector3(-7.75f, 1.15f, 2.02f);
        doorTriggerCollider.size = new Vector3(1.05f, 2.1f, 0.16f);
        doorTriggerCollider.enabled = true;

        // エレベーター扉正面に「START」表示を配置（扉 Z=1.96 から 9cm 手前の Z=2.05 で面重なり 0）
        if (startSign != null)
        {
            var signGo = startSign.gameObject;
            signGo.SetActive(true);
            signGo.transform.SetPositionAndRotation(new Vector3(-7.75f, 1.48f, 2.05f), Quaternion.LookRotation(Vector3.back));
            signGo.transform.localScale = Vector3.one * 0.62f;
            startSign.text = "START\n扉を開ける";
        }
    }

    static float GetDoorOutwardSign(Transform doorRoot, float centerX, float fallbackSign)
    {
        if (doorRoot == null) return fallbackSign;
        var renderers = doorRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return fallbackSign;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        float diff = b.center.x - centerX;
        return Mathf.Abs(diff) > 0.01f ? Mathf.Sign(diff) : fallbackSign;
    }

    static void SetDoorColliders(Transform doorRoot, bool enabled)
    {
        if (doorRoot == null) return;
        foreach (var col in doorRoot.GetComponentsInChildren<Collider>(true))
            col.enabled = enabled;
    }

    static void MakeDynamicRecursive(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.isStatic = false;
            if (t.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                mf.mesh = Instantiate(mf.sharedMesh);
            if (t.TryGetComponent<Renderer>(out var r))
            {
                r.lightmapIndex = -1;
                r.enabled = false;
                r.enabled = true;
            }
        }
    }

    void CleanupOldTimeAttackProps()
    {
        if (goalFigure != null)
        {
            var holder = goalFigure.parent != null && goalFigure.parent != transform ? goalFigure.parent : goalFigure;
            holder.gameObject.SetActive(false);
        }
        // 注意: goalSign は TimeAttackGoalSign 自身のコンポーネントなので goalSign.gameObject を消す
        if (goalSign != null)
            goalSign.gameObject.SetActive(false);

        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform) continue;
            if (t.name == "StartRetryButton" || t.name == "GoalRetryButton" || t.name == "ReturnPadEffect")
                t.gameObject.SetActive(false);
        }
    }

    static void ClearWalkObstacles()
    {
        foreach (var col in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            string n = col.name;
            if (n.Contains("_Chair_") || n.StartsWith("Cable_") || n.StartsWith("CasterWagon") || n.Contains("UmbrellaStand"))
                col.enabled = false;
        }
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.name.StartsWith("CasterWagon") && r.transform.position.x < -4.5f)
            {
                r.gameObject.isStatic = false;
                if (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                    mf.mesh = Instantiate(mf.sharedMesh);
                r.enabled = false;
                r.transform.position += new Vector3(1.8f, 0f, 0f);
                r.enabled = true;
            }
        }
    }

    void SetupLandingBirthdayBoard()
    {
        ApplyBirthdayCardTexture();
        if (birthdayCardBoard == null) return;
        birthdayCardBoard.SetActive(false);

        // バンジー着地クッションの正面（GroundSign のすぐ上）に配置して、着地した瞬間に目の前にも出るようにする
        if (bungee != null && bungee.cushion != null && bungee.groundSign != null)
        {
            Transform gs = bungee.groundSign.transform;
            Vector3 toCushion = Vector3.ProjectOnPlane(bungee.cushion.position - gs.position, Vector3.up).normalized;
            if (toCushion.sqrMagnitude < 0.01f) toCushion = Vector3.right;
            Vector3 boardPos = gs.position + Vector3.up * 1.25f;
            birthdayCardBoard.transform.SetPositionAndRotation(boardPos, Quaternion.LookRotation(-toCushion));
            birthdayCardBoard.transform.localScale = Vector3.one * 1.35f;
        }
    }

    void UpdateLandingBirthdayBoard()
    {
        bool landed = ShowBirthdayCard;
        if (birthdayCardBoard != null && birthdayCard != null && birthdayCardBoard.activeSelf != landed)
        {
            if (landed) ApplyBirthdayCardTexture();
            birthdayCardBoard.SetActive(landed);
        }
        if (landed && bungee != null && bungee.groundSign != null)
        {
            const string msg = "ばんじー誕生日おめでとう！\n光る輪で上に戻る";
            if (bungee.groundSign.text != msg) bungee.groundSign.text = msg;
        }
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

    bool StartPressed()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame) return true;
        var pad = Gamepad.current;
        if (pad != null && (pad.buttonWest.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)) return true;
        return false;
    }

    void Play(AudioClip clip, float volume)
    {
        if (clip == null || sfx == null) return;
        sfx.PlayOneShot(clip, volume);
    }

    public static string FormatTime(float seconds) => $"{seconds:00.00} 秒";
}
