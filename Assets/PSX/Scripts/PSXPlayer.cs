using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple first-person walker. WASD / left stick to move, mouse / right stick to look, Shift to run.
/// Click to lock the cursor, Esc to release it.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PSXPlayer : MonoBehaviour
{
    public Transform cameraPivot;

    [Header("Movement")]
    public float walkSpeed = 2.2f;
    public float runSpeed = 4.5f;
    public float acceleration = 12f;
    public float gravity = -18f;

    [Header("Look")]
    public float mouseSensitivity = 0.08f;
    public float stickSensitivity = 140f;
    public float maxPitch = 85f;

    [Header("Head bob")]
    public float bobAmount = 0.035f;
    public float bobFrequency = 1.9f;

    CharacterController controller;
    Vector3 velocity;
    float yaw, pitch, bobPhase;
    Vector3 pivotRestPos;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        yaw = transform.eulerAngles.y;
        if (cameraPivot) pivotRestPos = cameraPivot.localPosition;
    }

    void Start() => LockCursor(true);

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        var pad = Gamepad.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame) LockCursor(true);

        // ---- Look ----
        Vector2 look = Vector2.zero;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            look += mouse.delta.ReadValue() * mouseSensitivity;
        if (pad != null)
            look += pad.rightStick.ReadValue() * stickSensitivity * Time.deltaTime;

        yaw += look.x;
        pitch = Mathf.Clamp(pitch - look.y, -maxPitch, maxPitch);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // ---- Move ----
        Vector2 move = Vector2.zero;
        bool run = false;
        if (kb != null)
        {
            if (kb.wKey.isPressed) move.y += 1;
            if (kb.sKey.isPressed) move.y -= 1;
            if (kb.dKey.isPressed) move.x += 1;
            if (kb.aKey.isPressed) move.x -= 1;
            run |= kb.leftShiftKey.isPressed;
        }
        if (pad != null)
        {
            move += pad.leftStick.ReadValue();
            run |= pad.leftStickButton.isPressed;
        }
        move = Vector2.ClampMagnitude(move, 1f);

        float speed = run ? runSpeed : walkSpeed;
        Vector3 target = (transform.right * move.x + transform.forward * move.y) * speed;
        Vector3 horiz = new Vector3(velocity.x, 0f, velocity.z);
        horiz = Vector3.MoveTowards(horiz, target, acceleration * Time.deltaTime);

        if (controller.isGrounded && velocity.y < 0f) velocity.y = -2f;
        velocity.y += gravity * Time.deltaTime;
        velocity = new Vector3(horiz.x, velocity.y, horiz.z);
        controller.Move(velocity * Time.deltaTime);

        // ---- Head bob ----
        if (cameraPivot)
        {
            float moving = horiz.magnitude / Mathf.Max(0.01f, runSpeed);
            bobPhase += Time.deltaTime * bobFrequency * Mathf.PI * 2f * (0.6f + moving);
            Vector3 bob = new Vector3(Mathf.Cos(bobPhase * 0.5f) * 0.5f, Mathf.Sin(bobPhase), 0f) * bobAmount * Mathf.Clamp01(moving * 2f);
            cameraPivot.localPosition = Vector3.Lerp(cameraPivot.localPosition, pivotRestPos + bob, 10f * Time.deltaTime);
        }
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
