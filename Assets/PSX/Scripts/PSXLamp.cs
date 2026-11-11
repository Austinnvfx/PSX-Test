using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A street lamp for PSX/Lit materials: lights nearby surfaces and glows through the fog.
/// PSXManager sends the 32 lamps nearest the camera to the shaders each frame, fading out the farthest.
/// Flickering lamps can play a looping buzz that swells during the flicker bursts.
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
    [Tooltip("Which material slot on the glow renderer is the bulb. -1 = all of them.")]
    public int glowMaterialIndex = -1;

    [Header("Buzz audio (flickering lamps)")]
    [Tooltip("Looping 3D buzz. Quiet while the lamp is steady, swells while it flickers.")]
    public AudioSource buzzSource;
    [Range(0f, 1f)] public float buzzVolume = 0.8f;
    [Tooltip("Buzz level between flicker bursts, as a fraction of Buzz Volume.")]
    [Range(0f, 1f)] public float buzzIdle = 0.08f;

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

    void Start()
    {
        if (Application.isPlaying && buzzSource && buzzSource.clip)
        {
            buzzSource.volume = 0f;
            if (!buzzSource.isPlaying) buzzSource.Play();
            buzzSource.time = Random.Range(0f, buzzSource.clip.length * 0.95f); // nearby lamps don't buzz in sync
        }
    }

    void OnDisable()
    {
        All.Remove(this);
        if (glowRenderer)
        {
            var empty = new MaterialPropertyBlock();
            if (glowMaterialIndex >= 0) glowRenderer.SetPropertyBlock(empty, glowMaterialIndex);
            else glowRenderer.SetPropertyBlock(empty);
        }
    }

    void Update()
    {
        float k = 1f;
        bool inBurst = false;
        if (flicker && Application.isPlaying)
        {
            // Mostly on, with bursts of buzzing dropout like a failing ballast.
            float slow = Mathf.PerlinNoise(Time.time * 0.6f, seed);
            if (slow < 0.42f)
            {
                inBurst = true;
                float fast = Mathf.PerlinNoise(Time.time * 25f, seed + 7f);
                k = fast > 0.5f ? Mathf.Lerp(0.5f, 1f, fast) : 0.04f;
            }
        }
        CurrentIntensity = intensity * k;

        if (buzzSource && Application.isPlaying)
        {
            float target = flicker ? (inBurst ? Mathf.Lerp(0.55f, 1f, 1f - k) : buzzIdle) * buzzVolume : 0f;
            float speed = target > buzzSource.volume ? 8f : 2f; // snap up, trail off
            buzzSource.volume = Mathf.MoveTowards(buzzSource.volume, target, speed * Time.deltaTime);
        }

        if (glowRenderer && Application.isPlaying && flicker)
        {
            if (!hasBaseEmission)
            {
                var mats = glowRenderer.sharedMaterials;
                var m = glowMaterialIndex >= 0 && glowMaterialIndex < mats.Length ? mats[glowMaterialIndex] : glowRenderer.sharedMaterial;
                if (m && m.HasProperty("_EmissionColor"))
                {
                    baseEmission = m.GetColor("_EmissionColor");
                    hasBaseEmission = true;
                }
            }
            if (hasBaseEmission)
            {
                mpb ??= new MaterialPropertyBlock();
                if (glowMaterialIndex >= 0)
                {
                    glowRenderer.GetPropertyBlock(mpb, glowMaterialIndex);
                    mpb.SetColor("_EmissionColor", baseEmission * k);
                    glowRenderer.SetPropertyBlock(mpb, glowMaterialIndex);
                }
                else
                {
                    glowRenderer.GetPropertyBlock(mpb);
                    mpb.SetColor("_EmissionColor", baseEmission * k);
                    glowRenderer.SetPropertyBlock(mpb);
                }
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(BulbPosition, range);
    }
}
