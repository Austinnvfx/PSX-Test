using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Something the player can look at and use. Shows "[E] prompt" when in reach, or
/// <see cref="lockedText"/> (no key) while it isn't available yet. Needs a collider on it or a child.
/// </summary>
public class Interactable : MonoBehaviour
{
    [Tooltip("Shown after the key, e.g. \"Clock out\" becomes \"[E] Clock out\".")]
    public string prompt = "Use";
    [Tooltip("Shown instead while not available, e.g. \"Should clock out first.\" Leave empty to show nothing.")]
    public string lockedText;
    public bool available = true;
    public float maxDistance = 2.3f;
    public UnityEvent onUse;
    public event Action Used;

    public void Use()
    {
        Used?.Invoke();
        onUse?.Invoke();
    }
}
