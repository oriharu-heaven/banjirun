using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

/// <summary>
/// VR シーンをヘッドセットなしで再生したとき（Mac のエディターなど）、キーボード・マウス（FirstPersonControls）で動けるようにする。
/// 頭と手の追跡を止め、手（コントローラー）は顔の前に構えた位置に置き、使うときは右手を伸ばす。
/// ヘッドセットがあるときは何もしない（FirstPersonControls も止めたまま）。
/// 「ヘッドセットがある」は、XR が動いている（isDeviceActive）か、XR の読み込み（OpenXR の loader）が動いているかで決める。
/// Quest では起動に時間がかかると、最初のフレームではまだ isDeviceActive が false のことがあり、
/// それだけで決めると、Quest なのにスティック移動と頭・手の追跡を止めてしまっていた（2026-10-20「VR 版が動かせない」）。
/// 念のため、キーボード操作に切り替えた後でヘッドセットが動き出したら、元に戻す。
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
    VRPlayerBody body;
    Transform cam;
    float reachTimer;
    Vector3 reachTarget;
    readonly List<UnityEngine.InputSystem.XR.TrackedPoseDriver> drivers = new List<UnityEngine.InputSystem.XR.TrackedPoseDriver>();

    /// <summary>XR の読み込み（OpenXR など）が動いている。Quest ではいつも true、Mac のエディターでは false</summary>
    static bool XRLoaderActive => XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null && XRGeneralSettings.Instance.Manager.activeLoader != null;

    void Start()
    {
        controls = GetComponent<FirstPersonControls>();
        body = GetComponent<VRPlayerBody>();
        bool headset = XRSettings.isDeviceActive || XRLoaderActive;
        Debug.Log($"[VR] ヘッドセット: {(headset ? "あり" : "なし（キーボード操作にする）")}（isDeviceActive={XRSettings.isDeviceActive}, loader={(XRLoaderActive ? XRGeneralSettings.Instance.Manager.activeLoader.name : "なし")}）");
        if (headset)
        {
            controls.enabled = false;
            enabled = false;
            return;
        }
        body.KeyboardFallback = true;
        cam = body.Camera.transform;
        GetComponentsInChildren(true, drivers);
        foreach (var driver in drivers) driver.enabled = false;
        controls.enabled = true;
        controls.Used += point =>
        {
            reachTimer = ReachTime;
            reachTarget = point;
        };
    }

    void Update()
    {
        // キーボード操作に切り替えた後でヘッドセットが動き出したら、スティック移動と頭・手の追跡に戻す
        if (!XRSettings.isDeviceActive) return;
        Debug.Log("[VR] ヘッドセットが動き出したので、キーボード操作をやめる");
        foreach (var driver in drivers) if (driver != null) driver.enabled = true;
        controls.enabled = false;
        body.KeyboardFallback = false;
        enabled = false;
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
        GUI.Label(new Rect(12, 10, 1000, 24), "VR シーン（ヘッドセットなし）: WASD 移動 / マウス 視点（クリックで固定・Esc で解除）/ Shift 走る / Space ジャンプ / E・クリック 使う / M ミニキャラ");
        GUI.Label(new Rect(Screen.width / 2f - 6, Screen.height / 2f - 12, 20, 24), "+");
        if (controls.Focused != null)
            GUI.Box(new Rect(Screen.width / 2f - 120, Screen.height * 0.62f, 240, 30), $"[E] {controls.Focused.UseHint}");
    }
}
