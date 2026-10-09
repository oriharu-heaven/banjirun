using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 小人目線の三人称オービットカメラ。壁にめり込まないよう SphereCast で距離を詰める。
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 targetOffset = new Vector3(0f, 0.2f, 0f);
    public float distance = 0.9f;
    public float minDistance = 0.1f;
    public float collisionRadius = 0.03f;
    public float mouseSensitivity = 0.12f;
    public float stickSensitivity = 150f;
    public float minPitch = -30f;
    public float maxPitch = 70f;
    public LayerMask collisionMask = ~0;

    float yaw;
    float pitch = 15f;

    void Start()
    {
        if (target != null) yaw = target.eulerAngles.y;
        LockCursor(true);
    }

    void LateUpdate()
    {
        if (target == null) return;

        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame) LockCursor(true);

        Vector2 look = Vector2.zero;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked) look += mouse.delta.ReadValue() * mouseSensitivity;
        if (Gamepad.current != null) look += Gamepad.current.rightStick.ReadValue() * stickSensitivity * Time.deltaTime;
        yaw += look.x;
        pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + targetOffset;
        Vector3 back = rotation * Vector3.back;

        float d = distance;
        if (Physics.SphereCast(pivot, collisionRadius, back, out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore)
            && hit.transform.root != target.root)
        {
            d = Mathf.Max(minDistance, hit.distance);
        }

        transform.SetPositionAndRotation(pivot + back * d, rotation);
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
