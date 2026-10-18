using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

/// <summary>
/// XR Origin に付ける、プレイヤーの体の窓口。足の位置・速さを返し、バンジーの間は移動を止めて外から動かせるようにする。
/// 移動の担当は、VR では XRI の移動（スティック・テレポート・重力）、パソコンでは VRDesktopControls。
/// </summary>
[RequireComponent(typeof(XROrigin))]
public class VRPlayerBody : MonoBehaviour
{
    XROrigin origin;
    CharacterController controller;
    readonly List<LocomotionProvider> providers = new List<LocomotionProvider>();
    readonly List<bool> providerWasEnabled = new List<bool>();
    Vector3 lastFeet;
    Vector3 velocity;
    bool desktop;

    public Vector3 Feet
    {
        get
        {
            Vector3 head = origin.Camera.transform.position;
            return new Vector3(head.x, transform.position.y, head.z);
        }
    }

    public Vector3 Head => origin.Camera.transform.position;
    public Camera Camera => origin.Camera;
    public Vector3 Velocity => velocity;
    public bool Locked { get; private set; }

    /// <summary>パソコン操作のとき true。XRI の移動は止め、VRDesktopControls が動かす。</summary>
    public bool Desktop
    {
        get => desktop;
        set { desktop = value; Apply(); }
    }

    void Awake()
    {
        origin = GetComponent<XROrigin>();
        controller = GetComponent<CharacterController>();
        GetComponentsInChildren(true, providers);
        foreach (var provider in providers) providerWasEnabled.Add(provider.enabled);
        lastFeet = Feet;
    }

    void Update()
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
        Apply();
    }

    public void MoveBy(Vector3 delta) => transform.position += delta;

    public void EndExternalMotion()
    {
        Locked = false;
        Apply();
    }

    /// <summary>足が feet に来るように移す。yaw は向く方角（度）。</summary>
    public void TeleportFeetTo(Vector3 feet, float yaw)
    {
        bool wasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        float turn = Mathf.DeltaAngle(origin.Camera.transform.eulerAngles.y, yaw);
        origin.RotateAroundCameraUsingOriginUp(turn);
        origin.MoveCameraToWorldLocation(feet + Vector3.up * origin.CameraInOriginSpaceHeight);
        lastFeet = Feet;
        velocity = Vector3.zero;
        if (controller != null) controller.enabled = wasEnabled;
    }

    void Apply()
    {
        if (controller != null) controller.enabled = !Locked;
        for (int i = 0; i < providers.Count; i++)
            if (providers[i] != null) providers[i].enabled = providerWasEnabled[i] && !Locked && !desktop;
    }
}
