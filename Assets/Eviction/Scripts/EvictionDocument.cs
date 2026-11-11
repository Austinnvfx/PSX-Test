using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Full-screen paper reader on the pixel HUD (the eviction notice; later missing posters and clues).
/// Also shows the punch card when clocking out. E / (A) puts the paper down.
/// </summary>
public class EvictionDocument : MonoBehaviour
{
    public AudioSource source;
    public AudioClip paperSound;

    public bool IsOpen { get; private set; }

    string title, body;
    Action onClosed;
    float openTime;
    float show; // 0..1 slide-in

    // Punch card (shown on its own, no input).
    string[] cardRows;
    string cardStamp;
    float cardUntil = -1f, cardStart;

    void Start()
    {
        var hud = EvictionHud.Instance;
        if (hud) hud.AddLayer(30, Draw);
    }

    public void Open(string title, string body, Action onClosed = null)
    {
        this.title = title;
        this.body = body;
        this.onClosed = onClosed;
        IsOpen = true;
        openTime = Time.time;
        show = 0f;
        PSXPlayer.InputLocked = true;
        PlayerInteractor.Blocked = true;
        if (source && paperSound) source.PlayOneShot(paperSound, 0.9f);
    }

    public void ShowCard(string[] rows, string stamp, float seconds)
    {
        cardRows = rows;
        cardStamp = stamp;
        cardStart = Time.time;
        cardUntil = Time.time + seconds;
    }

    void Update()
    {
        if (!IsOpen || EvictionMenu.Paused) return;
        show = Mathf.MoveTowards(show, 1f, Time.deltaTime * 5f);
        var hud = EvictionHud.Instance;
        if (hud && Time.time - openTime > 0.6f) hud.SetTooltip(EvictionHud.Key("E", "A") + " Put down");
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        bool close = (kb != null && (kb.eKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)) ||
                     (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame));
        if (close && Time.time - openTime > 0.6f) { EvictionMenu.EscUsedFrame = Time.frameCount; Close(); }
    }

    public void Close()
    {
        if (!IsOpen) return;
        {
            IsOpen = false;
            PSXPlayer.InputLocked = false;
            PlayerInteractor.Blocked = false;
            if (source && paperSound) source.PlayOneShot(paperSound, 0.6f);
            var cb = onClosed; onClosed = null;
            cb?.Invoke();
        }
    }

    // ------------------------------------------------------------------ drawing

    static readonly Color32 Paper = new Color32(226, 222, 206, 255);
    static readonly Color32 PaperEdge = new Color32(196, 190, 170, 255);
    static readonly Color32 Ink = new Color32(30, 30, 34, 255);
    static readonly Color32 Tape = new Color32(222, 214, 160, 150);
    static readonly Color32 CardCol = new Color32(214, 200, 160, 255);
    static readonly Color32 CardLine = new Color32(170, 150, 110, 255);
    static readonly Color32 Red = new Color32(170, 30, 30, 255);

    void Draw(EvictionHud hud)
    {
        if (IsOpen) DrawPaper(hud);
        if (cardRows != null && Time.time < cardUntil) DrawCard(hud);
    }

    void DrawPaper(EvictionHud hud)
    {
        hud.Rect(0, 0, hud.W, hud.H, new Color32(0, 0, 0, (byte)(150 * show)));
        // Wide and short enough that the "Put down" prompt below never covers the text.
        int pw = Mathf.Min(hud.W - 20, 300), ph = hud.H - 34;
        int x = (hud.W - pw) / 2;
        int y = Mathf.RoundToInt(Mathf.Lerp(hud.H, 5, 1f - Mathf.Pow(1f - show, 3f)));
        hud.Rect(x + 3, y + 3, pw, ph, new Color32(0, 0, 0, 110));
        hud.Rect(x, y, pw, ph, PaperEdge);
        hud.Rect(x + 1, y + 1, pw - 2, ph - 2, Paper);
        // Grain: a few fixed specks so it reads as cheap paper.
        var rng = new System.Random(7);
        for (int i = 0; i < 260; i++) hud.Pixel(x + 2 + rng.Next(pw - 4), y + 2 + rng.Next(ph - 4), PaperEdge);
        // Tape at the top corners.
        hud.Rect(x - 6, y - 3, 22, 9, Tape);
        hud.Rect(x + pw - 16, y - 3, 22, 9, Tape);

        int ty = y + 10;
        if (!string.IsNullOrEmpty(title))
        {
            int tw = PSXFont.Measure(title, 2);
            hud.Text(x + (pw - tw) / 2, ty - 2, title, Ink, 2);
            ty += 17;
            hud.Rect(x + 12, ty - 2, pw - 24, 1, Ink);
            ty += 3;
        }
        int cols = (pw - 20) / PSXFont.Advance;
        foreach (var line in PSXFont.Wrap(body, cols))
        {
            if (ty + 8 > y + ph - 2) break;
            hud.Text(x + 10, ty, line, Ink);
            ty += 9;
        }
    }

    void DrawCard(EvictionHud hud)
    {
        float t = Time.time - cardStart, left = cardUntil - Time.time;
        float k = Mathf.Min(Mathf.Clamp01(t * 5f), Mathf.Clamp01(left * 4f));
        const int cw = 142, ch = 176;
        int x = hud.W / 2 - cw / 2;
        int y = Mathf.RoundToInt(Mathf.Lerp(hud.H, (hud.H - ch) / 2 - 8, k));
        hud.Rect(x + 3, y + 3, cw, ch, new Color32(0, 0, 0, 110));
        hud.Rect(x, y, cw, ch, CardCol);
        hud.Rect(x + cw / 2 - 6, y + 4, 12, 4, CardLine); // punch notch
        int ry = y + 14;
        for (int i = 0; i < cardRows.Length; i++)
        {
            hud.Text(x + 6, ry, cardRows[i], Ink);
            ry += 11;
            if (i == 2 || i == 3 || i == cardRows.Length - 1) { hud.Rect(x + 4, ry - 3, cw - 8, 1, CardLine); ry += 2; }
        }
        // The fresh stamp lands a beat after the card: big, red, with a smudged 1px ghost.
        if (t > 0.3f && !string.IsNullOrEmpty(cardStamp))
        {
            int sw = PSXFont.Measure(cardStamp, 2);
            int sx = x + (cw - sw) / 2, sy = ry + 4;
            hud.Text(sx + 1, sy + 1, cardStamp, new Color32(170, 30, 30, 110), 2);
            hud.Text(sx, sy, cardStamp, Red, 2);
        }
    }
}
