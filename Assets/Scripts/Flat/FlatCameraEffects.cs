using UnityEngine;

/// <summary>
/// 画面で遊ぶときだけの、落ちる迫力の演出。VR では視界を勝手に動かすと酔うので使わない。
/// ・速く落ちるほど視野（FOV）を広げる
/// ・ロープがピンと張った瞬間と、クッションに着地したときに画面を揺らす
/// ・ドッキリモードで板がきしんだ・折れた瞬間に画面を揺らす
/// </summary>
[RequireComponent(typeof(PlayerBody))]
[DefaultExecutionOrder(20)] // FirstPersonControls が頭の向きを決めた後に揺らす
public class FlatCameraEffects : MonoBehaviour
{
    public float baseFov = 70f;
    public float maxExtraFov = 18f;
    public float speedForMaxFov = 14f;

    PlayerBody body;
    BungeeJump bungee;
    Camera cam;
    float shake;
    float shakeTime;

    void Start()
    {
        body = GetComponent<PlayerBody>();
        cam = body.Camera;
        cam.fieldOfView = baseFov;
        bungee = FindFirstObjectByType<BungeeJump>();
        if (bungee == null) return;
        bungee.CordSnapped += strength => Shake(0.6f + strength);
        bungee.PlankCracked += strength => Shake(0.5f + strength * 0.8f);
        bungee.PhaseChanged += phase =>
        {
            if (phase == BungeeJump.Phase.Landed) Shake(0.7f);
        };
    }

    void Shake(float amount) => shake = Mathf.Max(shake, amount);

    void LateUpdate()
    {
        float speed = body.Locked ? body.Velocity.magnitude : 0f;
        float targetFov = baseFov + maxExtraFov * Mathf.Clamp01(speed / speedForMaxFov);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-4f * Time.deltaTime));

        if (shake <= 0.001f) return;
        shakeTime += Time.deltaTime;
        float s = shake * shake;
        var offset = new Vector3(
            (Mathf.PerlinNoise(shakeTime * 25f, 0f) - 0.5f) * 6f * s,
            (Mathf.PerlinNoise(0f, shakeTime * 25f) - 0.5f) * 6f * s,
            (Mathf.PerlinNoise(shakeTime * 25f, shakeTime * 25f) - 0.5f) * 4f * s);
        cam.transform.localRotation *= Quaternion.Euler(offset);
        shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 1.6f);
    }
}
