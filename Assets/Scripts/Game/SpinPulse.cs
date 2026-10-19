using UnityEngine;

/// <summary>回り続け、ふくらんだり縮んだりする飾り（戻る輪の光など）。</summary>
public class SpinPulse : MonoBehaviour
{
    public Vector3 spin = new Vector3(0f, 45f, 0f); // 度/秒（自分の向きで）
    public float pulseAmount = 0.05f;
    public float pulseSpeed = 3f;
    public bool pulseHeightOnly;

    Vector3 baseScale;

    void Awake() => baseScale = transform.localScale;

    void Update()
    {
        transform.Rotate(spin * Time.deltaTime, Space.Self);
        float s = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        transform.localScale = pulseHeightOnly ? new Vector3(baseScale.x, baseScale.y * s, baseScale.z) : baseScale * s;
    }
}
