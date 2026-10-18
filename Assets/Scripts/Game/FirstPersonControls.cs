using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 画面で遊ぶときの一人称の操作（キーボード・マウス・ゲームパッド）。PlayerBody と同じ GameObject に付ける。
/// 画面版（FlatPlayerBody）ではいつも使い、VR 版ではヘッドセットが無いときだけ VRNoHeadsetFallback が有効にする。
/// キーボード: WASD/矢印 移動, マウス 視点（クリックでカーソル固定 / Esc で解除）, Shift 走る, Space ジャンプ, E・左クリック 使う
/// ゲームパッド: 左スティック 移動（押し込みで走る）, 右スティック 視点, A ジャンプ, X・右トリガー 使う
/// 自動操作（デモの録画）は Scripted を true にして ScriptedCommands を毎フレーム入れる。
/// </summary>
[RequireComponent(typeof(PlayerBody), typeof(CharacterController))]
public class FirstPersonControls : MonoBehaviour
{
    public struct Commands
    {
        public Vector2 move;  // -1〜1
        public Vector2 look;  // このフレームで回す角度（度）。x: 左右, y: 上下
        public bool run, jump, use;
    }

    public float walkSpeed = 1.6f;
    public float runSpeed = 3.2f;
    public float jumpHeight = 0.5f;
    public float gravity = -9.81f;
    public float eyeHeight = 1.6f;
    public float mouseSensitivity = 0.1f;
    public float stickLookSpeed = 140f;  // 度/秒
    public float reach = 2.5f;

    [System.NonSerialized] public bool Scripted;
    [System.NonSerialized] public Commands ScriptedCommands;

    /// <summary>何かを使ったとき（使った物の当たった位置）。VR の手を伸ばす動きなどに使う。</summary>
    public event System.Action<Vector3> Used;

    PlayerBody body;
    CharacterController controller;
    Transform cam;
    float pitch;
    float verticalVelocity;
    IUsable focused;
    Vector3 focusPoint;

    public IUsable Focused => focused;
    public bool CursorLocked => Cursor.lockState == CursorLockMode.Locked;

    void Awake()
    {
        body = GetComponent<PlayerBody>();
        controller = GetComponent<CharacterController>();
    }

    void Start()
    {
        cam = body.Camera.transform;
        cam.localRotation = Quaternion.identity;
    }

    void Update()
    {
        Commands c = Scripted ? ScriptedCommands : ReadDevices();
        Look(c.look);
        UpdateFocus();
        if (c.use && focused != null)
        {
            focused.Use(cam.position);
            Used?.Invoke(focusPoint);
        }
        if (body.Locked)
        {
            verticalVelocity = 0f;
            return;
        }
        Move(c, Time.deltaTime);
    }

    void LateUpdate()
    {
        // 頭は体の真上、目の高さに置く
        cam.position = transform.position + transform.up * eyeHeight;
    }

    Commands ReadDevices()
    {
        var c = new Commands();
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        var pad = Gamepad.current;

        if (keyboard != null)
        {
            if (keyboard.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            c.move = new Vector2(x, y);
            c.run = keyboard.leftShiftKey.isPressed;
            c.jump = keyboard.spaceKey.wasPressedThisFrame;
            c.use = keyboard.eKey.wasPressedThisFrame;
        }
        if (mouse != null)
        {
            bool clickToLock = mouse.leftButton.wasPressedThisFrame && !CursorLocked;
            if (clickToLock) SetCursorLocked(true);
            c.use |= mouse.leftButton.wasPressedThisFrame && !clickToLock;
            if (CursorLocked) c.look += mouse.delta.ReadValue() * mouseSensitivity;
        }
        if (pad != null)
        {
            Vector2 stick = pad.leftStick.ReadValue();
            if (stick.sqrMagnitude > c.move.sqrMagnitude) c.move = stick;
            c.look += pad.rightStick.ReadValue() * stickLookSpeed * Time.deltaTime;
            c.run |= pad.leftStickButton.isPressed;
            c.jump |= pad.buttonSouth.wasPressedThisFrame;
            c.use |= pad.buttonWest.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame;
        }
        return c;
    }

    void Look(Vector2 look)
    {
        // 左右は体ごと回し、上下は頭だけ傾ける
        transform.RotateAround(cam.position, Vector3.up, look.x);
        pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
        cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void Move(Commands c, float dt)
    {
        Vector3 wish = Vector3.ClampMagnitude(transform.forward * c.move.y + transform.right * c.move.x, 1f) * (c.run ? runSpeed : walkSpeed);
        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -1f;
            if (c.jump) verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
        }
        verticalVelocity += gravity * dt;
        controller.Move((wish + Vector3.up * verticalVelocity) * dt);
    }

    void UpdateFocus()
    {
        focused = null;
        var hits = Physics.RaycastAll(cam.position, cam.forward, reach, ~0, QueryTriggerInteraction.Collide);
        foreach (var hit in hits.OrderBy(h => h.distance))
        {
            if (hit.transform.IsChildOf(transform)) continue;
            focused = hit.collider.GetComponentInParent<IUsable>();
            focusPoint = hit.point;
            break;
        }
    }

    public static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
