using System.Collections;
using UnityEngine;

/// <summary>
/// 窓の外のバンジージャンプ。ジャンプ台に付ける。
/// ハーネスを付けて、台の先の細い板から落ちると、ロープ（ばね）で何度か弾み、そのあと下のクッションまでゆっくり降ろされる。
/// クッションの上の光る輪に入ると、バルコニーへ戻る。
/// 怖さの演出: 板がしなってきしむ、心臓の音（板の先ほど速く大きく）、係の人のカウントダウン、落ちる間の風の音と風の筋、
/// ロープが張るときの音、地面すれすれまで落ちる長いロープ。
/// VR でも画面版でも同じ。プレイヤーは PlayerBody を通してだけ動かす。
/// ミニキャラ（PlayerBody.Scale）のときは、腰の高さ・体に付けたハーネスを縮め、ロープは地面すれすれに届く長さ・強さに変える。
/// </summary>
public class BungeeJump : MonoBehaviour
{
    public enum Phase { Idle, Harnessed, Falling, Lowering, Landed }

    [Header("場所")]
    public Transform anchor;       // ロープの付け根
    public Transform jumpEdge;     // 板の先端の中央。forward が外向き
    public Transform dropLine;     // 手すり（パラペット）の外側。これより外で台より下へ落ちたら「飛んだ」とみなす。forward が外向き
    public float platformTop = 1.15f;
    public float cushionTop = -21.5f;
    public Transform cushion;      // 着地したときに弾ませる（底が原点）
    public Transform returnPad;    // 戻る輪
    public Transform returnPoint;  // 戻る場所（足の位置と向き）
    public BungeeHarness harness;

    [Header("板（台の先の細い飛び込み板）")]
    public Transform plank;        // 付け根が原点で forward が外向き。乗るとしなって揺れる
    public float plankLength = 1.35f;
    public float plankHalfWidth = 0.25f;
    public float plankDip = 3f;    // 先に立ったときに下がる角度（度）

    [Header("ロープ（体重 1 あたり。最初の一番下でクッションの約 1m 上まで落ちる）")]
    public float cordLength = 13f;
    public float stiffness = 2.9f;
    public float damping = 0.25f;
    public float swayDamping = 0.4f;
    public float airDrag = 0.01f;
    public float waistHeight = 0.95f;   // 等身大のとき。ミニでは大きさに合わせて縮める
    [Tooltip("ミニキャラのときのロープ。腰が低く体が小さいので、最初の一番下でクッションの約 30cm 上（体 1 つ分ほど）まで落ちるようにした")]
    public float miniCordLength = 15f;
    public float miniStiffness = 3.3f;
    public int bouncesBeforeLowering = 2;
    public float lowerSpeed = 2.2f;
    public LineRenderer cord;

    [Header("案内")]
    public TextMesh platformSign;
    public TextMesh countdownSign;  // 板の先の空中に出す大きな数字
    public TextMesh groundSign;

    [Header("音")]
    public AudioSource ambientWind; // 台のまわりの風（3D・ループ）
    public AudioSource staffVoice;  // 係の人の声（台の上）
    public AudioClip heartbeat, fallWind, jumpWhoosh, cordStretch, plankCreak, carabiner, cushionLand, cheer, voiceNice;
    public AudioClip[] voiceCount;  // 3, 2, 1, バンジー！
    public AudioClip shrinkSound, growSound; // ミニキャラになる・等身大に戻る（音を 1 か所で鳴らして録画にも残すため、ここで鳴らす）

    [Header("風の筋（落ちる速さが分かるように）")]
    public Material windStreakMaterial;

    [Header("体に付けたハーネス（付けている間だけ。外から見たときだけ映り、自分の目には映さない）")]
    public GameObject wornHarnessModel;
    public float wornHarnessHeight = 0.85f; // 足からハーネスの真ん中まで

    /// <summary>自分のカメラには映さず、外から見るカメラ（録画の外からの視点など）にだけ映す物のレイヤー</summary>
    public const int ThirdPersonOnlyLayer = 26;

    const string IdleText = "ハーネスを付けてから\n飛ぼう";
    const string HarnessedText = "準備OK！\n板の先から飛ぼう";
    const float CountdownStep = 1f;
    const int CordPoints = 16;

    public Phase CurrentPhase { get; private set; }
    public int Bounces { get; private set; }
    public float PhaseTime { get; private set; }
    public float PlankProgress { get; private set; }  // 板のどこまで来たか（0〜1）
    public event System.Action<Phase> PhaseChanged;
    /// <summary>ロープがピンと張った瞬間（強さ 0〜1）。画面版のカメラの揺れなどに使う。</summary>
    public event System.Action<float> CordSnapped;
    /// <summary>鳴らした音（音・音量・3D の位置。2D は null）。録画で音を後から重ねるのに使う。</summary>
    public static event System.Action<AudioClip, float, Vector3?> SoundPlayed;

    PlayerBody body;
    Vector3 waist;
    Vector3 velocity;
    float length;
    float cordStiffness;
    float cordScale = -1f;
    float countdown = -1f;
    int shownCount = -1;
    bool rising;
    bool taut;
    Vector3 cushionScale;
    float squashTime = float.MaxValue;
    Vector3 countdownScale;
    Quaternion plankRest;
    float plankAngle;
    float creakTimer;
    bool onPlank;
    AudioSource heartSource, windSource, sfx;
    ParticleSystem streaks;
    Transform wornHarness;

    void Start()
    {
        body = FindFirstObjectByType<PlayerBody>();
        if (cushion != null) cushionScale = cushion.localScale;
        if (cord != null) cord.positionCount = CordPoints;
        if (countdownSign != null) countdownScale = countdownSign.transform.localScale;
        if (plank != null) plankRest = plank.rotation;
        if (body != null)
        {
            SetupPlayerEffects(body.Camera.transform);
            body.ResizeStarted += mini => Play(mini ? shrinkSound : growSound, 0.7f);
        }
        if (wornHarnessModel != null)
        {
            // 体の無い 1 人称で下を見ると、宙に浮いたハーネスが視界の真ん中をふさぐので、自分の目には映さない
            wornHarness = CreateCentered(wornHarnessModel, "WornHarness");
            foreach (var t in wornHarness.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ThirdPersonOnlyLayer;
            if (body != null) body.Camera.cullingMask &= ~(1 << ThirdPersonOnlyLayer);
        }
        SetPhase(Phase.Idle);
    }

    void Update()
    {
        if (body == null) return;
        float dt = Time.deltaTime;
        PhaseTime += dt;
        switch (CurrentPhase)
        {
            case Phase.Idle: CheckReturnPad(); break;
            case Phase.Harnessed: UpdateHarnessed(dt); break;
            case Phase.Falling:
            case Phase.Lowering: Simulate(dt); break;
            case Phase.Landed: CheckReturnPad(); break;
        }
        UpdatePlank(dt);
        UpdateWornHarness();
        UpdateCord();
        UpdateEffects(dt);
        AnimateCushion(dt);
    }

    /// <summary>ハーネスを付ける（BungeeHarness から呼ばれる）。</summary>
    public void AttachHarness()
    {
        if (CurrentPhase != Phase.Idle || body == null) return;
        harness.SetOnHook(false);
        Play(carabiner, 0.9f);
        SetPhase(Phase.Harnessed);
    }

    void UpdateHarnessed(float dt)
    {
        Vector3 feet = body.Feet;
        bool outside = Vector3.Dot(feet - dropLine.position, dropLine.forward) > 0f;
        if (outside && feet.y < platformTop - 0.3f)
        {
            StartFall();
            return;
        }

        // 板の先に立ったらカウントダウン（押し出しはしない。飛ぶのは自分で）
        float toEdge = Vector3.Dot(jumpEdge.position - feet, jumpEdge.forward);
        bool atEdge = outside && toEdge < 0.6f && feet.y > platformTop - 0.25f;
        if (atEdge)
        {
            countdown = Mathf.Max(countdown, 0f) + dt;
            int n = Mathf.Max(0, 3 - Mathf.FloorToInt(countdown / CountdownStep));
            if (n != shownCount)
            {
                shownCount = n;
                SetText(countdownSign, n > 0 ? n.ToString() : "バンジー！");
                // 電光掲示板（LedSign）なら掲示板が自分で点滅する。ただの文字なら、大きくしてから戻す
                if (countdownSign != null && countdownSign.GetComponent<LedSign>() == null) countdownSign.transform.localScale = countdownScale * 1.6f;
                if (voiceCount != null && 3 - n < voiceCount.Length) PlayVoice(voiceCount[3 - n], 1f);
            }
        }
        else if (countdown >= 0f)
        {
            countdown = -1f;
            shownCount = -1;
            SetText(countdownSign, "");
        }
    }

    /// <summary>体の大きさに合わせた腰の高さ</summary>
    float Waist => waistHeight * body.Scale;

    void StartFall()
    {
        waist = body.Feet + Vector3.up * Waist;
        velocity = body.Velocity;
        length = CordLengthFor(body.Scale);
        cordStiffness = Mathf.Lerp(miniStiffness, stiffness, Mathf.InverseLerp(PlayerBody.MiniScale, 1f, body.Scale));
        Bounces = 0;
        rising = false;
        taut = false;
        body.BeginExternalMotion();
        Play(jumpWhoosh, 1f);
        SetPhase(Phase.Falling);
    }

    void Simulate(float dt)
    {
        bool lowering = CurrentPhase == Phase.Lowering;
        if (lowering) length += lowerSpeed * dt;
        // 降ろすときは揺れないよう、ちょうど揺れが止まる強さで抑える
        float c = lowering ? 2f * Mathf.Sqrt(cordStiffness) : damping;
        Vector3 before = waist;
        bool tautNow = false;

        const int steps = 4;
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            Vector3 a = Physics.gravity - airDrag * velocity.magnitude * velocity;
            Vector3 d = waist - anchor.position;
            float dist = d.magnitude;
            if (dist > length)
            {
                Vector3 dir = d / dist;
                a -= dir * (cordStiffness * (dist - length) + c * Vector3.Dot(velocity, dir));
                a -= swayDamping * Vector3.ProjectOnPlane(velocity, dir);
                tautNow = true;
            }
            velocity += a * h;
            waist += velocity * h;
        }

        // ロープがピンと張った瞬間の音（速いほど大きく）
        if (tautNow && !taut && !lowering && velocity.magnitude > 3f)
        {
            float strength = Mathf.Clamp01(velocity.magnitude / 14f);
            Play(cordStretch, Mathf.Clamp01(0.3f + strength));
            CordSnapped?.Invoke(strength);
        }
        taut = tautNow;

        // 弾んだ回数を数え、何度か弾んだら一番上で降ろし始める
        if (!rising && velocity.y > 0.5f)
        {
            rising = true;
            Bounces++;
        }
        else if (rising && velocity.y < 0f)
        {
            rising = false;
            if (!lowering && Bounces >= bouncesBeforeLowering) SetPhase(Phase.Lowering);
        }

        bool landed = waist.y - Waist <= cushionTop;
        if (landed) waist.y = cushionTop + Waist;
        body.MoveBy(waist - before);
        if (landed) Land();
    }

    void Land()
    {
        velocity = Vector3.zero;
        body.EndExternalMotion();
        harness.SetOnHook(true);
        squashTime = 0f;
        Play(cushionLand, 1f);
        StartCoroutine(PlayLater(cheer, 0.3f, 0.55f, false));
        StartCoroutine(PlayLater(voiceNice, 0.9f, 0.8f, true));
        SetPhase(Phase.Landed);
    }

    void CheckReturnPad()
    {
        if (returnPad == null) return;
        Vector3 feet = body.Feet;
        Vector3 offset = feet - returnPad.position;
        if (Mathf.Abs(offset.y) > 0.6f || new Vector2(offset.x, offset.z).magnitude > 0.6f) return;
        body.TeleportFeetTo(returnPoint.position, returnPoint.eulerAngles.y);
        SetPhase(Phase.Idle);
    }

    /// <summary>板に乗ると先ほど下がってゆらゆら揺れ、きしむ。</summary>
    void UpdatePlank(float dt)
    {
        if (plank == null) return;
        float p = 0f;
        if (CurrentPhase == Phase.Idle || CurrentPhase == Phase.Harnessed)
        {
            Vector3 rel = Quaternion.Inverse(plankRest) * (body.Feet - plank.position);
            if (rel.z > -0.05f && rel.z < plankLength + 0.15f && Mathf.Abs(rel.x) < plankHalfWidth + 0.15f && Mathf.Abs(rel.y) < 0.4f)
                p = Mathf.Clamp01(rel.z / plankLength);
        }
        PlankProgress = p;

        bool now = p > 0.03f;
        if (now && !onPlank) PlayAt(plankCreak, jumpEdge.position, 0.9f);
        onPlank = now;
        if (p > 0.6f)
        {
            creakTimer -= dt;
            if (creakTimer <= 0f)
            {
                PlayAt(plankCreak, jumpEdge.position, 0.6f);
                creakTimer = Random.Range(1.8f, 3f);
            }
        }

        float target = plankDip * p + Mathf.Sin(Time.time * 6.5f) * 0.6f * p;
        plankAngle = Mathf.Lerp(plankAngle, target, 1f - Mathf.Exp(-8f * dt));
        plank.rotation = plankRest * Quaternion.Euler(plankAngle, 0f, 0f);
    }

    /// <summary>付けている間は、体（足の真上）について行き、顔の向きに合わせて回す。</summary>
    void UpdateWornHarness()
    {
        if (wornHarness == null) return;
        bool attached = CurrentPhase == Phase.Harnessed || CurrentPhase == Phase.Falling || CurrentPhase == Phase.Lowering;
        if (wornHarness.gameObject.activeSelf != attached) wornHarness.gameObject.SetActive(attached);
        if (!attached) return;
        float yaw = body.Camera.transform.eulerAngles.y;
        wornHarness.SetPositionAndRotation(body.Feet + Vector3.up * (wornHarnessHeight * body.Scale), Quaternion.Euler(0f, yaw, 0f));
        wornHarness.localScale = Vector3.one * body.Scale;
    }

    /// <summary>モデルを、見た目の真ん中が原点に来る入れ物に入れて作る（当たり判定は外す）。</summary>
    Transform CreateCentered(GameObject model, string name)
    {
        var pivot = new GameObject(name).transform;
        pivot.SetParent(transform, false);
        var instance = Instantiate(model, pivot);
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            instance.transform.position += pivot.position - b.center;
        }
        foreach (var c in instance.GetComponentsInChildren<Collider>()) Destroy(c);
        pivot.gameObject.SetActive(false);
        return pivot;
    }

    void UpdateCord()
    {
        if (cord == null) return;
        // ハーネスを付けている間は背中（腰の少し後ろ。顔の前を横切らないように）へ、それ以外はフックのハーネスへ
        bool attached = CurrentPhase == Phase.Harnessed || CurrentPhase == Phase.Falling || CurrentPhase == Phase.Lowering;
        Vector3 start = anchor.position;
        // ミニでも 12cm は後ろへ（体に合わせて縮めると、目のすぐ前を太い線が横切る）
        Vector3 back = -Vector3.ProjectOnPlane(body.transform.forward, Vector3.up).normalized * Mathf.Max(0.3f * body.Scale, 0.12f);
        Vector3 end = attached ? (CurrentPhase == Phase.Harnessed ? body.Feet + Vector3.up * Waist : waist) + back : harness.cordPoint.position;
        float distance = Vector3.Distance(start, end);
        float rest = attached ? (CurrentPhase == Phase.Harnessed ? CordLengthFor(body.Scale) : length) : distance + 1.2f; // フックに掛けてある間は少したるませておく
        // ミニの体に付いている端は細くする
        float endWidth = attached ? Mathf.Max(body.Scale, 0.25f) : 1f;
        if (!Mathf.Approximately(endWidth, cordScale))
        {
            cordScale = endWidth;
            cord.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, endWidth);
        }
        float sag = Mathf.Min(Mathf.Max(0f, rest - distance) * 0.5f, 6f);
        for (int i = 0; i < CordPoints; i++)
        {
            float t = i / (CordPoints - 1f);
            cord.SetPosition(i, Vector3.Lerp(start, end, t) + Vector3.down * (sag * 4f * t * (1f - t)));
        }
    }

    /// <summary>体の大きさに合わせたロープの長さ（等身大とミニの間は補間）</summary>
    float CordLengthFor(float scale) => Mathf.Lerp(miniCordLength, cordLength, Mathf.InverseLerp(PlayerBody.MiniScale, 1f, scale));

    // ---- 音と風の筋 ----

    void SetupPlayerEffects(Transform head)
    {
        heartSource = AddLoop(head, heartbeat);
        windSource = AddLoop(head, fallWind);
        sfx = head.gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;

        if (windStreakMaterial == null) return;
        // 頭について行く（回転はしない）入れ物の中で、粒を自分の速さの逆向きに流す。
        // 入れ物ごと動くので、粒は空中に止まっていて、そこを自分が突き抜けていくように見える
        var go = new GameObject("WindStreaks");
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.identity;
        streaks = go.AddComponent<ParticleSystem>();
        streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = streaks.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = 1.4f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
        main.maxParticles = 1500;
        var emission = streaks.emission;
        emission.rateOverTime = 0f;
        var shape = streaks.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 4.5f;
        shape.radiusThickness = 0.7f;
        var flow = streaks.velocityOverLifetime;
        flow.enabled = true;
        flow.space = ParticleSystemSimulationSpace.Local;
        // 流れる速さで伸ばして線にする
        var renderer = streaks.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.cameraVelocityScale = 0f;
        renderer.velocityScale = 0.08f;
        renderer.lengthScale = 1f;
        renderer.sharedMaterial = windStreakMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        streaks.Play();
    }

    static AudioSource AddLoop(Transform head, AudioClip clip)
    {
        if (clip == null) return null;
        var source = head.gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.volume = 0f;
        source.playOnAwake = false;
        source.Play();
        return source;
    }

    void UpdateEffects(float dt)
    {
        bool flying = CurrentPhase == Phase.Falling || CurrentPhase == Phase.Lowering;
        Vector3 v = flying ? velocity : body.Velocity;
        float speed = v.magnitude;

        if (windSource != null)
        {
            float target = Mathf.Pow(Mathf.Clamp01((speed - 2f) / 12f), 1.3f);
            windSource.volume = Mathf.MoveTowards(windSource.volume, target, dt * 2.5f);
            windSource.pitch = 0.8f + Mathf.Clamp01(speed / 15f) * 0.5f;
        }
        if (heartSource != null)
        {
            // ハーネスを付けると鳴り始め、板の先・カウントダウンで速く大きくなる。飛んだら止まる
            float tension = Mathf.Max(PlankProgress, countdown >= 0f ? 1f : 0f);
            float target = CurrentPhase == Phase.Harnessed ? 0.35f + 0.6f * tension : 0f;
            heartSource.volume = Mathf.MoveTowards(heartSource.volume, target, dt * (target > heartSource.volume ? 0.8f : 3f));
            heartSource.pitch = 0.95f + 0.4f * tension;
        }
        if (streaks != null)
        {
            var emission = streaks.emission;
            emission.rateOverTime = flying && speed > 4f ? (speed - 4f) * 55f : 0f;
            streaks.transform.SetPositionAndRotation(body.Head, Quaternion.identity);
            var flow = streaks.velocityOverLifetime;
            flow.x = -v.x;
            flow.y = -v.y;
            flow.z = -v.z;
            var shape = streaks.shape;
            shape.position = speed > 0.1f ? v / speed * 3.5f : Vector3.zero; // 進む先に出す
        }
        if (countdownSign != null)
            countdownSign.transform.localScale = Vector3.Lerp(countdownSign.transform.localScale, countdownScale, 1f - Mathf.Exp(-10f * dt));
    }

    void Play(AudioClip clip, float volume)
    {
        if (clip == null || sfx == null) return;
        sfx.PlayOneShot(clip, volume);
        SoundPlayed?.Invoke(clip, volume, null);
    }

    void PlayVoice(AudioClip clip, float volume)
    {
        if (clip == null || staffVoice == null) return;
        staffVoice.PlayOneShot(clip, volume);
        SoundPlayed?.Invoke(clip, volume, staffVoice.transform.position);
    }

    static void PlayAt(AudioClip clip, Vector3 position, float volume)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, volume);
        SoundPlayed?.Invoke(clip, volume, position);
    }

    IEnumerator PlayLater(AudioClip clip, float delay, float volume, bool voice)
    {
        yield return new WaitForSeconds(delay);
        if (voice) PlayVoice(clip, volume);
        else Play(clip, volume);
    }

    void AnimateCushion(float dt)
    {
        if (cushion == null || squashTime > 2f) return;
        squashTime += dt;
        // 着地でつぶれて、ぷるんと戻る
        float k = -0.12f * Mathf.Cos(squashTime * 14f) * Mathf.Exp(-squashTime * 5f);
        cushion.localScale = new Vector3(cushionScale.x * (1f - k * 0.4f), cushionScale.y * (1f + k), cushionScale.z * (1f - k * 0.4f));
    }

    void SetPhase(Phase phase)
    {
        CurrentPhase = phase;
        PhaseTime = 0f;
        countdown = -1f;
        shownCount = -1;
        SetText(countdownSign, "");
        switch (phase)
        {
            case Phase.Idle:
                SetText(platformSign, IdleText);
                SetText(groundSign, "下はクッション\n安心して飛んでね");
                break;
            case Phase.Harnessed:
                SetText(platformSign, HarnessedText);
                break;
            case Phase.Falling:
                SetText(platformSign, "");
                SetText(groundSign, "ナイスジャンプ！");
                break;
            case Phase.Landed:
                SetText(platformSign, IdleText);
                SetText(groundSign, "着地成功！\n光る輪で上に戻る");
                break;
        }
        PhaseChanged?.Invoke(phase);
    }

    static void SetText(TextMesh sign, string text)
    {
        if (sign != null) sign.text = text;
    }
}
