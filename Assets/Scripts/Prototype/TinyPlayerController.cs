using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 小人サイズのプレイヤー操作（移動・ダッシュ・ジャンプ・ドア操作）。
/// WASD/左スティック: 移動, Shift/LB: ダッシュ, Space/A: ジャンプ, E/X: ドア開閉
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class TinyPlayerController : MonoBehaviour
{
    [Header("Movement (m/s)")]
    public float walkSpeed = 0.8f;
    public float runSpeed = 1.8f;
    public float acceleration = 8f;
    public float turnSpeed = 720f;

    [Header("Jump")]
    public float jumpHeight = 0.35f;
    public float gravity = -9.81f;
    public float coyoteTime = 0.1f;
    public float jumpBufferTime = 0.12f;

    [Header("Interact")]
    public float interactRadius = 0.6f;

    [Header("References")]
    public Transform cameraTransform;

    CharacterController controller;
    InputAction moveAction, jumpAction, sprintAction, interactAction;
    Vector3 planarVelocity;
    float verticalVelocity;
    float lastGroundedTime = float.NegativeInfinity;
    float lastJumpPressedTime = float.NegativeInfinity;
    Door nearbyDoor;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

        moveAction = new InputAction("Move");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
        moveAction.AddBinding("<Gamepad>/leftStick");

        jumpAction = new InputAction("Jump", binding: "<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");

        sprintAction = new InputAction("Sprint", binding: "<Keyboard>/leftShift");
        sprintAction.AddBinding("<Gamepad>/leftShoulder");

        interactAction = new InputAction("Interact", binding: "<Keyboard>/e");
        interactAction.AddBinding("<Gamepad>/buttonWest");
    }

    void OnEnable()
    {
        moveAction.Enable(); jumpAction.Enable(); sprintAction.Enable(); interactAction.Enable();
    }

    void OnDisable()
    {
        moveAction.Disable(); jumpAction.Disable(); sprintAction.Disable(); interactAction.Disable();
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (controller.isGrounded)
        {
            lastGroundedTime = Time.time;
            if (verticalVelocity < 0f) verticalVelocity = -1f;
        }
        if (jumpAction.WasPressedThisFrame()) lastJumpPressedTime = Time.time;

        // カメラ基準の移動方向
        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 forward = Vector3.forward, right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }
        Vector3 wishDir = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
        float speed = sprintAction.IsPressed() ? runSpeed : walkSpeed;
        planarVelocity = Vector3.MoveTowards(planarVelocity, wishDir * speed, acceleration * speed * dt);

        if (wishDir.sqrMagnitude > 0.0001f)
        {
            Quaternion target = Quaternion.LookRotation(wishDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }

        // ジャンプ（コヨーテタイム + 入力バッファ）
        if (Time.time - lastJumpPressedTime <= jumpBufferTime && Time.time - lastGroundedTime <= coyoteTime)
        {
            verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            lastJumpPressedTime = float.NegativeInfinity;
            lastGroundedTime = float.NegativeInfinity;
        }
        verticalVelocity += gravity * dt;

        CollisionFlags flags = controller.Move((planarVelocity + Vector3.up * verticalVelocity) * dt);
        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;

        nearbyDoor = FindNearbyDoor();
        if (nearbyDoor != null && interactAction.WasPressedThisFrame()) nearbyDoor.Toggle(transform.position);
    }

    Door FindNearbyDoor()
    {
        Vector3 center = transform.position + Vector3.up * controller.height * 0.5f;
        Collider[] hits = Physics.OverlapSphere(center, interactRadius, ~0, QueryTriggerInteraction.Collide);
        Door best = null;
        float bestDist = float.MaxValue;
        foreach (Collider hit in hits)
        {
            Door door = hit.GetComponentInParent<Door>();
            if (door == null) continue;
            float d = (hit.ClosestPoint(center) - center).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = door; }
        }
        return best;
    }

    void OnGUI()
    {
        GUI.Label(new Rect(12, 10, 600, 24), "WASD: Move   Shift: Run   Space: Jump   E: Door   Mouse: Camera   Esc: Cursor");
        if (nearbyDoor != null)
        {
            var style = new GUIStyle(GUI.skin.box) { fontSize = 20 };
            string text = nearbyDoor.IsOpen ? "[E] Close" : "[E] Open";
            GUI.Box(new Rect(Screen.width / 2f - 80, Screen.height * 0.7f, 160, 36), text, style);
        }
    }
}
