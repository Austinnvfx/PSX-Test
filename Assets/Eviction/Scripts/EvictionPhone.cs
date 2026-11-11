using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Early-2000s flip phone, drawn on the pixel HUD. Buzzes with a text, Q to check it, R to reply
/// (type on the keyboard, Enter to send; gamepads pick a preset), and the contact writes back.
/// Messages show as boxes: theirs on the left, yours (dark) on the right, each with name and time.
/// The story can also push scripted messages (e.g. the glitching "It's time to wake up.").
/// </summary>
public class EvictionPhone : MonoBehaviour
{
    [Header("Messages")]
    public string contactName = "My Love";
    [TextArea] public string incomingMessage = "Hey hun, just gonna grab some groceries on my way home, you'll probably get home before me.";
    public string incomingTime = "6:34 PM";
    [TextArea] public string replyFromContact = "Be safe, love you <3";
    public string replyTime = "6:35 PM";
    [Tooltip("Seconds between sending and her answer arriving.")]
    public float replyDelay = 3.5f;
    public int maxChars = 160;
    [Tooltip("What controller players can send (no keyboard to type with).")]
    public string[] gamepadReplies = { "Ok, see you at home.", "Love you too. Drive safe.", "Sounds good, I'm heading out now." };

    [Header("Sounds")]
    public AudioSource source;
    public AudioClip buzz, flipOpen, flipClose, sent;
    public AudioClip[] keyClicks;
    [Range(0f, 1f)] public float buzzVolume = 0.55f;

    public bool IsOpen => state != State.Hidden && state != State.Closing;
    public bool HasUnread => unread;
    public bool IsTyping => state == State.Typing;
    /// <summary>The player sent a reply (for later story beats).</summary>
    public string SentReply { get; private set; }
    /// <summary>Time.time when the newest message first appeared on an open screen (-1 = not seen yet).</summary>
    public float LatestSeenAt { get; private set; } = -1f;

    enum State { Hidden, Opening, Splash, Read, Typing, Sending, Sent, Closing }

    class Msg { public bool me; public string text, time; public bool glitch; public float shownAt = -1f; }

    State state = State.Hidden;
    bool unread, everNotified, replyPending, replied, replyAllowed = true;
    float slide;          // 0 hidden .. 1 fully up
    bool flipped;         // false = closed clamshell, true = open
    readonly StringBuilder typed = new StringBuilder();
    int presetIndex;
    float stateTime;
    int lastKey = -1;
    int scroll;           // pixels scrolled up from the newest message
    int maxScroll;
    readonly List<Msg> thread = new List<Msg>();
    Coroutine buzzLoop;

    void Start()
    {
        var hud = EvictionHud.Instance;
        if (hud) hud.AddLayer(20, Draw);
    }

    void OnEnable()
    {
        if (Keyboard.current != null) Keyboard.current.onTextInput += OnText;
    }

    void OnDisable()
    {
        if (Keyboard.current != null) Keyboard.current.onTextInput -= OnText;
        PSXPlayer.InputLocked = false;
        PlayerInteractor.Blocked = false;
    }

    /// <summary>Her grocery text arrives: buzz until the player checks it.</summary>
    public void Notify() => Receive(incomingMessage, incomingTime, false, true);

    /// <summary>A message from the contact that can't be replied to (story beats).</summary>
    public void NotifyScripted(string text, string time, bool glitch) => Receive(text, time, glitch, false);

    void Receive(string text, string time, bool glitch, bool allowReply)
    {
        everNotified = true;
        unread = true;
        replyAllowed = allowReply && !replied;
        thread.Add(new Msg { me = false, text = text, time = time, glitch = glitch });
        LatestSeenAt = -1f;
        scroll = 0;
        if (buzzLoop != null) StopCoroutine(buzzLoop);
        buzzLoop = StartCoroutine(BuzzUntilRead());
    }

    /// <summary>Put the phone away now (e.g. getting into the car).</summary>
    public void ForceClose()
    {
        if (state == State.Hidden) return;
        PSXPlayer.InputLocked = false;
        state = State.Hidden;
        slide = 0f;
        PlayerInteractor.Blocked = false;
    }

    IEnumerator BuzzUntilRead()
    {
        for (int round = 0; round < 6 && unread; round++)
        {
            for (int i = 0; i < 2 && unread; i++)
            {
                Play(buzz, buzzVolume);
                yield return new WaitForSeconds(1.25f);
            }
            float wait = 6f;
            while (wait > 0f && unread) { wait -= Time.deltaTime; yield return null; }
        }
        buzzLoop = null;
    }

    void Play(AudioClip clip, float volume = 1f)
    {
        if (source && clip) source.PlayOneShot(clip, volume);
    }

    // ------------------------------------------------------------------ input

    void Update()
    {
        var hud = EvictionHud.Instance;
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        var mouse = Mouse.current;
        if (EvictionMenu.Paused) return;
        stateTime += Time.deltaTime;

        bool phoneKey = (kb != null && kb.qKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame);
        bool backKey = pad != null && pad.buttonEast.wasPressedThisFrame;

        switch (state)
        {
            case State.Hidden:
                slide = Mathf.MoveTowards(slide, 0f, Time.deltaTime * 4f);
                if (!everNotified || PSXPlayer.InputLocked || PlayerInteractor.Blocked) break;
                if (unread && hud) hud.SetSecondaryTooltip(EvictionHud.Key("Q", "Y") + " Check phone");
                if (phoneKey) Open();
                break;

            case State.Opening:
                slide = Mathf.MoveTowards(slide, 1f, Time.deltaTime * 4.5f);
                if (slide >= 1f && !flipped) { flipped = true; Play(flipOpen); }
                if (flipped && stateTime > 0.4f)
                {
                    if (unread) { unread = false; SetState(State.Splash); }
                    else SetState(State.Read);
                }
                break;

            case State.Splash: // "1 New Message"
                if (stateTime > 0.9f) SetState(State.Read);
                break;

            case State.Read:
                MarkLatestSeen();
                // Scroll back through the conversation.
                int step = 0;
                if (kb != null && kb.upArrowKey.wasPressedThisFrame) step += 10;
                if (kb != null && kb.downArrowKey.wasPressedThisFrame) step -= 10;
                if (pad != null && pad.dpad.up.wasPressedThisFrame) step += 10;
                if (pad != null && pad.dpad.down.wasPressedThisFrame) step -= 10;
                if (mouse != null) { float wheel = mouse.scroll.ReadValue().y; if (wheel > 0) step += 10; else if (wheel < 0) step -= 10; }
                scroll = Mathf.Clamp(scroll + step, 0, maxScroll);

                bool canReply = replyAllowed && !replied && !replyPending;
                if (hud) hud.SetTooltip(canReply
                    ? EvictionHud.Key("R", "X") + " Reply   " + EvictionHud.Key("Q", "B") + " Close"
                    : EvictionHud.Key("Q", "B") + " Close");
                if (phoneKey || backKey) Close();
                else if (canReply && ((kb != null && kb.rKey.wasPressedThisFrame) || (pad != null && pad.buttonWest.wasPressedThisFrame)))
                    StartTyping();
                break;

            case State.Typing:
                UpdateTyping(kb, pad, hud);
                break;

            case State.Sending:
                if (stateTime > 1.6f) { Play(sent, 0.8f); SetState(State.Sent); }
                break;

            case State.Sent:
                if (stateTime > 1.2f)
                {
                    PSXPlayer.InputLocked = false;
                    scroll = 0;
                    SetState(State.Read);
                    StartCoroutine(ReplyArrives());
                }
                break;

            case State.Closing:
                slide = Mathf.MoveTowards(slide, 0f, Time.deltaTime * 4.5f);
                if (slide <= 0f) { state = State.Hidden; PlayerInteractor.Blocked = false; }
                break;
        }
    }

    void MarkLatestSeen()
    {
        if (thread.Count == 0) return;
        var last = thread[thread.Count - 1];
        if (last.shownAt < 0f) last.shownAt = Time.time;
        if (LatestSeenAt < 0f && scroll == 0) LatestSeenAt = Time.time;
    }

    void SetState(State s) { state = s; stateTime = 0f; }

    void Open()
    {
        flipped = false;
        scroll = 0;
        SetState(State.Opening);
        PlayerInteractor.Blocked = true;
    }

    void Close()
    {
        Play(flipClose);
        flipped = false;
        SetState(State.Closing);
    }

    void StartTyping()
    {
        typed.Clear();
        presetIndex = 0;
        SetState(State.Typing);
        PSXPlayer.InputLocked = true;
    }

    void UpdateTyping(Keyboard kb, Gamepad pad, EvictionHud hud)
    {
        if (EvictionHud.UsingGamepad)
        {
            if (hud) hud.SetTooltip("(Up/Down) Choose   (A) Send   (B) Back");
            if (pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame) { presetIndex = (presetIndex + gamepadReplies.Length - 1) % gamepadReplies.Length; Click(); }
                if (pad.dpad.down.wasPressedThisFrame) { presetIndex = (presetIndex + 1) % gamepadReplies.Length; Click(); }
                if (pad.buttonSouth.wasPressedThisFrame && gamepadReplies.Length > 0) { Send(gamepadReplies[presetIndex]); return; }
                if (pad.buttonEast.wasPressedThisFrame) { CancelTyping(); return; }
            }
        }
        else if (hud) hud.SetTooltip("[Enter] Send   [Esc] Cancel");

        if (kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame) { EvictionMenu.EscUsedFrame = Time.frameCount; CancelTyping(); return; }
        if (kb.backspaceKey.wasPressedThisFrame && typed.Length > 0) { typed.Length--; Click(); }
        if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) && typed.ToString().Trim().Length > 0)
            Send(typed.ToString().Trim());
    }

    void OnText(char c)
    {
        if (state != State.Typing || stateTime < 0.05f || EvictionHud.UsingGamepad) return; // ignore the R that opened the editor
        if (c < 32 || c > 126 || typed.Length >= maxChars) return;
        if (!PSXFont.Has(c)) c = '?';
        typed.Append(c);
        Click();
    }

    void Click()
    {
        if (keyClicks == null || keyClicks.Length == 0) return;
        int i = Random.Range(0, keyClicks.Length);
        if (keyClicks.Length > 1 && i == lastKey) i = (i + 1) % keyClicks.Length;
        lastKey = i;
        Play(keyClicks[i], 0.5f);
    }

    void CancelTyping()
    {
        PSXPlayer.InputLocked = false;
        SetState(State.Read);
    }

    void Send(string text)
    {
        SentReply = text;
        thread.Add(new Msg { me = true, text = text, time = replyTime });
        replyPending = true;
        SetState(State.Sending);
    }

    IEnumerator ReplyArrives()
    {
        yield return new WaitForSeconds(replyDelay);
        replyPending = false;
        replied = true;
        replyAllowed = false;
        thread.Add(new Msg { me = false, text = replyFromContact, time = replyTime });
        scroll = 0;
        Play(buzz, buzzVolume);
        if (state == State.Hidden) // phone is away: it buzzes until checked again
        {
            unread = true;
            if (buzzLoop != null) StopCoroutine(buzzLoop);
            buzzLoop = StartCoroutine(BuzzUntilRead());
        }
    }

    // ------------------------------------------------------------------ drawing

    static readonly Color32 Body = new Color32(52, 56, 64, 255);
    static readonly Color32 BodyLight = new Color32(88, 94, 106, 255);
    static readonly Color32 BodyDark = new Color32(26, 28, 33, 255);
    static readonly Color32 Lcd = new Color32(160, 194, 156, 255);
    static readonly Color32 LcdMid = new Color32(132, 166, 130, 255);
    static readonly Color32 LcdDark = new Color32(104, 136, 104, 255);
    static readonly Color32 Ink = new Color32(22, 38, 26, 255);
    static readonly Color32 KeyCol = new Color32(150, 154, 162, 255);

    const int PhoneW = 172, TopH = 158, ScreenW = 156, ScreenH = 130;

    void Draw(EvictionHud hud)
    {
        if (slide <= 0.001f && state == State.Hidden) return;
        float e = 1f - Mathf.Pow(1f - slide, 3f);
        int x = hud.W / 2 + 24 - PhoneW / 2;
        int restY = flipped ? 6 : hud.H - 112;
        int y = Mathf.RoundToInt(Mathf.Lerp(hud.H + 4, restY, e));

        if (!flipped)
        {
            // Closed clamshell: the lid, with the little outside display.
            hud.RoundRect(x, y, PhoneW, 160, Body, 6);
            hud.RoundRect(x + 3, y + 3, PhoneW - 6, 154, BodyLight, 5);
            hud.RoundRect(x + 6, y + 6, PhoneW - 12, 148, Body, 4);
            hud.Rect(x + 60, y + 26, 52, 32, BodyDark);
            hud.Rect(x + 62, y + 28, 48, 28, unread ? Lcd : LcdDark);
            Envelope(hud, x + 79, y + 37, Ink);
            return;
        }

        // Open: lid with the screen on top, keypad below (runs off the bottom of the screen).
        hud.RoundRect(x, y, PhoneW, TopH, Body, 6);
        hud.RoundRect(x + 2, y + 2, PhoneW - 4, TopH - 4, BodyLight, 5);
        hud.RoundRect(x + 4, y + 4, PhoneW - 8, TopH - 8, Body, 4);
        hud.Rect(x + PhoneW / 2 - 10, y + 7, 20, 2, BodyDark); // earpiece
        int sx = x + (PhoneW - ScreenW) / 2, sy = y + 14;
        hud.Rect(sx - 2, sy - 2, ScreenW + 4, ScreenH + 4, BodyDark);
        hud.Rect(sx, sy, ScreenW, ScreenH, Lcd);
        DrawScreen(hud, sx, sy);

        int ky = y + TopH + 4;
        hud.Rect(x + 10, y + TopH - 2, PhoneW - 20, 8, BodyDark); // hinge
        hud.RoundRect(x + 2, ky, PhoneW - 4, 160, Body, 6);
        hud.RoundRect(x + 5, ky + 3, PhoneW - 10, 154, BodyLight, 5);
        hud.RoundRect(x + 7, ky + 5, PhoneW - 14, 150, Body, 4);
        hud.RoundRect(x + 16, ky + 10, 32, 10, KeyCol, 2);
        hud.RoundRect(x + PhoneW - 48, ky + 10, 32, 10, KeyCol, 2);
        hud.RoundRect(x + PhoneW / 2 - 17, ky + 8, 34, 22, KeyCol, 4);
        hud.Rect(x + PhoneW / 2 - 4, ky + 15, 8, 8, BodyLight);
        for (int r = 0; r < 4; r++)
        for (int c = 0; c < 3; c++)
        {
            int kx = x + 26 + c * 42, kyy = ky + 38 + r * 22;
            hud.RoundRect(kx, kyy, 32, 15, KeyCol, 3);
            string label = r < 3 ? (r * 3 + c + 1).ToString() : (c == 0 ? "*" : c == 1 ? "0" : "#");
            hud.Text(kx + 13, kyy + 4, label, BodyDark);
        }
    }

    void DrawScreen(EvictionHud hud, int sx, int sy)
    {
        // Status bar: signal, title, battery.
        for (int i = 0; i < 4; i++) hud.Rect(sx + 3 + i * 3, sy + 9 - i * 2, 2, 2 + i * 2, Ink);
        hud.Frame(sx + ScreenW - 16, sy + 3, 12, 7, Ink);
        hud.Rect(sx + ScreenW - 4, sy + 5, 1, 3, Ink);
        hud.Rect(sx + ScreenW - 14, sy + 5, 6, 3, Ink);
        hud.Rect(sx, sy + 13, ScreenW, 1, LcdDark);

        int bodyTop = sy + 16, bodyBottom = sy + ScreenH - 15;

        if (state == State.Splash)
        {
            Title(hud, sx, sy, "Messages");
            bool blink = (int)(stateTime * 4f) % 2 == 0;
            int ex = sx + ScreenW / 2 - 10, ey = sy + 34;
            if (blink) BigEnvelope(hud, ex, ey, Ink);
            Center(hud, sx, sy + 60, "1 New Message");
            Center(hud, sx, sy + 74, "From: " + contactName);
            return;
        }

        if (state == State.Sending || state == State.Sent)
        {
            Title(hud, sx, sy, "Messages");
            if (state == State.Sending)
            {
                int dots = (int)(stateTime * 3f) % 4;
                Center(hud, sx, sy + 46, "Sending" + new string('.', dots));
                int bw = ScreenW - 30;
                hud.Frame(sx + 15, sy + 62, bw, 7, Ink);
                hud.Rect(sx + 17, sy + 64, Mathf.RoundToInt((bw - 4) * Mathf.Clamp01(stateTime / 1.5f)), 3, Ink);
            }
            else
            {
                Check(hud, sx + ScreenW / 2 - 6, sy + 38, Ink);
                Center(hud, sx, sy + 58, "Message sent");
            }
            return;
        }

        if (state == State.Typing)
        {
            Title(hud, sx, sy, "To: " + contactName);
            if (EvictionHud.UsingGamepad)
            {
                int ly = bodyTop + 2;
                for (int i = 0; i < gamepadReplies.Length; i++)
                {
                    var lines = PSXFont.Wrap(gamepadReplies[i], (ScreenW - 18) / PSXFont.Advance);
                    int bh = lines.Count * PSXFont.LineHeight + 5;
                    bool sel = i == presetIndex;
                    hud.Rect(sx + 4, ly, ScreenW - 8, bh, sel ? Ink : LcdMid);
                    hud.Frame(sx + 4, ly, ScreenW - 8, bh, Ink);
                    for (int l = 0; l < lines.Count; l++) hud.Text(sx + 8, ly + 3 + l * PSXFont.LineHeight, lines[l], sel ? Lcd : Ink);
                    ly += bh + 3;
                }
            }
            else
            {
                // Context: the message being answered, then the input box.
                int cols = (ScreenW - 14) / PSXFont.Advance;
                bool cursorOn = (int)(stateTime * 2.5f) % 2 == 0;
                var lines = PSXFont.Wrap(typed.ToString() + (cursorOn ? "_" : " "), cols);
                int shown = Mathf.Min(lines.Count, 5);
                int boxH = Mathf.Max(2, shown) * PSXFont.LineHeight + 14;
                int boxY = bodyBottom - boxH - 1;
                DrawThread(hud, sx, bodyTop, boxY - 3, true);
                hud.Rect(sx + 3, boxY, ScreenW - 6, boxH, new Color32(176, 208, 172, 255));
                hud.Frame(sx + 3, boxY, ScreenW - 6, boxH, Ink);
                string counter = (maxChars - typed.Length).ToString();
                hud.Text(sx + ScreenW - 7 - PSXFont.Measure(counter), boxY + 3, counter, LcdDark);
                int first = lines.Count - shown;
                for (int i = first; i < lines.Count; i++)
                    hud.Text(sx + 7, boxY + 12 + (i - first) * PSXFont.LineHeight, lines[i], Ink);
            }
            SoftKeys(hud, sx, sy, "Send", "Back");
            return;
        }

        // Reading: the conversation as message boxes, newest at the bottom.
        Title(hud, sx, sy, contactName);
        DrawThread(hud, sx, bodyTop, bodyBottom, false);
        bool canReply = replyAllowed && !replied && !replyPending;
        SoftKeys(hud, sx, sy, canReply ? "Reply" : "", "Back");
    }

    /// <summary>Message boxes laid out from the bottom up, clipped to [top, bottom), scrolled by <see cref="scroll"/>.</summary>
    void DrawThread(EvictionHud hud, int sx, int top, int bottom, bool compact)
    {
        int maxText = (ScreenW - 30) / PSXFont.Advance;
        var clip = new RectInt(sx, top, ScreenW, Mathf.Max(0, bottom - top));
        int y = bottom + (compact ? 0 : scroll);
        int contentH = 0;
        for (int m = thread.Count - 1; m >= 0; m--)
        {
            var msg = thread[m];
            string text = GlitchText(msg);
            var lines = PSXFont.Wrap(text, maxText);
            int textW = 0;
            foreach (var l in lines) textW = Mathf.Max(textW, PSXFont.Measure(l));
            int bw = textW + 10, bh = lines.Count * PSXFont.LineHeight + 5;
            int header = 10;
            int block = header + bh + 5;
            y -= block;
            contentH += block;
            if (y + block < top) continue;

            int jitter = GlitchActive(msg) ? Random.Range(-2, 3) : 0;
            int bx = msg.me ? sx + ScreenW - 4 - bw : sx + 4;
            bx += jitter;
            hud.Clip = clip;
            // Header: name and time.
            string head = (msg.me ? "Me" : contactName) + "  " + msg.time;
            int hx = msg.me ? sx + ScreenW - 4 - PSXFont.Measure(head) : sx + 4;
            hud.Text(hx, y, head, LcdDark);
            // The box: theirs light with a border, yours filled dark.
            int by = y + header;
            Color32 fill = msg.me ? Ink : new Color32(186, 216, 180, 255);
            Color32 textCol = msg.me ? Lcd : Ink;
            hud.Rect(bx + 1, by, bw - 2, bh, fill);
            hud.Rect(bx, by + 1, bw, bh - 2, fill);
            if (!msg.me)
            {
                hud.Rect(bx + 1, by, bw - 2, 1, Ink); hud.Rect(bx + 1, by + bh - 1, bw - 2, 1, Ink);
                hud.Rect(bx, by + 1, 1, bh - 2, Ink); hud.Rect(bx + bw - 1, by + 1, 1, bh - 2, Ink);
                hud.Rect(bx - 2, by + bh - 4, 2, 1, Ink); hud.Rect(bx - 1, by + bh - 3, 1, 1, Ink); // little tail
            }
            else
            {
                hud.Rect(bx + bw, by + bh - 4, 2, 1, Ink); hud.Rect(bx + bw, by + bh - 3, 1, 1, Ink);
            }
            if (GlitchActive(msg) && Random.value < 0.5f)
                for (int l = 0; l < lines.Count; l++)
                    hud.Text(bx + 5 + 1, by + 3 + l * PSXFont.LineHeight, lines[l], new Color32(170, 40, 40, 160));
            for (int l = 0; l < lines.Count; l++)
                hud.Text(bx + 5, by + 3 + l * PSXFont.LineHeight, lines[l], textCol);
            hud.Clip = null;
        }
        if (!compact)
        {
            maxScroll = Mathf.Max(0, contentH - (bottom - top));
            if (scroll < maxScroll) // more above: little arrow
            {
                int ax = sx + ScreenW - 8, ay = top + 1;
                hud.Rect(ax + 2, ay, 1, 1, Ink); hud.Rect(ax + 1, ay + 1, 3, 1, Ink); hud.Rect(ax, ay + 2, 5, 1, Ink);
            }
        }
    }

    // The "It's time to wake up." message: wrong letters flicker for a moment, then it settles.
    const float GlitchSeconds = 1.6f;
    bool GlitchActive(Msg m) => m.glitch && m.shownAt >= 0f && Time.time - m.shownAt < GlitchSeconds;

    string GlitchText(Msg m)
    {
        if (!m.glitch) return m.text;
        if (m.shownAt < 0f || Time.time - m.shownAt < 0.5f) return "It's tme to w4ke up.";
        if (!GlitchActive(m)) return m.text;
        var chars = m.text.ToCharArray();
        int seed = (int)(Time.time * 12f);
        var rng = new System.Random(seed);
        const string junk = "#%&?/4!|_";
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] != ' ' && rng.NextDouble() < 0.18) chars[i] = junk[rng.Next(junk.Length)];
        return new string(chars);
    }

    void Title(EvictionHud hud, int sx, int sy, string t) => hud.Text(sx + (ScreenW - PSXFont.Measure(t)) / 2, sy + 3, t, Ink);

    void Center(EvictionHud hud, int sx, int y, string t) => hud.Text(sx + (ScreenW - PSXFont.Measure(t)) / 2, y, t, Ink);

    void SoftKeys(EvictionHud hud, int sx, int sy, string left, string right)
    {
        int y = sy + ScreenH - 13;
        hud.Rect(sx, y - 2, ScreenW, 1, LcdDark);
        if (!string.IsNullOrEmpty(left))
        {
            int w = PSXFont.Measure(left) + 8;
            hud.RoundRect(sx + 3, y, w, 11, Ink, 2);
            hud.Text(sx + 7, y + 1, left, Lcd);
        }
        if (!string.IsNullOrEmpty(right))
        {
            int w = PSXFont.Measure(right) + 8;
            hud.RoundRect(sx + ScreenW - 3 - w, y, w, 11, Ink, 2);
            hud.Text(sx + ScreenW + 1 - w, y + 1, right, Lcd);
        }
    }

    static void Envelope(EvictionHud hud, int x, int y, Color32 c)
    {
        hud.Frame(x, y, 14, 10, c);
        for (int i = 0; i < 6; i++) { hud.Pixel(x + 1 + i, y + 1 + i, c); hud.Pixel(x + 12 - i, y + 1 + i, c); }
    }

    static void BigEnvelope(EvictionHud hud, int x, int y, Color32 c)
    {
        hud.Frame(x, y, 21, 15, c);
        hud.Frame(x + 1, y + 1, 19, 13, c);
        for (int i = 0; i < 9; i++) { hud.Rect(x + 2 + i, y + 2 + i, 1, 1, c); hud.Rect(x + 18 - i, y + 2 + i, 1, 1, c); }
    }

    static void Check(EvictionHud hud, int x, int y, Color32 c)
    {
        for (int i = 0; i < 4; i++) hud.Rect(x + i, y + 6 + i, 2, 2, c);
        for (int i = 0; i < 8; i++) hud.Rect(x + 4 + i, y + 9 - i, 2, 2, c);
    }
}
