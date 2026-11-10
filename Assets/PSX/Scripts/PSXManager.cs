using UnityEngine;

/// <summary>
/// Pushes the global PSX look settings (fog, ambient, vertex snap, color depth) to every PSX/Lit material.
/// One per scene. Runs in edit mode so the Scene view previews the look.
/// </summary>
[ExecuteAlways]
public class PSXManager : MonoBehaviour
{
    [Header("Fog")]
    public Color fogColor = new Color(0.46f, 0.47f, 0.48f);
    [Tooltip("Higher = thicker. ~0.085 gives roughly 20m of visibility.")]
    [Range(0f, 0.3f)] public float fogDensity = 0.085f;
    [Tooltip("How much drifting noise thins/thickens the fog.")]
    [Range(0f, 1f)] public float fogNoise = 0.4f;
    public float fogNoiseScale = 0.12f;
    public Vector3 fogWind = new Vector3(0.35f, 0.05f, 0.2f);

    [Header("Lighting")]
    public Color ambient = new Color(0.30f, 0.30f, 0.32f);

    [Header("Retro")]
    [Tooltip("Vertical resolution of the vertex snapping grid. Lower = more wobble.")]
    public float snapResolution = 120f;
    [Tooltip("Color levels per channel. 32 = PS1 15-bit color.")]
    [Range(2, 256)] public int colorLevels = 32;
    [Range(0f, 2f)] public float ditherStrength = 1f;

    [Tooltip("Cameras whose background is set to the fog color. Uses Camera.main when empty.")]
    public Camera[] cameras;

    void OnEnable() => Apply();
    void OnValidate() => Apply();
    void Update() => Apply();

    void Apply()
    {
        Shader.SetGlobalColor("_PSXFogColor", fogColor);
        Shader.SetGlobalVector("_PSXFogParams", new Vector4(fogDensity, fogNoise, fogNoiseScale, 0f));
        Shader.SetGlobalVector("_PSXFogWind", fogWind);
        Shader.SetGlobalColor("_PSXAmbient", ambient);
        Shader.SetGlobalVector("_PSXSnapRes", new Vector4(0f, snapResolution, 0f, 0f));
        Shader.SetGlobalVector("_PSXColorParams", new Vector4(colorLevels, ditherStrength, 0f, 0f));

        if (cameras != null && cameras.Length > 0)
        {
            foreach (var c in cameras)
                if (c) SetBackground(c);
        }
        else if (Camera.main)
        {
            SetBackground(Camera.main);
        }
    }

    void SetBackground(Camera c)
    {
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = fogColor;
    }
}
