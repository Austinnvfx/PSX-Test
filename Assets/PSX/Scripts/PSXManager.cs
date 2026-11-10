using UnityEngine;

/// <summary>
/// Pushes the global PSX look settings (fog, ambient, vertex snap, color depth) to every PSX/Lit material.
/// One per scene. Runs in edit mode so the Scene view previews the look.
/// </summary>
[ExecuteAlways]
public class PSXManager : MonoBehaviour
{
    [Header("Fog")]
    public Color fogColor = new Color(0.38f, 0.39f, 0.40f);
    [Tooltip("Higher = thicker. 0.055 keeps ~10m clear and walls off around 30m.")]
    [Range(0f, 0.3f)] public float fogDensity = 0.055f;
    [Tooltip("How much drifting noise thins/thickens the fog.")]
    [Range(0f, 1f)] public float fogNoise = 0.3f;
    public float fogNoiseScale = 0.12f;
    public Vector3 fogWind = new Vector3(0.35f, 0.05f, 0.2f);

    [Header("Lighting")]
    public Color ambient = new Color(0.30f, 0.30f, 0.32f);

    [Header("Retro")]
    [Tooltip("Vertical resolution of the vertex snapping grid. Lower = more wobble. 240 = real PS1 pixel grid.")]
    public float snapResolution = 240f;
    [Tooltip("Color levels per channel. 32 = PS1 15-bit color.")]
    [Range(2, 256)] public int colorLevels = 32;
    [Range(0f, 2f)] public float ditherStrength = 0.7f;

    [Tooltip("Cameras whose background is set to the fog color. Uses Camera.main when empty.")]
    public Camera[] cameras;

    void OnEnable() => Apply();
    void OnValidate() => Apply();
    void LateUpdate() => Apply(); // after the player has moved and lamps have flickered

    void Apply()
    {
        Shader.SetGlobalColor("_PSXFogColor", fogColor);
        Shader.SetGlobalVector("_PSXFogParams", new Vector4(fogDensity, fogNoise, fogNoiseScale, 0f));
        Shader.SetGlobalVector("_PSXFogWind", fogWind);
        Shader.SetGlobalColor("_PSXAmbient", ambient);
        Shader.SetGlobalVector("_PSXSnapRes", new Vector4(0f, snapResolution, 0f, 0f));
        Shader.SetGlobalVector("_PSXColorParams", new Vector4(colorLevels, ditherStrength, 0f, 0f));

        Camera mainCam = null;
        if (cameras != null && cameras.Length > 0)
        {
            foreach (var c in cameras)
                if (c) { SetBackground(c); if (!mainCam) mainCam = c; }
        }
        else if (Camera.main)
        {
            mainCam = Camera.main;
            SetBackground(mainCam);
        }

        PushLamps(mainCam ? mainCam.transform.position : transform.position);
    }

    const int MaxLamps = 16; // must match PSX_MAX_LAMPS in PSXCommon.hlsl
    static readonly Vector4[] lampPos = new Vector4[MaxLamps];
    static readonly Vector4[] lampCol = new Vector4[MaxLamps];
    static readonly System.Collections.Generic.List<PSXLamp> sorted = new System.Collections.Generic.List<PSXLamp>();

    static void PushLamps(Vector3 camPos)
    {
        sorted.Clear();
        foreach (var l in PSXLamp.All)
            if (l && l.isActiveAndEnabled) sorted.Add(l);
        sorted.Sort((a, b) => (a.BulbPosition - camPos).sqrMagnitude.CompareTo((b.BulbPosition - camPos).sqrMagnitude));

        int count = Mathf.Min(MaxLamps, sorted.Count);
        for (int i = 0; i < MaxLamps; i++)
        {
            if (i < count)
            {
                var l = sorted[i];
                Vector3 p = l.BulbPosition;
                Color c = l.color.linear * l.CurrentIntensity;
                lampPos[i] = new Vector4(p.x, p.y, p.z, Mathf.Max(0.01f, l.range));
                lampCol[i] = new Vector4(c.r, c.g, c.b, l.haloStrength);
            }
            else
            {
                lampPos[i] = new Vector4(0, -10000, 0, 0.01f);
                lampCol[i] = Vector4.zero;
            }
        }
        Shader.SetGlobalVectorArray("_PSXLampPos", lampPos);
        Shader.SetGlobalVectorArray("_PSXLampColor", lampCol);
        Shader.SetGlobalFloat("_PSXLampCount", count);
    }

    void SetBackground(Camera c)
    {
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = fogColor;
    }
}
