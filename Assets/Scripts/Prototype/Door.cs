using UnityEngine;

/// <summary>
/// ヒンジ位置に置いたピボットに付ける開き戸。子のドアパーツごと鉛直軸で回転する。
/// プレイヤーから遠ざかる向きに開く。
/// </summary>
public class Door : MonoBehaviour
{
    public float openAngle = 95f;
    public float swingSpeed = 160f; // deg/sec

    Quaternion closedRotation;
    Vector3 leafDirection; // ヒンジ→ドア中央（閉状態, 水平）
    float currentAngle;
    float targetAngle;

    public bool IsOpen => !Mathf.Approximately(targetAngle, 0f);

    void Awake()
    {
        closedRotation = transform.rotation;
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            leafDirection = Vector3.ProjectOnPlane(b.center - transform.position, Vector3.up).normalized;
        }
    }

    public void Toggle(Vector3 actorPosition)
    {
        if (IsOpen)
        {
            targetAngle = 0f;
            return;
        }
        // Y軸の正回転でドア先端は Cross(up, leafDirection) 方向へ動く。人のいない側へ開く。
        Vector3 swingDir = Vector3.Cross(Vector3.up, leafDirection);
        Vector3 toActor = actorPosition - transform.position;
        targetAngle = Vector3.Dot(swingDir, toActor) > 0f ? -openAngle : openAngle;
    }

    void Update()
    {
        if (Mathf.Approximately(currentAngle, targetAngle)) return;
        currentAngle = Mathf.MoveTowards(currentAngle, targetAngle, swingSpeed * Time.deltaTime);
        transform.rotation = Quaternion.AngleAxis(currentAngle, Vector3.up) * closedRotation;
    }
}
