using UnityEngine;

/// <summary>
/// 窓の外のバンジージャンプ。ジャンプ台に付ける。
/// ハーネスを付けて台の先から落ちると、ロープ（ばね）で何度か弾み、そのあと下のクッションまでゆっくり降ろされる。
/// クッションの上の光る輪に入ると、バルコニーへ戻る。
/// </summary>
public class BungeeJump : MonoBehaviour
{
    public enum Phase { Idle, Harnessed, Falling, Lowering, Landed }

    [Header("場所")]
    public Transform anchor;       // ロープの付け根
    public Transform jumpEdge;     // 台の先端の中央。forward が外向き
    public Transform dropLine;     // 手すり（パラペット）の外側。これより外で台より下へ落ちたら「飛んだ」とみなす。forward が外向き
    public float platformTop = 1.15f;
    public float cushionTop = -21.5f;
    public Transform cushion;      // 着地したときに弾ませる（底が原点）
    public Transform returnPad;    // 戻る輪
    public Transform returnPoint;  // 戻る場所（足の位置と向き）
    public BungeeHarness harness;

    [Header("ロープ（体重 1 あたり）")]
    public float cordLength = 9f;
    public float stiffness = 2.5f;
    public float damping = 0.25f;
    public float swayDamping = 0.4f;
    public float airDrag = 0.01f;
    public float waistHeight = 0.95f;
    public int bouncesBeforeLowering = 2;
    public float lowerSpeed = 2.2f;
    public LineRenderer cord;

    [Header("案内")]
    public TextMesh platformSign;
    public TextMesh countdownSign;  // 台の先の空中に出す大きな数字
    public TextMesh groundSign;

    const string IdleText = "ハーネスを付けてから\n飛ぼう";
    const string HarnessedText = "準備OK！\n先から飛び降りよう";
    const float CountdownStep = 0.9f;
    const int CordPoints = 16;

    public Phase CurrentPhase { get; private set; }
    public int Bounces { get; private set; }
    public float PhaseTime { get; private set; }
    public event System.Action<Phase> PhaseChanged;

    VRPlayerBody body;
    Vector3 waist;
    Vector3 velocity;
    float length;
    float countdown = -1f;
    bool rising;
    Vector3 cushionScale;
    float squashTime = float.MaxValue;

    void Start()
    {
        body = FindFirstObjectByType<VRPlayerBody>();
        if (cushion != null) cushionScale = cushion.localScale;
        if (cord != null) cord.positionCount = CordPoints;
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
        UpdateCord();
        AnimateCushion(dt);
    }

    /// <summary>ハーネスを付ける（BungeeHarness から呼ばれる）。</summary>
    public void AttachHarness()
    {
        if (CurrentPhase != Phase.Idle || body == null) return;
        harness.SetOnHook(false);
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

        // 台の先に立ったらカウントダウン（押し出しはしない。飛ぶのは自分で）
        float toEdge = Vector3.Dot(jumpEdge.position - feet, jumpEdge.forward);
        bool atEdge = outside && toEdge < 0.7f && feet.y > platformTop - 0.2f;
        if (atEdge)
        {
            countdown = Mathf.Max(countdown, 0f) + dt;
            int n = 3 - Mathf.FloorToInt(countdown / CountdownStep);
            SetText(countdownSign, n > 0 ? n.ToString() : "バンジー！");
        }
        else if (countdown >= 0f)
        {
            countdown = -1f;
            SetText(countdownSign, "");
        }
    }

    void StartFall()
    {
        waist = body.Feet + Vector3.up * waistHeight;
        velocity = body.Velocity;
        length = cordLength;
        Bounces = 0;
        rising = false;
        body.BeginExternalMotion();
        SetPhase(Phase.Falling);
    }

    void Simulate(float dt)
    {
        bool lowering = CurrentPhase == Phase.Lowering;
        if (lowering) length += lowerSpeed * dt;
        // 降ろすときは揺れないよう、ちょうど揺れが止まる強さで抑える
        float c = lowering ? 2f * Mathf.Sqrt(stiffness) : damping;
        Vector3 before = waist;

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
                a -= dir * (stiffness * (dist - length) + c * Vector3.Dot(velocity, dir));
                a -= swayDamping * Vector3.ProjectOnPlane(velocity, dir);
            }
            velocity += a * h;
            waist += velocity * h;
        }

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

        bool landed = waist.y - waistHeight <= cushionTop;
        if (landed) waist.y = cushionTop + waistHeight;
        body.MoveBy(waist - before);
        if (landed) Land();
    }

    void Land()
    {
        velocity = Vector3.zero;
        body.EndExternalMotion();
        harness.SetOnHook(true);
        squashTime = 0f;
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

    void UpdateCord()
    {
        if (cord == null) return;
        // ハーネスを付けている間は背中（腰の少し後ろ。顔の前を横切らないように）へ、それ以外はフックのハーネスへ
        bool attached = CurrentPhase == Phase.Harnessed || CurrentPhase == Phase.Falling || CurrentPhase == Phase.Lowering;
        Vector3 start = anchor.position;
        Vector3 back = -Vector3.ProjectOnPlane(body.transform.forward, Vector3.up).normalized * 0.3f;
        Vector3 end = attached ? (CurrentPhase == Phase.Harnessed ? body.Feet + Vector3.up * waistHeight : waist) + back : harness.cordPoint.position;
        float distance = Vector3.Distance(start, end);
        float rest = attached ? length : distance + 1.2f; // フックに掛けてある間は少したるませておく
        float sag = Mathf.Min(Mathf.Max(0f, rest - distance) * 0.5f, 6f);
        for (int i = 0; i < CordPoints; i++)
        {
            float t = i / (CordPoints - 1f);
            cord.SetPosition(i, Vector3.Lerp(start, end, t) + Vector3.down * (sag * 4f * t * (1f - t)));
        }
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
        SetText(countdownSign, "");
        if (phase == Phase.Harnessed) length = cordLength;
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
