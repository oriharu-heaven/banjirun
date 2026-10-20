using UnityEngine;

/// <summary>
/// プレイヤーの体の窓口。VR（VRPlayerBody）でも画面で遊ぶとき（FlatPlayerBody）でも、ゲームの仕組みはこれだけを使う。
/// 足の位置・頭・速さを返し、バンジーの間は移動を止めて外から動かせるようにする。
/// 体の大きさ（等身大 ⇔ ミニキャラ）もここで切り替える。小さくなると同じ建物がずっと高く見える。
/// 付ける GameObject は、左右の向きを持つ体の根元（CharacterController もここ）で、カメラはその子。
/// </summary>
public abstract class PlayerBody : MonoBehaviour
{
    /// <summary>ミニキャラの大きさ（身長 25cm ほど。元の小人の試作と同じ）。23m の高さが、目には 150m ほどに見える</summary>
    public const float MiniScale = 0.15f;
    /// <summary>大きさが変わるのにかかる時間（秒）</summary>
    const float ResizeTime = 0.8f;
    // 等身大の体（大きくなれるか調べるのに使う）
    const float FullHeight = 1.6f;
    const float FullRadius = 0.1f;

    Vector3 lastFeet;
    Vector3 velocity;
    float scaleFrom = 1f;
    float scaleTo = 1f;
    float resizeTimer = ResizeTime;

    public abstract Camera Camera { get; }
    public Vector3 Head => Camera.transform.position;
    /// <summary>頭の真下の床の位置</summary>
    public Vector3 Feet
    {
        get
        {
            Vector3 head = Head;
            return new Vector3(head.x, transform.position.y, head.z);
        }
    }
    public Vector3 Velocity => velocity;
    /// <summary>バンジーなどで、外から動かしている間は true（ふつうの移動は止まる）</summary>
    public bool Locked { get; private set; }
    /// <summary>今の体の大きさ（1 = 等身大、MiniScale = ミニキャラ）。切り替えの途中は間の値</summary>
    public float Scale { get; private set; } = 1f;
    /// <summary>ミニキャラになっている・なろうとしている</summary>
    public bool IsMini => scaleTo < 1f;
    /// <summary>歩こうとしている向き（水平・長さ 0〜1）。操作する側（FirstPersonControls や VR の移動）が入れる。よじ登り（ClimbAssist）が使う</summary>
    public Vector3 MoveIntent { get; set; }
    /// <summary>大きさの切り替えを始めたとき（ミニになるなら true）</summary>
    public event System.Action<bool> ResizeStarted;
    /// <summary>大きくなれなかったとき（頭の上がふさがっている・バンジー中）</summary>
    public event System.Action ResizeBlocked;

    protected virtual void Start()
    {
        lastFeet = Feet;
        ApplyScale(Scale);
    }

    protected virtual void Update()
    {
        UpdateResize();
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector3 feet = Feet;
        velocity = Vector3.Lerp(velocity, (feet - lastFeet) / dt, 0.5f);
        lastFeet = feet;
    }

    /// <summary>移動を止めて、MoveBy で動かせるようにする（バンジーの間）。</summary>
    public void BeginExternalMotion()
    {
        Locked = true;
        OnLockChanged(true);
    }

    public void MoveBy(Vector3 delta) => transform.position += delta;

    public void EndExternalMotion()
    {
        Locked = false;
        OnLockChanged(false);
    }

    /// <summary>足が feet に来るように移す。yaw は向く方角（度）。</summary>
    public void TeleportFeetTo(Vector3 feet, float yaw)
    {
        Teleport(feet, yaw);
        lastFeet = Feet;
        velocity = Vector3.zero;
    }

    /// <summary>等身大 ⇔ ミニキャラを切り替える（バンジーの間はできない）。</summary>
    public void ToggleMini() => SetMini(!IsMini);

    public void SetMini(bool mini, bool instant = false)
    {
        float target = mini ? MiniScale : 1f;
        if (Mathf.Approximately(target, scaleTo)) return;
        if (Locked || (!mini && !HasRoomToGrow()))
        {
            ResizeBlocked?.Invoke();
            return;
        }
        scaleFrom = Scale;
        scaleTo = target;
        resizeTimer = instant ? ResizeTime : 0f;
        ResizeStarted?.Invoke(mini);
        if (instant) SetScale(target);
    }

    void UpdateResize()
    {
        if (resizeTimer >= ResizeTime) return;
        resizeTimer += Time.deltaTime;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(resizeTimer / ResizeTime));
        // 大きさの比で補間すると、縮むのも伸びるのも同じ速さに見える
        SetScale(Mathf.Exp(Mathf.Lerp(Mathf.Log(scaleFrom), Mathf.Log(scaleTo), t)));
    }

    /// <summary>足の位置（床の上の点）を変えずに大きさを変える。</summary>
    void SetScale(float scale)
    {
        Vector3 feet = Feet;
        Scale = scale;
        ApplyScale(scale);
        Vector3 shift = feet - Feet;
        shift.y = 0f;
        transform.position += shift;
        Physics.SyncTransforms();
        lastFeet = Feet;
    }

    /// <summary>等身大に戻っても頭や体がぶつからないか（机の下などでは戻れない）。</summary>
    bool HasRoomToGrow()
    {
        Vector3 feet = Feet;
        Vector3 bottom = feet + Vector3.up * (FullRadius + 0.05f);
        Vector3 top = feet + Vector3.up * (FullHeight - FullRadius);
        foreach (var hit in Physics.OverlapCapsule(bottom, top, FullRadius * 0.9f, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform)) return false;
        return true;
    }

    protected abstract void Teleport(Vector3 feet, float yaw);
    protected abstract void OnLockChanged(bool locked);
    /// <summary>体（当たり判定・目の高さ・移動の速さ）を大きさ scale に合わせる。</summary>
    protected abstract void ApplyScale(float scale);
}
