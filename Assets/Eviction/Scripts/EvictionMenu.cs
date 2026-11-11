using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Title screen (New Game / Settings / Credits / Quit), Settings, and the pause menu (Esc / Start):
/// Resume / Settings / Quit to Title. Drawn on the pixel HUD. Keyboard, mouse and gamepad.
/// </summary>
public class EvictionMenu : MonoBehaviour
{
    public EvictionSequence sequence;
    public EvictionPhone phone;
    public EvictionDocument document;
    public AudioSource uiSource;
    public AudioClip moveSound, selectSound;
    [Tooltip("Optional: music on the title screen. Empty = just the rain.")]
    public AudioClip menuMusic;

    public string title = "EVICTION";
    public string subtitle = "A PSX Horror Concept Demo";

    public static bool Paused { get; private set; }
    /// <summary>Frame when the phone or a paper used Esc (so the same press doesn't also pause).</summary>
    public static int EscUsedFrame = -1;

    enum Page { None, Title, Settings, Pause, Credits }
    Page page = Page.None, settingsBack;
    int sel;
    float pageTime, titleAlpha;
    AudioSource musicSrc;
    bool savedBlocked;
    readonly List<RectInt> itemRects = new List<RectInt>();
    Vector2 lastMouse;

    void Start()
    {
        var hud = EvictionHud.Instance;
        if (hud) hud.AddLayer(60, Draw);
        if (uiSource) uiSource.ignoreListenerPause = true;
        PSXPlayer.EscapeOpensMenu = true;
    }

    void OnDestroy()
    {
        Paused = false;
        Time.timeScale = 1f;
    }

    // ------------------------------------------------------------------ pages

    public void ShowTitle()
    {
        Open(Page.Title);
        PSXPlayer.CursorFree = true;
        PSXPlayer.SetCursorLocked(false);
        if (menuMusic)
        {
            musicSrc = gameObject.AddComponent<AudioSource>();
            musicSrc.clip = menuMusic;
            musicSrc.loop = true;
            musicSrc.ignoreListenerPause = true;
            EvictionSettings.RegisterMusic(musicSrc);
            musicSrc.Play();
        }
    }

    void Open(Page p) { page = p; sel = 0; pageTime = 0f; }

    void Pause()
    {
        Paused = true;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        savedBlocked = PlayerInteractor.Blocked;
        PlayerInteractor.Blocked = true;
        PSXPlayer.CursorFree = true;
        PSXPlayer.SetCursorLocked(false);
        Open(Page.Pause);
        Play(selectSound);
    }

    void Resume()
    {
        Paused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        PlayerInteractor.Blocked = savedBlocked;
        PSXPlayer.CursorFree = false;
        PSXPlayer.SetCursorLocked(true);
        page = Page.None;
    }

    // ------------------------------------------------------------------ items

    struct Item { public string label; public Func<string> value; public Action select; public Action<int> adjust; }

    List<Item> Items()
    {
        var list = new List<Item>();
        switch (page)
        {
            case Page.Title:
                list.Add(new Item { label = "New Game", select = () => { page = Page.None; StopMenuMusic(); sequence.NewGame(); } });
                list.Add(new Item { label = "Settings", select = () => { settingsBack = Page.Title; Open(Page.Settings); } });
                list.Add(new Item { label = "Credits", select = () => { page = Page.Credits; sequence.PlayCreditsFromMenu(() => Open(Page.Title)); } });
                list.Add(new Item { label = "Quit", select = Quit });
                break;
            case Page.Pause:
                list.Add(new Item { label = "Resume", select = Resume });
                list.Add(new Item { label = "Settings", select = () => { settingsBack = Page.Pause; Open(Page.Settings); } });
                list.Add(new Item { label = "Quit to Title", select = () => { Paused = false; Time.timeScale = 1f; AudioListener.pause = false; SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); } });
                break;
            case Page.Settings:
                list.Add(Slider("Master Volume", () => EvictionSettings.Master, v => EvictionSettings.Master = v));
                list.Add(Slider("Music Volume", () => EvictionSettings.Music, v => EvictionSettings.Music = v));
                list.Add(Slider("Effects Volume", () => EvictionSettings.Effects, v => EvictionSettings.Effects = v));
                list.Add(new Item
                {
                    label = "Mouse Sensitivity",
                    value = () => EvictionSettings.Sensitivity.ToString("0.00") + "x",
                    adjust = d => { EvictionSettings.Sensitivity = Mathf.Clamp(Mathf.Round((EvictionSettings.Sensitivity + d * 0.25f) * 4f) / 4f, 0.25f, 3f); Changed(); },
                });
                list.Add(Toggle("Invert Y", () => EvictionSettings.InvertY, v => EvictionSettings.InvertY = v));
                list.Add(Toggle("Fullscreen", () => EvictionSettings.Fullscreen, v => EvictionSettings.Fullscreen = v));
                list.Add(Toggle("PS1 Wobble", () => EvictionSettings.Wobble, v => EvictionSettings.Wobble = v));
                list.Add(new Item { label = "Back", select = Back });
                break;
        }
        return list;
    }

    Item Slider(string label, Func<float> get, Action<float> set) => new Item
    {
        label = label,
        value = () => Mathf.RoundToInt(get() * 100f) + "%",
        adjust = d => { set(Mathf.Clamp01(Mathf.Round((get() + d * 0.1f) * 10f) / 10f)); Changed(); },
    };

    Item Toggle(string label, Func<bool> get, Action<bool> set) => new Item
    {
        label = label,
        value = () => get() ? "On" : "Off",
        adjust = d => { set(!get()); Changed(); },
    };

    void Changed()
    {
        EvictionSettings.Apply();
        EvictionSettings.Save();
    }

    void Back()
    {
        if (page == Page.Settings) Open(settingsBack);
        else if (page == Page.Pause) Resume();
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void StopMenuMusic() { if (musicSrc) musicSrc.Stop(); }

    void Play(AudioClip c, float v = 0.6f) { if (uiSource && c) uiSource.PlayOneShot(c, v); }

    // ------------------------------------------------------------------ input

    void Update()
    {
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        var mouse = Mouse.current;
        pageTime += Time.unscaledDeltaTime;
        titleAlpha = Mathf.MoveTowards(titleAlpha, page == Page.Title || (page == Page.Settings && settingsBack == Page.Title) ? 1f : 0f, Time.unscaledDeltaTime * 0.8f);

        if (page == Page.None)
        {
            // Pause: Esc / Start, but not while typing a text or reading a paper (Esc closes those).
            bool esc = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame);
            if (esc && sequence && sequence.InGame && !document.IsOpen && !phone.IsTyping && EscUsedFrame != Time.frameCount) Pause();
            return;
        }
        if (page == Page.Credits) return;

        var items = Items();
        if (items.Count == 0) return;
        sel = Mathf.Clamp(sel, 0, items.Count - 1);

        bool up = (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) || (pad != null && pad.dpad.up.wasPressedThisFrame);
        bool down = (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) || (pad != null && pad.dpad.down.wasPressedThisFrame);
        bool left = (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) || (pad != null && pad.dpad.left.wasPressedThisFrame);
        bool right = (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) || (pad != null && pad.dpad.right.wasPressedThisFrame);
        bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        if (pageTime < 0.15f) { confirm = back = false; } // don't let the key that opened the page also act on it

        // Mouse: hover selects, click activates (left half of a value lowers it, right half raises it).
        var hud = EvictionHud.Instance;
        if (mouse != null && hud && itemRects.Count == items.Count)
        {
            Vector2 mp = mouse.position.ReadValue();
            var hp = new Vector2Int(Mathf.FloorToInt(mp.x / Screen.width * hud.W), Mathf.FloorToInt((1f - mp.y / Screen.height) * hud.H));
            bool moved = (mp - lastMouse).sqrMagnitude > 1f;
            lastMouse = mp;
            for (int i = 0; i < itemRects.Count; i++)
            {
                if (!itemRects[i].Contains(hp)) continue;
                if (moved && sel != i) { sel = i; Play(moveSound, 0.4f); }
                if (mouse.leftButton.wasPressedThisFrame && pageTime > 0.15f)
                {
                    if (items[i].adjust != null) { items[i].adjust(hp.x < itemRects[i].center.x + itemRects[i].width / 4 ? -1 : 1); Play(moveSound); }
                    else if (items[i].select != null) { Play(selectSound); items[i].select(); return; }
                }
            }
        }

        if (up) { sel = (sel + items.Count - 1) % items.Count; Play(moveSound, 0.4f); }
        if (down) { sel = (sel + 1) % items.Count; Play(moveSound, 0.4f); }
        var it = items[sel];
        if (it.adjust != null && (left || right)) { it.adjust(right ? 1 : -1); Play(moveSound); }
        else if (confirm)
        {
            if (it.select != null) { Play(selectSound); it.select(); }
            else if (it.adjust != null) { it.adjust(1); Play(moveSound); }
        }
        else if (back && page != Page.Title) { Play(selectSound); Back(); }
    }

    // ------------------------------------------------------------------ drawing

    static readonly Color32 Panel = new Color32(0, 0, 0, 170);
    static readonly Color32 Highlight = new Color32(150, 40, 36, 220);
    static readonly Color32 TitleCol = new Color32(205, 200, 190, 255);

    void Draw(EvictionHud h)
    {
        itemRects.Clear();
        if (page == Page.None || page == Page.Credits)
        {
            if (titleAlpha > 0f && page == Page.Credits) return;
            if (titleAlpha <= 0f) return;
        }

        if (page == Page.Pause || (page == Page.Settings && settingsBack == Page.Pause))
            h.Rect(0, 0, h.W, h.H, new Color32(0, 0, 0, 140));

        // Title over the rain.
        if (titleAlpha > 0f && page != Page.Pause && !(page == Page.Settings && settingsBack == Page.Pause))
        {
            h.Rect(0, 0, h.W, h.H, new Color32(0, 0, 0, (byte)(60 * titleAlpha)));
            if (page == Page.Title)
            {
                h.TextCentered(42, title, EvictionHud.WithAlpha(TitleCol, titleAlpha), 4, true);
                h.TextCentered(76, subtitle, EvictionHud.WithAlpha(EvictionHud.Grey, titleAlpha), 1, true);
            }
        }
        if (page == Page.None || page == Page.Credits) return;

        var items = Items();
        bool settings = page == Page.Settings;
        int rowH = settings ? 14 : 16;
        int panelW = settings ? 230 : 130;
        int top = page == Page.Title ? 108 : (h.H - items.Count * rowH) / 2 + (settings ? 6 : 8);
        int px = (h.W - panelW) / 2;

        if (page == Page.Pause) h.TextCentered(top - 30, "PAUSED", TitleCol, 2, true);
        if (settings) h.TextCentered(top - 26, "SETTINGS", TitleCol, 2, true);

        h.Rect(px - 6, top - 6, panelW + 12, items.Count * rowH + 10, Panel);
        for (int i = 0; i < items.Count; i++)
        {
            int y = top + i * rowH;
            var rect = new RectInt(px, y - 2, panelW, rowH - 1);
            itemRects.Add(rect);
            bool on = i == sel;
            if (on) h.Rect(rect.x, rect.y, rect.width, rect.height, Highlight);
            var col = on ? EvictionHud.White : new Color32(185, 185, 180, 255);
            if (settings && items[i].value != null)
            {
                h.Text(px + 6, y + 1, items[i].label, col);
                string v = "< " + items[i].value() + " >";
                h.Text(px + panelW - 6 - PSXFont.Measure(v), y + 1, v, col);
            }
            else
            {
                string label = items[i].label;
                h.Text(px + (panelW - PSXFont.Measure(label)) / 2, y + 2, label, col);
            }
        }

        string help = EvictionHud.UsingGamepad
            ? (settings ? "(D-Pad) Change   (A) Select   (B) Back" : "(D-Pad) Move   (A) Select")
            : (settings ? "[Arrows] Change   [Enter] Select   [Esc] Back" : "[Arrows/Mouse] Move   [Enter] Select");
        h.TextCentered(h.H - 16, help, new Color32(150, 150, 150, 220));
    }
}
