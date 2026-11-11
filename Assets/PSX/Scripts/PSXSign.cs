using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A street sign plate with two lines of chunky pixel text, drawn into a tiny texture at load.
/// Edit the text in the Inspector and the sign redraws. Use &lt; &gt; ^ for arrows.
/// Goes on the plate (a PSX/Lit renderer whose front face is UV-mapped 0-1).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class PSXSign : MonoBehaviour
{
    public enum Style { Road, Warning }

    public Style style = Style.Road;
    [Tooltip("Use < > ^ for arrows. Letters, numbers, space and - . ! / : are supported.")]
    public string line1 = "THE FACILITY";
    public string line2 = "^ 100M";
    [Tooltip("How grimy the sign is.")]
    [Range(0f, 1f)] public float dirt = 0.5f;
    [Tooltip("Faint glow so the sign is barely readable in the dark. Flashlight makes it pop.")]
    [Range(0f, 0.5f)] public float glow = 0.1f;

    public const int Width = 96, Height = 32;

    Texture2D tex;
    MaterialPropertyBlock mpb;

    void OnEnable() => Redraw();
    void OnValidate() => Redraw();

    void OnDisable()
    {
        if (tex) DestroyImmediate(tex);
        tex = null;
    }

    public void Redraw()
    {
        var r = GetComponent<MeshRenderer>();
        if (!r) return;
        if (!tex)
        {
            tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
                name = "Sign Text",
            };
        }
        tex.SetPixels32(Paint());
        tex.Apply(false);

        mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetTexture("_BaseMap", tex);
        mpb.SetTexture("_EmissionMap", tex);
        mpb.SetColor("_EmissionColor", Color.white * glow);
        r.SetPropertyBlock(mpb);
    }

    Color32[] Paint()
    {
        Color bg, fg, edge;
        if (style == Style.Road) { bg = new Color(0.08f, 0.30f, 0.16f); fg = new Color(0.92f, 0.92f, 0.88f); edge = fg; }
        else { bg = new Color(0.78f, 0.62f, 0.10f); fg = new Color(0.06f, 0.05f, 0.04f); edge = fg; }

        var px = new Color[Width * Height];
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            bool border = x == 2 || x == Width - 3 || y == 2 || y == Height - 3;
            bool outside = x < 2 || x > Width - 3 || y < 2 || y > Height - 3;
            px[y * Width + x] = outside ? bg * 0.8f : border ? edge : bg;
        }

        // Warning signs get hazard stripes top and bottom.
        if (style == Style.Warning)
            for (int x = 3; x < Width - 3; x++)
                for (int y = 3; y < 6; y++)
                {
                    bool stripe = ((x + y) / 4) % 2 == 0;
                    if (stripe) { px[y * Width + x] = fg; px[(Height - 1 - y) * Width + x] = fg; }
                }

        int top = Height - 9;  // line 1 baseline area (rows counted from the bottom)
        DrawLine(px, line1, top - 7 + 1, fg);
        DrawLine(px, line2, top - 7 - 9 + 1, fg);

        // Grime: dark blotches, rust streaks running down, the odd scratch.
        int seed = (line1 + line2).GetHashCode();
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            float n = Noise(x * 0.12f, y * 0.25f, seed);
            float streak = Noise(x * 0.9f, y * 0.06f, seed + 17);
            float grime = Mathf.Clamp01((n - 0.55f) * 2.5f) * 0.45f + Mathf.Clamp01((streak - 0.6f) * 2f) * 0.35f;
            grime *= dirt;
            var c = px[y * Width + x];
            var rust = new Color(0.30f, 0.16f, 0.07f);
            c = Color.Lerp(c, rust, grime);
            c *= 1f - (Hash(x, y, seed) * 0.12f * dirt);
            c.a = 1f;
            px[y * Width + x] = c;
        }

        var outPx = new Color32[px.Length];
        for (int i = 0; i < px.Length; i++) outPx[i] = px[i];
        return outPx;
    }

    void DrawLine(Color[] px, string text, int y0, Color c)
    {
        if (string.IsNullOrEmpty(text)) return;
        text = text.ToUpperInvariant();
        int maxChars = (Width - 5) / 6; // 15 characters
        if (text.Length > maxChars) text = text.Substring(0, maxChars);
        int w = text.Length * 6 - 1;
        int x0 = (Width - w) / 2;
        for (int i = 0; i < text.Length; i++)
        {
            if (!Font.TryGetValue(text[i], out var rows)) continue;
            for (int row = 0; row < 7; row++)
            for (int col = 0; col < 5; col++)
            {
                if ((rows[row] & (1 << (4 - col))) == 0) continue;
                int x = x0 + i * 6 + col;
                int y = y0 + (6 - row); // row 0 is the top of the glyph
                if (x >= 0 && x < Width && y >= 0 && y < Height) px[y * Width + x] = c;
            }
        }
    }

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }

    static float Noise(float x, float y, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float a = Hash(x0, y0, seed), b = Hash(x0 + 1, y0, seed);
        float c = Hash(x0, y0 + 1, seed), d = Hash(x0 + 1, y0 + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // 5x7 pixel font. Each glyph is 7 rows, top to bottom; bit 4 is the leftmost pixel.
    static readonly Dictionary<char, byte[]> Font = new Dictionary<char, byte[]>
    {
        ['A'] = new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
        ['B'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E },
        ['C'] = new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E },
        ['D'] = new byte[] { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E },
        ['E'] = new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F },
        ['F'] = new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 },
        ['G'] = new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F },
        ['H'] = new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
        ['I'] = new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E },
        ['J'] = new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C },
        ['K'] = new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 },
        ['L'] = new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F },
        ['M'] = new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 },
        ['N'] = new byte[] { 0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11 },
        ['O'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
        ['P'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 },
        ['Q'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D },
        ['R'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 },
        ['S'] = new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E },
        ['T'] = new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 },
        ['U'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
        ['V'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 },
        ['W'] = new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A },
        ['X'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 },
        ['Y'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 },
        ['Z'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F },
        ['0'] = new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E },
        ['1'] = new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
        ['2'] = new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F },
        ['3'] = new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E },
        ['4'] = new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 },
        ['5'] = new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
        ['6'] = new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E },
        ['7'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
        ['8'] = new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E },
        ['9'] = new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
        [' '] = new byte[] { 0, 0, 0, 0, 0, 0, 0 },
        ['-'] = new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 },
        ['.'] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C },
        ['!'] = new byte[] { 0x04, 0x04, 0x04, 0x04, 0x04, 0x00, 0x04 },
        ['/'] = new byte[] { 0x01, 0x01, 0x02, 0x04, 0x08, 0x10, 0x10 },
        [':'] = new byte[] { 0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00 },
        ['>'] = new byte[] { 0x00, 0x04, 0x02, 0x1F, 0x02, 0x04, 0x00 }, // arrow right
        ['<'] = new byte[] { 0x00, 0x04, 0x08, 0x1F, 0x08, 0x04, 0x00 }, // arrow left
        ['^'] = new byte[] { 0x04, 0x0E, 0x15, 0x04, 0x04, 0x04, 0x04 }, // arrow ahead
    };
}
