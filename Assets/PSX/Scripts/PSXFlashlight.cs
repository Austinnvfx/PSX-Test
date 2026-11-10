using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shader-driven flashlight for PSX/Lit materials. Lags behind the camera for a hand-held feel,
/// flickers occasionally, and toggles with F (or gamepad north button).
/// </summary>
[ExecuteAlways]
public class PSXFlashlight : MonoBehaviour
{
    public bool isOn = true;
    public Color color = new Color(1f, 0.93f, 0.78f);
    [Range(0f, 8f)] public float intensity = 2.2f;
    public float range = 18f;
    [Range(1f, 90f)] public float outerAngle = 38f;
    [Range(1f, 90f)] public float innerAngle = 22f;

    [Header("Hand-held feel")]
    [Tooltip("Transform the light follows (usually the camera). Rotation lags behind it.")]
    public Transform follow;
    public float lagSpeed = 10f;

    [Header("Flicker")]
    public bool flicker = true;
    [Tooltip("Average seconds between flicker bursts.")]
    public float flickerInterval = 9f;

    [Header("Audio (optional)")]
    public AudioSource clickSource;

    Quaternion smoothRot;
    float nextFlicker;
    float flickerEnd;

    void OnEnable()
    {
        smoothRot = follow ? follow.rotation : transform.rotation;
        nextFlicker = Time.time + Random.Range(flickerInterval * 0.5f, flickerInterval * 1.5f);
    }

    void OnDisable()
    {
        Shader.SetGlobalVector("_PSXFlashPos", Vector4.zero);
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        var kb = Keyboard.current;
        var pad = Gamepad.current;
        if ((kb != null && kb.fKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame))
        {
            isOn = !isOn;
            if (clickSource) clickSource.Play();
        }
    }

    void LateUpdate()
    {
        if (follow)
        {
            if (Application.isPlaying)
            {
                smoothRot = Quaternion.Slerp(smoothRot, follow.rotation, 1f - Mathf.Exp(-lagSpeed * Time.deltaTime));
                transform.rotation = smoothRot;
            }
            else
            {
                transform.rotation = follow.rotation;
            }
        }

        float k = isOn ? FlickerMultiplier() : 0f;

        Vector3 p = transform.position;
        Vector3 d = transform.forward;
        float outer = Mathf.Max(outerAngle, innerAngle + 0.5f);
        Shader.SetGlobalVector("_PSXFlashPos", new Vector4(p.x, p.y, p.z, k > 0f ? 1f : 0f));
        Shader.SetGlobalVector("_PSXFlashDir", new Vector4(d.x, d.y, d.z, range));
        Shader.SetGlobalVector("_PSXFlashCone", new Vector4(
            Mathf.Cos(outer * 0.5f * Mathf.Deg2Rad),
            Mathf.Cos(innerAngle * 0.5f * Mathf.Deg2Rad),
            intensity * k, 0f));
        Shader.SetGlobalColor("_PSXFlashColor", color);
    }

    float FlickerMultiplier()
    {
        if (!flicker || !Application.isPlaying) return 1f;

        if (Time.time >= nextFlicker)
        {
            flickerEnd = Time.time + Random.Range(0.15f, 0.6f);
            nextFlicker = Time.time + Random.Range(flickerInterval * 0.5f, flickerInterval * 1.5f);
        }

        if (Time.time < flickerEnd)
        {
            // Stuttering, dying-battery flicker.
            float n = Mathf.PerlinNoise(Time.time * 40f, 0.37f);
            return n > 0.45f ? Mathf.Lerp(0.6f, 1f, n) : 0.05f;
        }
        return 1f;
    }
}
