using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Looks for <see cref="Interactable"/>s in front of the camera, shows their tooltip, uses them on E / (A).</summary>
public class PlayerInteractor : MonoBehaviour
{
    /// <summary>Set while something else owns the screen (phone, a document, a cutscene).</summary>
    public static bool Blocked;
    public LayerMask mask = ~(1 << 2);
    public float reach = 3.2f;

    public Interactable Current { get; private set; }

    void OnDestroy() => Blocked = false;

    void Update()
    {
        Current = null;
        var hud = EvictionHud.Instance;
        if (Blocked || PSXPlayer.InputLocked || !hud) return;

        if (!Physics.Raycast(transform.position, transform.forward, out var hit, reach, mask, QueryTriggerInteraction.Collide)) return;
        var it = hit.collider.GetComponentInParent<Interactable>();
        if (!it || !it.isActiveAndEnabled || hit.distance > it.maxDistance) return;

        if (!it.available)
        {
            if (!string.IsNullOrEmpty(it.lockedText)) hud.SetTooltip(it.lockedText);
            return;
        }
        Current = it;
        hud.SetTooltip(EvictionHud.Key("E", "A") + " " + it.prompt);

        var kb = Keyboard.current;
        var pad = Gamepad.current;
        if ((kb != null && kb.eKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame))
            it.Use();
    }
}
