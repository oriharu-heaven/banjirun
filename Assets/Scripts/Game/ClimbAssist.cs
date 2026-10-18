using System.Collections;
using UnityEngine;

/// <summary>
/// ミニキャラのときの、よじ登り。段（舞台 20cm・窓のさん・ジャンプ台の階段 29cm など）に向かって歩くと、
/// 手を掛けて上に乗る。ミニの体（身長 25cm）は、ふつうの段差をそのまま上がれないため。
/// 等身大のときは何もしない（段は CharacterController がそのまま上がる）。VR でも画面版でも同じ。
/// 歩こうとしている向きは PlayerBody.MoveIntent から読む。
/// </summary>
[RequireComponent(typeof(PlayerBody))]
public class ClimbAssist : MonoBehaviour
{
    public float maxHeightPerScale = 2.3f;  // 登れる高さ（体の大きさ 1 あたり）。ミニ（0.15）で約 35cm
    public float stepHeightPerScale = 0.3f; // これより低い段は、登らずに歩いて上がる
    public float radiusPerScale = 0.1f;     // 体の太さ（半径）
    public float heightPerScale = 1.6f;
    public float climbTime = 0.55f;
    public float onlyBelowScale = 0.5f;

    /// <summary>よじ登り始めたとき（登る高さ）</summary>
    public event System.Action<float> Climbing;

    PlayerBody body;
    bool climbing;

    void Awake() => body = GetComponent<PlayerBody>();

    void Update()
    {
        if (climbing || body.Locked || body.Scale > onlyBelowScale) return;
        Vector3 dir = body.MoveIntent;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.09f) return;
        dir.Normalize();
        if (FindLedge(dir, out Vector3 top, out float forward)) StartCoroutine(Climb(top.y - body.Feet.y, dir * forward));
    }

    /// <summary>目の前に、登れる高さの段の上面があるか探す。</summary>
    bool FindLedge(Vector3 dir, out Vector3 top, out float forward)
    {
        top = default;
        forward = 0f;
        float s = body.Scale;
        float radius = radiusPerScale * s, maxHeight = maxHeightPerScale * s, step = stepHeightPerScale * s;
        Vector3 feet = body.Feet;
        if (!Cast(feet + Vector3.up * 0.02f, Vector3.down, 0.06f, out _)) return false; // 空中では登らない

        // すぐ前に壁があるか。すねの高さだけでなく、胸・頭の高さも見る（ジャンプ台の床のような薄い板は、頭の高さで張り出している）
        float height = heightPerScale * s;
        RaycastHit wall = default;
        bool blocked = false;
        foreach (float y in new[] { step + 0.01f, height * 0.5f, height * 0.9f })
            if (Cast(feet + Vector3.up * y, dir, radius + 0.06f, out wall) && wall.normal.y <= 0.7f)
            {
                blocked = true;
                break;
            }
        if (!blocked) return false;

        // 壁の少し奥を、登れる一番上から下に向かって調べ、上面を見つける
        Vector3 probe = new Vector3(wall.point.x, feet.y + maxHeight + 0.01f, wall.point.z) + dir * (radius + 0.02f);
        if (Physics.CheckSphere(probe, 0.004f, ~0, QueryTriggerInteraction.Ignore)) return false; // 高すぎる（上まで壁）
        if (!Cast(probe, Vector3.down, maxHeight, out var surface) || surface.normal.y < 0.7f) return false;
        float rise = surface.point.y - feet.y;
        if (rise < step * 0.5f) return false;

        // 上に乗ったとき、体が入るすき間があるか
        top = surface.point;
        Vector3 standAt = top + dir * radius;
        if (Overlaps(standAt + Vector3.up * (radius + 0.01f), standAt + Vector3.up * (height - radius), radius * 0.9f)) return false;
        forward = Vector3.Distance(new Vector3(feet.x, 0f, feet.z), new Vector3(standAt.x, 0f, standAt.z)) + radius;
        return true;
    }

    /// <summary>自分の体を除いて、一番近くに当たった物</summary>
    bool Cast(Vector3 origin, Vector3 dir, float distance, out RaycastHit hit)
    {
        hit = default;
        bool found = false;
        foreach (var h in Physics.RaycastAll(origin, dir, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(transform) || (found && h.distance >= hit.distance)) continue;
            hit = h;
            found = true;
        }
        return found;
    }

    bool Overlaps(Vector3 bottom, Vector3 top, float radius)
    {
        foreach (var c in Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
            if (!c.transform.IsChildOf(transform)) return true;
        return false;
    }

    /// <summary>まず真上に体を引き上げ、それから段の上へ前に出る。</summary>
    IEnumerator Climb(float rise, Vector3 forward)
    {
        climbing = true;
        Climbing?.Invoke(rise);
        body.BeginExternalMotion();
        Vector3 up = Vector3.up * (rise + 0.005f);
        float upTime = climbTime * 0.6f, forwardTime = climbTime * 0.4f;
        float done = 0f;
        for (float t = 0f; t < upTime;)
        {
            t = Mathf.Min(upTime, t + Time.deltaTime);
            float p = Mathf.SmoothStep(0f, 1f, t / upTime);
            body.MoveBy(up * (p - done));
            done = p;
            yield return null;
        }
        done = 0f;
        for (float t = 0f; t < forwardTime;)
        {
            t = Mathf.Min(forwardTime, t + Time.deltaTime);
            float p = Mathf.SmoothStep(0f, 1f, t / forwardTime);
            body.MoveBy(forward * (p - done));
            done = p;
            yield return null;
        }
        body.EndExternalMotion();
        climbing = false;
    }
}
