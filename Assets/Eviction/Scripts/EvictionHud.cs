using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Pixel HUD drawn at the game's own low resolution (same as PSXRetroCamera), so tooltips, the phone,
/// documents and titles get the same chunky pixels as the 3D. Also owns the fade-to-black.
/// Other scripts draw by adding a layer: hud.AddLayer(order, DrawFn).
/// </summary>
[DefaultExecutionOrder(1000)]
public class EvictionHud : MonoBehaviour
{
    public static EvictionHud Instance { get; private set; }
    /// <summary>True when the last input came from a gamepad (tooltips show pad buttons).</summary>
    public static bool UsingGamepad { get; private set; }

    [Range(120, 720)] public int verticalResolution = 240;
    [Tooltip("0 = clear, 1 = black. Drawn under the HUD, over the game.")]
    [Range(0f, 1f)] public float fade = 1f;

    public int W { get; private set; }
    public int H { get; private set; }

    public static readonly Color32 White = new Color32(235, 235, 228, 255);
    public static readonly Color32 Grey = new Color32(150, 150, 150, 255);
    public static readonly Color32 Shadow = new Color32(0, 0, 0, 200);

    Color32[] buf;
    Texture2D tex;
    RawImage hudImage, fadeImage;
    int lastScreenW, lastScreenH;
    string tooltip, tooltipSecondary;
    readonly List<(int order, Action<EvictionHud> draw)> layers = new List<(int, Action<EvictionHud>)>();

    void Awake()
    {
        Instance = this;
        var canvasGo = new GameObject("Eviction HUD", typeof(Canvas));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // above the PSX display canvas
        fadeImage = FullScreen("Fade", canvasGo.transform);
        fadeImage.color = Color.black;
        fadeImage.texture = Texture2D.whiteTexture;
        hudImage = FullScreen("Pixels", canvasGo.transform);
        Rebuild();
    }

    void OnDestroy() { if (Instance == this) Instance = null; if (tex) Destroy(tex); }

    static RawImage FullScreen(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<RawImage>();
        img.raycastTarget = false;
        return img;
    }

    void Rebuild()
    {
        lastScreenW = Screen.width; lastScreenH = Screen.height;
        H = verticalResolution;
        W = Mathf.Max(1, Mathf.RoundToInt(H * (float)lastScreenW / Mathf.Max(1, lastScreenH)));
        if (tex) Destroy(tex);
        tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        buf = new Color32[W * H];
        hudImage.texture = tex;
    }

    public void AddLayer(int order, Action<EvictionHud> draw)
    {
        layers.Add((order, draw));
        layers.Sort((a, b) => a.order.CompareTo(b.order));
    }

    /// <summary>Show a prompt at the bottom of the screen this frame (call every frame while it applies).</summary>
    public void SetTooltip(string text) => tooltip = text;
    /// <summary>A second prompt line above the first (e.g. the phone buzzing while you look at a door).</summary>
    public void SetSecondaryTooltip(string text) => tooltipSecondary = text;

    /// <summary>"[E]" on keyboard, "(A)" on a gamepad.</summary>
    public static string Key(string keyboard, string pad) => UsingGamepad ? "(" + pad + ")" : "[" + keyboard + "]";

    void Update()
    {
        var pad = Gamepad.current;
        if (pad != null && (pad.wasUpdatedThisFrame && (pad.leftStick.ReadValue().sqrMagnitude > 0.1f || pad.rightStick.ReadValue().sqrMagnitude > 0.1f ||
            pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.buttonWest.isPressed || pad.buttonNorth.isPressed || pad.dpad.ReadValue().sqrMagnitude > 0.1f)))
            UsingGamepad = true;
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if ((kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 4f))
            UsingGamepad = false;
    }

    void LateUpdate()
    {
        if (Screen.width != lastScreenW || Screen.height != lastScreenH || H != verticalResolution) Rebuild();

        System.Array.Clear(buf, 0, buf.Length);
        foreach (var l in layers) { Clip = null; l.draw(this); }
        Clip = null;

        if (!string.IsNullOrEmpty(tooltip)) Prompt(tooltip, H - 22);
        if (!string.IsNullOrEmpty(tooltipSecondary)) Prompt(tooltipSecondary, string.IsNullOrEmpty(tooltip) ? H - 22 : H - 36);
        tooltip = tooltipSecondary = null;

        tex.SetPixels32(buf);
        tex.Apply(false);
        fadeImage.color = new Color(0f, 0f, 0f, fade);
        fadeImage.enabled = fade > 0.001f;
    }

    void Prompt(string text, int y)
    {
        int w = PSXFont.Measure(text) + 8;
        int x = (W - w) / 2;
        Rect(x, y - 3, w, 15, new Color32(0, 0, 0, 150));
        Text(x + 4, y, text, White);
    }

    // ------------------------------------------------------------------ drawing (top-left origin, pixels)

    /// <summary>While set, all drawing is limited to this rectangle (e.g. a scrolling phone screen).</summary>
    public RectInt? Clip;

    public void Rect(int x, int y, int w, int h, Color32 c)
    {
        int x0 = Mathf.Max(0, x), y0 = Mathf.Max(0, y), x1 = Mathf.Min(W, x + w), y1 = Mathf.Min(H, y + h);
        if (Clip.HasValue) { var k = Clip.Value; x0 = Mathf.Max(x0, k.xMin); y0 = Mathf.Max(y0, k.yMin); x1 = Mathf.Min(x1, k.xMax); y1 = Mathf.Min(y1, k.yMax); }
        if (x0 >= x1 || y0 >= y1) return;
        bool opaque = c.a == 255;
        float a = c.a / 255f, ia = 1f - a;
        for (int py = y0; py < y1; py++)
        {
            int row = (H - 1 - py) * W;
            for (int px = x0; px < x1; px++)
            {
                int i = row + px;
                if (opaque) { buf[i] = c; continue; }
                var d = buf[i];
                buf[i] = new Color32((byte)(c.r * a + d.r * ia), (byte)(c.g * a + d.g * ia), (byte)(c.b * a + d.b * ia),
                                     (byte)Mathf.Min(255, c.a + d.a * ia));
            }
        }
    }

    /// <summary>Rectangle with the corners nipped off (a cheap rounded look).</summary>
    public void RoundRect(int x, int y, int w, int h, Color32 c, int r = 2)
    {
        for (int i = 0; i < r; i++)
        {
            int inset = r - i;
            Rect(x + inset, y + i, w - inset * 2, 1, c);
            Rect(x + inset, y + h - 1 - i, w - inset * 2, 1, c);
        }
        Rect(x, y + r, w, h - r * 2, c);
    }

    public void Frame(int x, int y, int w, int h, Color32 c)
    {
        Rect(x, y, w, 1, c); Rect(x, y + h - 1, w, 1, c);
        Rect(x, y, 1, h, c); Rect(x + w - 1, y, 1, h, c);
    }

    public void Pixel(int x, int y, Color32 c) => Rect(x, y, 1, 1, c);

    public void Text(int x, int y, string s, Color32 c, int scale = 1) => PSXFont.Draw(buf, W, H, x, y, s, c, scale, Clip);

    public void TextShadowed(int x, int y, string s, Color32 c, int scale = 1)
    {
        PSXFont.Draw(buf, W, H, x + scale, y + scale, s, new Color32(0, 0, 0, (byte)(c.a * 0.8f)), scale, Clip);
        PSXFont.Draw(buf, W, H, x, y, s, c, scale, Clip);
    }

    public void TextCentered(int y, string s, Color32 c, int scale = 1, bool shadow = false)
    {
        int x = (W - PSXFont.Measure(s, scale)) / 2;
        if (shadow) TextShadowed(x, y, s, c, scale); else Text(x, y, s, c, scale);
    }

    public static Color32 WithAlpha(Color32 c, float a) => new Color32(c.r, c.g, c.b, (byte)Mathf.Clamp(c.a * a, 0, 255));
}
