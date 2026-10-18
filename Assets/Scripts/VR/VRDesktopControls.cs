using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// ヘッドセットが無いとき（Mac のエディターなど）に、キーボードとマウスで VR プレイヤーを自由に動かす。XR Origin に付ける。
/// WASD/矢印: 移動, マウス: 視点（クリックでカーソル固定 / Esc で解除）, Shift: 走る, Space: ジャンプ, E・左クリック: 使う
/// 自動操作（デモの録画）は Scripted を true にして ScriptedCommands を毎フレーム入れる。
/// </summary>
[RequireComponent(typeof(VRPlayerBody), typeof(CharacterController))]
public class VRDesktopControls : MonoBehaviour
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
    public float reach = 2.5f;

    [Header("手（コントローラー）の置き場所")]
    public Transform leftHand;
    public Transform rightHand;

    [System.NonSerialized] public bool Scripted;
    [System.NonSerialized] public Commands ScriptedCommands;

    const float ReachTime = 0.5f;

    VRPlayerBody body;
    CharacterController controller;
    Transform cam;
    float pitch;
    float verticalVelocity;
    float reachTimer;
    Vector3 reachTarget;
    IVRUsable focused;
    Vector3 focusPoint;

    public IVRUsable Focused => focused;

    void Start()
    {
        // ヘッドセットで遊んでいるときは XRI のスティック移動に任せる
        if (XRSettings.isDeviceActive)
        {
            enabled = false;
            return;
        }
        body = GetComponent<VRPlayerBody>();
        controller = GetComponent<CharacterController>();
        cam = body.Camera.transform;
        body.Desktop = true;
        foreach (var driver in GetComponentsInChildren<UnityEngine.InputSystem.XR.TrackedPoseDriver>(true)) driver.enabled = false;
        cam.localRotation = Quaternion.identity;
    }

    void Update()
    {
        Commands c = Scripted ? ScriptedCommands : ReadKeyboardMouse();
        Look(c.look);
        UpdateFocus();
        if (c.use && focused != null)
        {
            focused.Use(cam.position);
            reachTimer = ReachTime;
            reachTarget = focusPoint;
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
        // 頭は体の真上、目の高さに置く（ヘッドセットの代わり）
        cam.position = transform.position + transform.up * eyeHeight;
        PlaceHands(Time.deltaTime);
    }

    Commands ReadKeyboardMouse()
    {
        var c = new Commands();
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null) return c;

        if (keyboard.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
        bool clickToLock = mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked;
        if (clickToLock) SetCursorLocked(true);

        float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
        float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
        c.move = new Vector2(x, y);
        c.run = keyboard.leftShiftKey.isPressed;
        c.jump = keyboard.spaceKey.wasPressedThisFrame;
        c.use = keyboard.eKey.wasPressedThisFrame || (mouse != null && mouse.leftButton.wasPressedThisFrame && !clickToLock);
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked) c.look = mouse.delta.ReadValue() * mouseSensitivity;
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
            focused = hit.collider.GetComponentInParent<IVRUsable>();
            focusPoint = hit.point;
            break;
        }
    }

    void PlaceHands(float dt)
    {
        // ヘッドセットが無いので、手は顔の前に構えた位置に置く。使うときは右手を伸ばす
        reachTimer = Mathf.Max(0f, reachTimer - dt);
        float reachAmount = Mathf.Sin(reachTimer / ReachTime * Mathf.PI);
        if (leftHand != null) leftHand.SetPositionAndRotation(cam.TransformPoint(-0.2f, -0.28f, 0.38f), cam.rotation * Quaternion.Euler(15f, 0f, 0f));
        if (rightHand != null)
        {
            Vector3 rest = cam.TransformPoint(0.2f, -0.28f, 0.38f);
            Vector3 target = reachTarget + (cam.position - reachTarget).normalized * 0.12f;
            rightHand.SetPositionAndRotation(Vector3.Lerp(rest, target, reachAmount), cam.rotation * Quaternion.Euler(15f, 0f, 0f));
        }
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void OnGUI()
    {
        if (Scripted) return;
        GUI.Label(new Rect(12, 10, 900, 24), "WASD: 移動   マウス: 視点（クリックで固定 / Esc で解除）   Shift: 走る   Space: ジャンプ   E・クリック: 使う");
        GUI.Label(new Rect(Screen.width / 2f - 6, Screen.height / 2f - 12, 20, 24), "+");
        if (focused != null)
        {
            var style = new GUIStyle(GUI.skin.box) { fontSize = 20 };
            GUI.Box(new Rect(Screen.width / 2f - 120, Screen.height * 0.62f, 240, 36), $"[E] {focused.UseHint}", style);
        }
    }
}
