using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders the camera into a tiny point-filtered render texture (e.g. 320x240) and stretches it
/// to the screen for chunky PS1-style pixels, with an optional vignette. Only active in Play mode.
/// </summary>
[RequireComponent(typeof(Camera))]
public class PSXRetroCamera : MonoBehaviour
{
    [Tooltip("Internal vertical resolution. PS1 games mostly ran at 240.")]
    [Range(120, 720)] public int verticalResolution = 240;
    public bool vignette = true;
    [Range(0f, 1f)] public float vignetteStrength = 0.65f;

    Camera cam;
    RenderTexture rt;
    GameObject uiRoot;
    Camera displayCam;
    RawImage screenImage;
    RawImage vignetteImage;
    readonly RawImage[] tearStrips = new RawImage[6];
    float tearUntil, nextTear;
    Texture2D vignetteTex;
    int lastW, lastH, lastRes;

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        cam = GetComponent<Camera>();
        BuildDisplay();
        Rebuild();
    }

    void OnDisable()
    {
        if (cam) cam.targetTexture = null;
        if (rt) { rt.Release(); Destroy(rt); }
        if (uiRoot) Destroy(uiRoot);
        if (displayCam) Destroy(displayCam.gameObject);
        if (vignetteTex) Destroy(vignetteTex);
    }

    void Update()
    {
        if (!Application.isPlaying || !cam) return;
        if (Screen.width != lastW || Screen.height != lastH || verticalResolution != lastRes)
            Rebuild();
        UpdateTear();
        if (vignetteImage)
        {
            vignetteImage.enabled = vignette;
            vignetteImage.color = new Color(1f, 1f, 1f, vignetteStrength);
        }
    }

    void BuildDisplay()
    {
        // A camera that renders nothing but clears the backbuffer, so the screen is never undefined.
        var camGo = new GameObject("PSX Display Camera");
        displayCam = camGo.AddComponent<Camera>();
        displayCam.cullingMask = 0;
        displayCam.clearFlags = CameraClearFlags.SolidColor;
        displayCam.backgroundColor = Color.black;
        displayCam.depth = cam.depth + 100;

        uiRoot = new GameObject("PSX Display", typeof(Canvas));
        var canvas = uiRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1000;

        screenImage = CreateFullscreenImage("Screen", uiRoot.transform);

        for (int i = 0; i < tearStrips.Length; i++)
        {
            tearStrips[i] = CreateFullscreenImage("Tear " + i, uiRoot.transform);
            tearStrips[i].enabled = false;
        }

        vignetteTex = BuildVignette(128);
        vignetteImage = CreateFullscreenImage("Vignette", uiRoot.transform);
        vignetteImage.texture = vignetteTex;
    }

    static RawImage CreateFullscreenImage(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var img = go.GetComponent<RawImage>();
        img.raycastTarget = false;
        return img;
    }

    void Rebuild()
    {
        lastW = Screen.width;
        lastH = Screen.height;
        lastRes = verticalResolution;

        int h = verticalResolution;
        int w = Mathf.Max(1, Mathf.RoundToInt(h * (float)lastW / Mathf.Max(1, lastH)));

        cam.targetTexture = null;
        if (rt) { rt.Release(); Destroy(rt); }

        rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
        {
            name = "PSX Low-Res Target",
            filterMode = FilterMode.Point,
            antiAliasing = 1,
            useMipMap = false,
        };
        rt.Create();
        cam.targetTexture = rt;
        screenImage.texture = rt;
        foreach (var s in tearStrips) if (s) s.texture = rt;
    }

    /// <summary>Tear the picture for a moment: horizontal bands slide sideways, like a bad signal.</summary>
    public void Tear(float seconds) { tearUntil = Time.unscaledTime + seconds; nextTear = 0f; }

    void UpdateTear()
    {
        bool on = Time.unscaledTime < tearUntil;
        if (!on) { foreach (var s in tearStrips) if (s) s.enabled = false; return; }
        if (Time.unscaledTime < nextTear) return;
        nextTear = Time.unscaledTime + 0.045f;
        foreach (var s in tearStrips)
        {
            if (!s) continue;
            s.enabled = Random.value < 0.75f;
            float y0 = Random.value * 0.95f, h = Random.Range(0.015f, 0.12f), dx = Random.Range(-0.08f, 0.08f);
            var rt = s.rectTransform;
            rt.anchorMin = new Vector2(0f, y0);
            rt.anchorMax = new Vector2(1f, Mathf.Min(1f, y0 + h));
            s.uvRect = new Rect(dx, y0, 1f, h);
        }
    }

    static Texture2D BuildVignette(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size * 2f - 1f;
            float v = (y + 0.5f) / size * 2f - 1f;
            float d = Mathf.Sqrt(u * u + v * v) / 1.414f;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.05f, d));
            px[y * size + x] = new Color(0f, 0f, 0f, a);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }
}
