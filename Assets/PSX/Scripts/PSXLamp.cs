using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A street lamp for PSX/Lit materials: lights nearby surfaces and glows through the fog.
/// PSXManager sends the 16 lamps nearest the camera to the shaders each frame.
/// </summary>
[ExecuteAlways]
public class PSXLamp : MonoBehaviour
{
    public static readonly List<PSXLamp> All = new List<PSXLamp>();

    public Color color = new Color(1f, 0.72f, 0.35f); // sodium-vapor yellow
    [Range(0f, 8f)] public float intensity = 2.5f;
    public float range = 11f;
    [Tooltip("How strongly the fog around the lamp glows.")]
    [Range(0f, 3f)] public float haloStrength = 0.7f;
    [Tooltip("Offset from this transform to the bulb.")]
    public Vector3 bulbOffset = new Vector3(0f, -0.05f, 0f);

    [Header("Flicker")]
    public bool flicker;
    [Tooltip("Renderer whose emission dims with the flicker (the lamp head).")]
    public Renderer glowRenderer;

    public float CurrentIntensity { get; private set; }
    public Vector3 BulbPosition => transform.TransformPoint(bulbOffset);

    MaterialPropertyBlock mpb;
    Color baseEmission;
    bool hasBaseEmission;
    float seed;

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        seed = Random.value * 100f;
        CurrentIntensity = intensity;
    }

    void OnDisable()
    {
        All.Remove(this);
        if (glowRenderer) glowRenderer.SetPropertyBlock(null);
    }

    void Update()
    {
        float k = 1f;
        if (flicker && Application.isPlaying)
        {
            // Mostly on, with bursts of buzzing dropout like a failing ballast.
            float slow = Mathf.PerlinNoise(Time.time * 0.6f, seed);
            if (slow < 0.42f)
            {
                float fast = Mathf.PerlinNoise(Time.time * 25f, seed + 7f);
                k = fast > 0.5f ? Mathf.Lerp(0.5f, 1f, fast) : 0.04f;
            }
        }
        CurrentIntensity = intensity * k;

        if (glowRenderer && Application.isPlaying)
        {
            if (!hasBaseEmission && glowRenderer.sharedMaterial && glowRenderer.sharedMaterial.HasProperty("_EmissionColor"))
            {
                baseEmission = glowRenderer.sharedMaterial.GetColor("_EmissionColor");
                hasBaseEmission = true;
            }
            if (hasBaseEmission && flicker)
            {
                mpb ??= new MaterialPropertyBlock();
                glowRenderer.GetPropertyBlock(mpb);
                mpb.SetColor("_EmissionColor", baseEmission * k);
                glowRenderer.SetPropertyBlock(mpb);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(BulbPosition, range);
    }
}
