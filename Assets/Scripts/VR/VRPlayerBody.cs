using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

/// <summary>
/// VR のプレイヤーの体（XR Origin に付ける）。移動は XRI（スティック・テレポート・重力）が担当し、
/// バンジーの間は止める。ヘッドセットが無いとき（KeyboardFallback）は XRI の移動を止め、FirstPersonControls が動かす。
/// ミニキャラ: XR Origin ごと縮める（頭の高さも手の動きも、自分の体の動きがそのまま小さくなる）。B・Y ボタンで切り替え。
/// </summary>
[RequireComponent(typeof(XROrigin))]
public class VRPlayerBody : PlayerBody
{
    XROrigin origin;
    CharacterController controller;
    readonly List<LocomotionProvider> providers = new List<LocomotionProvider>();
    readonly List<bool> providerWasEnabled = new List<bool>();
    readonly List<ContinuousMoveProvider> moves = new List<ContinuousMoveProvider>();
    readonly List<float> moveSpeeds = new List<float>();
    float stepOffset, skinWidth;
    bool keyboardFallback;
    InputAction resizeAction;

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
        GetComponentsInChildren(true, moves);
        foreach (var move in moves) moveSpeeds.Add(move.moveSpeed);
        if (controller != null)
        {
            stepOffset = controller.stepOffset;
            skinWidth = controller.skinWidth;
        }
        // 右手の B・左手の Y でミニキャラ ⇔ 等身大
        resizeAction = new InputAction("Resize", InputActionType.Button);
        resizeAction.AddBinding("<XRController>{RightHand}/secondaryButton");
        resizeAction.AddBinding("<XRController>{LeftHand}/secondaryButton");
    }

    void OnEnable() => resizeAction.Enable();
    void OnDisable() => resizeAction.Disable();
    void OnDestroy() => resizeAction.Dispose();

    protected override void Update()
    {
        if (!keyboardFallback)
        {
            if (resizeAction.WasPressedThisFrame()) ToggleMini();
            MoveIntent = Locked ? Vector3.zero : StickDirection();
        }
        base.Update();
    }

    /// <summary>スティックで歩こうとしている向き（XRI の移動と同じく、頭の向きが前）</summary>
    Vector3 StickDirection()
    {
        foreach (var move in moves)
        {
            if (move == null || !move.isActiveAndEnabled) continue;
            Vector2 input = Vector2.ClampMagnitude(move.leftHandMoveInput.ReadValue() + move.rightHandMoveInput.ReadValue(), 1f);
            if (input.sqrMagnitude < 0.01f) continue;
            Transform forward = move.forwardSource != null ? move.forwardSource : origin.Camera.transform;
            Vector3 f = Vector3.ProjectOnPlane(forward.forward, Vector3.up).normalized;
            Vector3 r = Vector3.Cross(Vector3.up, f);
            return f * input.y + r * input.x;
        }
        return Vector3.zero;
    }

    protected override void Teleport(Vector3 feet, float yaw)
    {
        bool wasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        float turn = Mathf.DeltaAngle(origin.Camera.transform.eulerAngles.y, yaw);
        origin.RotateAroundCameraUsingOriginUp(turn);
        origin.MoveCameraToWorldLocation(feet + Vector3.up * (origin.CameraInOriginSpaceHeight * transform.lossyScale.y));
        if (controller != null) controller.enabled = wasEnabled;
    }

    protected override void OnLockChanged(bool locked) => Apply();

    protected override void ApplyScale(float scale)
    {
        transform.localScale = Vector3.one * scale;
        // XRI はスティックの移動の速さに XR Origin の大きさを掛ける。そのままだとミニでは遅すぎるので、大きさの平方根になるよう戻す
        for (int i = 0; i < moves.Count; i++)
            if (moves[i] != null) moves[i].moveSpeed = moveSpeeds[i] / Mathf.Sqrt(scale);
        if (controller != null)
        {
            // 体の高さ・太さは XR Origin の大きさで一緒に縮む。段を上がる高さとすき間は縮まないので、ここで縮める
            controller.stepOffset = stepOffset * scale;
            controller.skinWidth = skinWidth * scale;
        }
    }

    void Apply()
    {
        if (controller != null) controller.enabled = !Locked;
        for (int i = 0; i < providers.Count; i++)
            if (providers[i] != null) providers[i].enabled = providerWasEnabled[i] && !Locked && !keyboardFallback;
    }
}
