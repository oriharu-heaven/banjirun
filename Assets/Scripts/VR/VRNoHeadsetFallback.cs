using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// VR シーンをヘッドセットなしで再生したとき（Mac のエディターなど）、キーボード・マウス（FirstPersonControls）で動けるようにする。
/// 頭と手の追跡を止め、手（コントローラー）は顔の前に構えた位置に置き、使うときは右手を伸ばす。
/// ヘッドセットがあるときは何もしない（FirstPersonControls も止めたまま）。
/// 画面で遊ぶ版は別のシーン（Flat_Prototype）。こちらは VR シーンを手早く試す・録画する用。
/// </summary>
[RequireComponent(typeof(VRPlayerBody), typeof(FirstPersonControls))]
[DefaultExecutionOrder(10)] // 頭の位置（FirstPersonControls.LateUpdate）が決まってから手を置く
public class VRNoHeadsetFallback : MonoBehaviour
{
    public Transform leftHand;
    public Transform rightHand;

    const float ReachTime = 0.5f;

    FirstPersonControls controls;
    Transform cam;
    float reachTimer;
    Vector3 reachTarget;

    void Start()
    {
        controls = GetComponent<FirstPersonControls>();
        if (XRSettings.isDeviceActive)
        {
            controls.enabled = false;
            enabled = false;
            return;
        }
        var body = GetComponent<VRPlayerBody>();
        body.KeyboardFallback = true;
        cam = body.Camera.transform;
        foreach (var driver in GetComponentsInChildren<UnityEngine.InputSystem.XR.TrackedPoseDriver>(true)) driver.enabled = false;
        controls.enabled = true;
        controls.Used += point =>
        {
            reachTimer = ReachTime;
            reachTarget = point;
        };
    }

    void LateUpdate()
    {
        reachTimer = Mathf.Max(0f, reachTimer - Time.deltaTime);
        float reachAmount = Mathf.Sin(reachTimer / ReachTime * Mathf.PI);
        Quaternion hold = cam.rotation * Quaternion.Euler(15f, 0f, 0f);
        if (leftHand != null) leftHand.SetPositionAndRotation(cam.TransformPoint(-0.2f, -0.28f, 0.38f), hold);
        if (rightHand != null)
        {
            Vector3 rest = cam.TransformPoint(0.2f, -0.28f, 0.38f);
            Vector3 target = reachTarget + (cam.position - reachTarget).normalized * 0.12f;
            rightHand.SetPositionAndRotation(Vector3.Lerp(rest, target, reachAmount), hold);
        }
    }

    void OnGUI()
    {
        if (controls == null || controls.Scripted) return;
        GUI.Label(new Rect(12, 10, 1000, 24), "VR シーン（ヘッドセットなし）: WASD 移動 / マウス 視点（クリックで固定・Esc で解除）/ Shift 走る / Space ジャンプ / E・クリック 使う");
        GUI.Label(new Rect(Screen.width / 2f - 6, Screen.height / 2f - 12, 20, 24), "+");
        if (controls.Focused != null)
            GUI.Box(new Rect(Screen.width / 2f - 120, Screen.height * 0.62f, 240, 30), $"[E] {controls.Focused.UseHint}");
    }
}
