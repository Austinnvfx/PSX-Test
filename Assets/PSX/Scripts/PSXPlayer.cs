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

    [Header("Footsteps (one per head-bob dip)")]
    public AudioSource footstepSource;
    public AudioClip[] dryFootsteps;
    [Tooltip("Same steps, wet. Blended in by PSXRain wetness.")]
    public AudioClip[] wetFootsteps;
    [Range(0f, 1f)] public float walkStepVolume = 0.25f;
    [Range(0f, 1f)] public float runStepVolume = 0.4f;
    public Vector2 stepPitch = new Vector2(0.92f, 1.06f);

    /// <summary>While true the player can't move or look (phone typing, reading, cutscenes). Gravity still applies.</summary>
    public static bool InputLocked;
    /// <summary>Settings: mouse sensitivity multiplier and inverted look.</summary>
    public static float SensitivityScale = 1f;
    public static bool InvertY;
    /// <summary>While true a menu owns the mouse: no cursor grabbing, Esc is left to the menu.</summary>
    public static bool CursorFree;
    /// <summary>Footsteps are quieter indoors (carpet, smaller room).</summary>
    [Range(0f, 1f)] public float indoorStepVolume = 0.7f;

    CharacterController controller;
    int lastStepClip = -1;
    Vector3 velocity;
    float yaw, pitch, bobPhase;
    Vector3 pivotRestPos;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        yaw = transform.eulerAngles.y;
        if (cameraPivot) pivotRestPos = cameraPivot.localPosition;
    }

    /// <summary>Set when a pause menu handles Esc (so Esc doesn't just free the mouse).</summary>
    public static bool EscapeOpensMenu;

    void Start() { if (!CursorFree) LockCursor(true); }

    /// <summary>Grab or free the mouse (menus).</summary>
    public static void SetCursorLocked(bool locked) => LockCursor(locked);

    void OnDestroy() { InputLocked = false; CursorFree = false; }

    /// <summary>Move the player instantly (during a fade), facing <paramref name="forward"/>.</summary>
    public void Teleport(Vector3 position, Vector3 forward)
    {
        if (!controller) controller = GetComponent<CharacterController>();
        controller.enabled = false;
        transform.position = position;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(forward.normalized);
            yaw = transform.eulerAngles.y;
        }
        pitch = 0f;
        if (cameraPivot) cameraPivot.localRotation = Quaternion.identity;
        velocity = Vector3.zero;
        controller.enabled = true;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        var pad = Gamepad.current;

        if (CursorFree) { if (Cursor.lockState != CursorLockMode.None) LockCursor(false); }
        else
        {
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !InputLocked && !EscapeOpensMenu) LockCursor(false);
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) LockCursor(true);
        }

        // ---- Look ----
        Vector2 look = Vector2.zero;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked && !InputLocked)
            look += mouse.delta.ReadValue() * mouseSensitivity * SensitivityScale;
        if (pad != null && !InputLocked)
            look += pad.rightStick.ReadValue() * stickSensitivity * SensitivityScale * Time.deltaTime;

        yaw += look.x;
        pitch = Mathf.Clamp(pitch - look.y * (InvertY ? -1f : 1f), -maxPitch, maxPitch);
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
        if (InputLocked) { move = Vector2.zero; run = false; }

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
            // The camera dips at phase 3π/2 (+2πk): that's a foot landing.
            float stepBefore = Mathf.Floor((bobPhase - Mathf.PI * 1.5f) / (Mathf.PI * 2f));
            bobPhase += Time.deltaTime * bobFrequency * Mathf.PI * 2f * (0.6f + moving);
            float stepAfter = Mathf.Floor((bobPhase - Mathf.PI * 1.5f) / (Mathf.PI * 2f));
            if (stepAfter != stepBefore && controller.isGrounded && moving > 0.12f)
                PlayFootstep(Mathf.InverseLerp(walkSpeed, runSpeed, horiz.magnitude));
            Vector3 bob = new Vector3(Mathf.Cos(bobPhase * 0.5f) * 0.5f, Mathf.Sin(bobPhase), 0f) * bobAmount * Mathf.Clamp01(moving * 2f);
            cameraPivot.localPosition = Vector3.Lerp(cameraPivot.localPosition, pivotRestPos + bob, 10f * Time.deltaTime);
        }
    }

    void PlayFootstep(float runAmount)
    {
        if (!footstepSource || dryFootsteps == null || dryFootsteps.Length == 0) return;

        int n = dryFootsteps.Length;
        int i = Random.Range(0, n);
        if (n > 1 && i == lastStepClip) i = (i + 1 + Random.Range(0, n - 1)) % n; // never the same twice
        lastStepClip = i;

        float volume = Mathf.Lerp(walkStepVolume, runStepVolume, runAmount) * Random.Range(0.85f, 1f)
                     * Mathf.Lerp(1f, indoorStepVolume, PSXRain.Cover);
        // Wet outside, dry under a roof.
        float wet = wetFootsteps != null && i < wetFootsteps.Length && wetFootsteps[i] ? PSXRain.Wetness * (1f - PSXRain.Cover) : 0f;

        footstepSource.pitch = Random.Range(stepPitch.x, stepPitch.y);
        if (wet < 0.999f) footstepSource.PlayOneShot(dryFootsteps[i], volume * (1f - wet));
        if (wet > 0.001f) footstepSource.PlayOneShot(wetFootsteps[i], volume * wet);
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
