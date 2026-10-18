using UnityEngine;

/// <summary>
/// 画面で遊ぶときのプレイヤーの体。CharacterController を持つ根元に付け、カメラはその子（目の高さ）。
/// 移動は FirstPersonControls、バンジーの間は BungeeJump が MoveBy で動かす。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FlatPlayerBody : PlayerBody
{
    [SerializeField] Camera head;
    CharacterController controller;

    public override Camera Camera => head;

    void Awake() => controller = GetComponent<CharacterController>();

    protected override void Teleport(Vector3 feet, float yaw)
    {
        bool wasEnabled = controller.enabled;
        controller.enabled = false;
        transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
        controller.enabled = wasEnabled;
    }

    protected override void OnLockChanged(bool locked) => controller.enabled = !locked;
}
