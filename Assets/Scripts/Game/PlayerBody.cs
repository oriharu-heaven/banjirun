using UnityEngine;

/// <summary>
/// プレイヤーの体の窓口。VR（VRPlayerBody）でも画面で遊ぶとき（FlatPlayerBody）でも、ゲームの仕組みはこれだけを使う。
/// 足の位置・頭・速さを返し、バンジーの間は移動を止めて外から動かせるようにする。
/// 付ける GameObject は、左右の向きを持つ体の根元（CharacterController もここ）で、カメラはその子。
/// </summary>
public abstract class PlayerBody : MonoBehaviour
{
    Vector3 lastFeet;
    Vector3 velocity;

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

    protected virtual void Start() => lastFeet = Feet;

    protected virtual void Update()
    {
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

    protected abstract void Teleport(Vector3 feet, float yaw);
    protected abstract void OnLockChanged(bool locked);
}
