using UnityEngine;

/// <summary>
/// 画面で遊ぶときのプレイヤーの体。CharacterController を持つ根元に付け、カメラはその子（目の高さ）。
/// 移動は FirstPersonControls、バンジーの間は BungeeJump が MoveBy で動かす。
/// 体の大きさを変えるときは、CharacterController の寸法とカメラの近くの描画範囲を縮める（Transform の拡大縮小は使わない）。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FlatPlayerBody : PlayerBody
{
    [SerializeField] Camera head;
    CharacterController controller;
    // 等身大のときの体（FlatSceneBuilder が決めた値）
    float height, radius, skinWidth, stepOffset, nearClip;

    public override Camera Camera => head;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        height = controller.height;
        radius = controller.radius;
        skinWidth = controller.skinWidth;
        stepOffset = controller.stepOffset;
        nearClip = head.nearClipPlane;
    }

    protected override void Teleport(Vector3 feet, float yaw)
    {
        bool wasEnabled = controller.enabled;
        controller.enabled = false;
        transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
        controller.enabled = wasEnabled;
    }

    protected override void OnLockChanged(bool locked) => controller.enabled = !locked;

    protected override void ApplyScale(float scale)
    {
        controller.height = height * scale;
        controller.radius = radius * scale;
        controller.skinWidth = skinWidth * scale;
        controller.stepOffset = stepOffset * scale;
        controller.center = new Vector3(0f, controller.height / 2f + controller.skinWidth, 0f);
        head.nearClipPlane = Mathf.Max(nearClip * scale, 0.005f);
    }
}
