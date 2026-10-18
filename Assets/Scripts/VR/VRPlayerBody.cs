using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

/// <summary>
/// VR のプレイヤーの体（XR Origin に付ける）。移動は XRI（スティック・テレポート・重力）が担当し、
/// バンジーの間は止める。ヘッドセットが無いとき（KeyboardFallback）は XRI の移動を止め、FirstPersonControls が動かす。
/// </summary>
[RequireComponent(typeof(XROrigin))]
public class VRPlayerBody : PlayerBody
{
    XROrigin origin;
    CharacterController controller;
    readonly List<LocomotionProvider> providers = new List<LocomotionProvider>();
    readonly List<bool> providerWasEnabled = new List<bool>();
    bool keyboardFallback;

    public override Camera Camera => origin.Camera;

    /// <summary>ヘッドセットが無いとき true。XRI の移動を止め、キーボード・マウスで動かす。</summary>
    public bool KeyboardFallback
    {
        get => keyboardFallback;
        set { keyboardFallback = value; Apply(); }
    }

    void Awake()
    {
        origin = GetComponent<XROrigin>();
        controller = GetComponent<CharacterController>();
        GetComponentsInChildren(true, providers);
        foreach (var provider in providers) providerWasEnabled.Add(provider.enabled);
    }

    protected override void Teleport(Vector3 feet, float yaw)
    {
        bool wasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        float turn = Mathf.DeltaAngle(origin.Camera.transform.eulerAngles.y, yaw);
        origin.RotateAroundCameraUsingOriginUp(turn);
        origin.MoveCameraToWorldLocation(feet + Vector3.up * origin.CameraInOriginSpaceHeight);
        if (controller != null) controller.enabled = wasEnabled;
    }

    protected override void OnLockChanged(bool locked) => Apply();

    void Apply()
    {
        if (controller != null) controller.enabled = !Locked;
        for (int i = 0; i < providers.Count; i++)
            if (providers[i] != null) providers[i].enabled = providerWasEnabled[i] && !Locked && !keyboardFallback;
    }
}
