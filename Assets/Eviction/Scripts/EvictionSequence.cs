using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs EVICTION: title screen -> office (clock out) -> parking lot (her text) -> the drive (black screen,
/// sound only) -> apartment (the notice) -> her phone: "It's time to wake up." -> hard cut -> credits ->
/// thank-you -> back to the title screen. All story text lives here so it can be edited in the Inspector.
/// </summary>
public class EvictionSequence : MonoBehaviour
{
    [Header("Rig")]
    public PSXPlayer player;
    public PSXRain rain;
    public EvictionHud hud;
    public EvictionPhone phone;
    public EvictionDocument document;
    public EvictionMenu menu;
    public AudioSource sfx;
    public AudioSource engine;

    [Header("Places")]
    public Transform officeSpawn;
    public Transform lotSpawn;
    public Transform apartmentSpawn;
    [Tooltip("Title screen camera drifts from A to B, looking at the target.")]
    public Transform titleCamA, titleCamB, titleLookAt;

    [Header("Things to use")]
    public Interactable punchClock;
    public Interactable exitDoor;
    public Interactable car;
    public Interactable notice;

    [Header("Sounds")]
    public AudioClip punchClockClip;
    public AudioClip doorOpen, doorClose;
    public AudioClip carDoorOpen, carDoorShut;
    public AudioClip engineStart, engineIdle, engineDriveAway, engineStop;
    [Tooltip("Optional: a voice-acted landlord call after reading the notice. Empty = skipped.")]
    public AudioClip landlordCall;
    [Tooltip("Optional: music over the credits. Empty = silence.")]
    public AudioClip creditsMusic;

    [Header("Timing")]
    public float phoneBuzzDelay = 4f;
    [Tooltip("Rain-only pause between putting the notice down and her phone buzzing.")]
    public float pauseBeforeLastText = 2f;
    [Tooltip("How long the last message stays on screen before the hard cut.")]
    public float lastTextHold = 2.6f;

    [Header("The last message")]
    public string lastText = "It's time to wake up.";
    public string lastTextTime = "3:12 AM";

    [Header("Punch card")]
    public string[] cardRows =
    {
        "TIME CARD",
        "EMP. NO. 0417",
        "WEEK OF 10/14/02",
        "DAY   IN     OUT",
        "MON  8:58A  6:34P",
        "TUE  9:03A  6:31P",
        "WED  8:55A  6:40P",
        "THU  9:01A  6:29P",
        "FRI  9:04A",
    };
    public string cardStamp = "OUT 6:30 PM";

    [Header("The notice")]
    public string noticeTitle = "NOTICE TO VACATE";
    [TextArea(12, 30)]
    public string noticeBody =
        "Date: October 18, 2002\n" +
        "To: All Occupants, Unit 1B\n" +
        "\n" +
        "You are hereby notified that your tenancy at this address is TERMINATED. You must vacate the premises within THIRTY (30) DAYS of this notice.\n" +
        "\n" +
        "This property has been acquired for redevelopment. Through a community partnership program, new housing has been arranged for your household at NO COST to you.\n" +
        "\n" +
        "A representative will contact you with your new address. Please do not contact the leasing office.\n" +
        "\n" +
        "We thank you for your cooperation.\n" +
        "- Property Management";

    [Header("Ending")]
    [TextArea(10, 40)]
    public string credits =
        "EVICTION\n" +
        "A PSX Horror Concept Demo\n" +
        "\n" +
        "CREATED BY\n" +
        "SOULWXRTH\n" +
        "Austinn.vfx\n" +
        "\n" +
        "STORY & DESIGN\n" +
        "SOULWXRTH & Austinn.vfx\n" +
        "\n" +
        "MUSIC\n" +
        "SOULWXRTH\n" +
        "\n" +
        "3D MODELS\n" +
        "Office Pack by nappin\n" +
        "Simple Retro Car by Polyeler\n" +
        "Low Poly Cars by AWBMECREATIONS\n" +
        "Residential Building by Carthage Studio\n" +
        "Demo City by Versatile Studio\n" +
        "\n" +
        "SOUND EFFECTS (PIXABAY)\n" +
        "freesound_community\n" +
        "universfield\n" +
        "hookedmedia\n" +
        "dragon-studio\n" +
        "musicnation782\n" +
        "matthewvakaliuk73627\n" +
        "rubyland1\n" +
        "soundsforstory\n" +
        "\n" +
        "S13 ENGINE SOUNDS\n" +
        "Initial D Wiki\n" +
        "\n" +
        "MADE WITH UNITY";
    [TextArea(4, 12)]
    public string thankYou =
        "THANK YOU FOR PLAYING\n" +
        "\n" +
        "This is our first small look at EVICTION.\n" +
        "Thank you for taking the time to play it.\n" +
        "It means more than you know.\n" +
        "\n" +
        "More soon.\n" +
        "- SOULWXRTH & Austinn.vfx";
    public float creditsScrollSpeed = 14f;

    /// <summary>True from New Game until the hard cut (the pause menu only works then).</summary>
    public bool InGame { get; private set; }
    public bool RollingCredits => rolling != Roll.None;

    bool clockedOut, leaving, driving, read;
    bool inTitle;
    AudioSource music;

    enum Roll { None, Credits, ThankYou }
    Roll rolling;
    float rollAlpha, creditsY;
    List<string> creditLines, thankLines;
    string caption;
    float captionA;

    void Start()
    {
        if (!hud) hud = EvictionHud.Instance;
        hud.AddLayer(50, DrawOverlay);
        hud.fade = 1f;
        EvictionSettings.Load();
        EvictionSettings.Apply();

        music = gameObject.AddComponent<AudioSource>();
        music.ignoreListenerPause = true;
        music.playOnAwake = false;
        music.spatialBlend = 0f;
        EvictionSettings.RegisterMusic(music);

        punchClock.Used += ClockOut;
        exitDoor.available = false;
        exitDoor.Used += () => { if (!leaving) StartCoroutine(LeaveOffice()); };
        car.Used += () => { if (!driving) StartCoroutine(DriveHome()); };
        notice.Used += () => { if (!read) ReadNotice(); };

        if (menu) EnterTitle();
        else NewGame();
    }

    // ------------------------------------------------------------------ title

    void EnterTitle()
    {
        inTitle = true;
        PlayerInteractor.Blocked = true;
        player.enabled = false;
        player.GetComponent<CharacterController>().enabled = false;
        StartCoroutine(TitleCamera());
        StartCoroutine(FadeTo(0f, 2.5f));
        menu.ShowTitle();
    }

    IEnumerator TitleCamera()
    {
        var pivot = player.cameraPivot;
        float t = 0f;
        while (inTitle)
        {
            t += Time.unscaledDeltaTime;
            float k = 0.5f - 0.5f * Mathf.Cos(t * 2f * Mathf.PI / 70f); // slow drift there and back
            Vector3 eye = Vector3.Lerp(titleCamA.position, titleCamB.position, k);
            player.transform.position = eye - pivot.localPosition;
            var look = titleLookAt.position - eye;
            var flat = new Vector3(look.x, 0f, look.z);
            player.transform.rotation = Quaternion.LookRotation(flat);
            pivot.localRotation = Quaternion.Euler(-Mathf.Atan2(look.y, flat.magnitude) * Mathf.Rad2Deg, 0f, 0f);
            yield return null;
        }
    }

    /// <summary>Called by the title menu.</summary>
    public void NewGame() => StartCoroutine(BeginGame());

    IEnumerator BeginGame()
    {
        if (inTitle) yield return FadeTo(1f, 1.2f);
        inTitle = false;
        hud.fade = 1f;
        yield return null;
        player.GetComponent<CharacterController>().enabled = true;
        player.enabled = true;
        player.Teleport(officeSpawn.position, officeSpawn.forward);
        PSXPlayer.CursorFree = false;
        PSXPlayer.SetCursorLocked(true);
        PlayerInteractor.Blocked = false;
        InGame = true;
        yield return new WaitForSeconds(0.8f);
        yield return FadeTo(0f, 3f);
    }

    // ------------------------------------------------------------------ 1. office

    void ClockOut()
    {
        if (clockedOut) return;
        clockedOut = true;
        punchClock.available = false;
        punchClock.lockedText = "";
        sfx.PlayOneShot(punchClockClip);
        document.ShowCard(cardRows, cardStamp, 3.2f);
        exitDoor.available = true;
    }

    IEnumerator LeaveOffice()
    {
        leaving = true;
        exitDoor.available = false;
        exitDoor.lockedText = "";
        PSXPlayer.InputLocked = true;
        sfx.PlayOneShot(doorOpen);
        yield return FadeTo(1f, 0.8f);
        player.Teleport(lotSpawn.position, lotSpawn.forward);
        yield return new WaitForSeconds(0.9f);
        sfx.PlayOneShot(doorClose);
        yield return new WaitForSeconds(0.3f);
        PSXPlayer.InputLocked = false;
        yield return FadeTo(0f, 1.5f);
        yield return new WaitForSeconds(phoneBuzzDelay);
        phone.Notify();
    }

    // ------------------------------------------------------------------ 2-3. the drive

    IEnumerator DriveHome()
    {
        driving = true;
        car.available = false;
        phone.ForceClose();
        PSXPlayer.InputLocked = true;
        PlayerInteractor.Blocked = true;
        sfx.PlayOneShot(carDoorOpen);
        yield return FadeTo(1f, 1.2f);
        PSXRain.MusicDuck = 0.55f; // inside the car the rain is muffled

        yield return new WaitForSeconds(0.4f);
        sfx.PlayOneShot(carDoorShut);
        yield return new WaitForSeconds(1.4f);

        engine.PlayOneShot(engineStart);
        yield return new WaitForSeconds(Mathf.Max(0.5f, engineStart.length - 0.6f));
        engine.clip = engineIdle;
        engine.loop = true;
        engine.volume = 0.8f;
        engine.Play();
        yield return new WaitForSeconds(2.6f);

        // Pull away: the idle hands off to the drive recording, which fades into the distance.
        StartCoroutine(FadeSource(engine, 0f, 0.6f));
        sfx.PlayOneShot(engineDriveAway, 0.75f);
        yield return new WaitForSeconds(Mathf.Min(engineDriveAway.length, 11.5f));
        engine.Stop();
        engine.volume = 1f;

        caption = "20 minutes later";
        for (float t = 0; t < 1f; t += Time.deltaTime) { captionA = t; yield return null; }
        captionA = 1f;
        yield return new WaitForSeconds(2.4f);
        for (float t = 0; t < 1f; t += Time.deltaTime) { captionA = 1f - t; yield return null; }
        captionA = 0f;
        yield return new WaitForSeconds(0.6f);

        sfx.PlayOneShot(engineStop, 0.9f);
        yield return new WaitForSeconds(Mathf.Min(engineStop.length, 5.2f));
        sfx.PlayOneShot(carDoorShut);
        yield return new WaitForSeconds(1.2f);

        player.Teleport(apartmentSpawn.position, apartmentSpawn.forward);
        PSXRain.MusicDuck = 0f;
        PSXPlayer.InputLocked = false;
        PlayerInteractor.Blocked = false;
        yield return FadeTo(0f, 2.2f);
    }

    // ------------------------------------------------------------------ 4. the notice, her last text

    void ReadNotice()
    {
        read = true;
        notice.available = false;
        document.Open(noticeTitle, noticeBody, () => StartCoroutine(AfterNotice()));
    }

    IEnumerator AfterNotice()
    {
        if (landlordCall)
        {
            // Hook for a voice-acted landlord call (not used in this demo).
            PSXPlayer.InputLocked = true;
            yield return new WaitForSeconds(1.5f);
            sfx.PlayOneShot(landlordCall);
            yield return new WaitForSeconds(landlordCall.length + 0.5f);
            PSXPlayer.InputLocked = false;
        }
        yield return new WaitForSeconds(pauseBeforeLastText);

        // Her phone again. The player has to open it.
        phone.NotifyScripted(lastText, lastTextTime, true);
        while (phone.LatestSeenAt < 0f) yield return null;
        yield return new WaitForSeconds(0.5f);
        var retro = Camera.main ? Camera.main.GetComponent<PSXRetroCamera>() : null;
        if (retro) retro.Tear(0.35f);
        yield return new WaitForSeconds(Mathf.Max(0.4f, lastTextHold - 0.5f));
        if (retro) retro.Tear(0.15f);
        yield return new WaitForSeconds(0.15f);

        // Hard cut: picture and sound gone at once.
        InGame = false;
        hud.fade = 1f;
        phone.ForceClose();
        PSXPlayer.InputLocked = true;
        PlayerInteractor.Blocked = true;
        AudioListener.pause = true;
        yield return new WaitForSecondsRealtime(2.5f);
        yield return RollCredits(false);

        // Back to the title screen.
        AudioListener.pause = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ------------------------------------------------------------------ credits

    /// <summary>Credits from the title menu (Esc/(B) skips). Calls back when done.</summary>
    public void PlayCreditsFromMenu(System.Action done) => StartCoroutine(MenuCredits(done));

    IEnumerator MenuCredits(System.Action done)
    {
        yield return FadeTo(1f, 0.8f);
        yield return RollCredits(true);
        yield return FadeTo(0f, 1.5f);
        done?.Invoke();
    }

    IEnumerator RollCredits(bool skippable)
    {
        if (creditsMusic) { music.clip = creditsMusic; music.loop = false; music.Play(); }
        bool Skip()
        {
            if (!skippable) return false;
            var kb = Keyboard.current; var pad = Gamepad.current;
            return (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) ||
                   (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame));
        }

        creditLines = new List<string>(credits.Replace("\r", "").Split('\n'));
        rolling = Roll.Credits;
        rollAlpha = 1f;
        creditsY = hud.H + 4;
        float end = -(creditLines.Count * 14) - 10;
        bool skipped = false;
        while (creditsY > end)
        {
            creditsY -= creditsScrollSpeed * Time.unscaledDeltaTime;
            if (Skip()) { skipped = true; break; }
            yield return null;
        }

        if (!skipped)
        {
            thankLines = new List<string>(thankYou.Replace("\r", "").Split('\n'));
            rolling = Roll.ThankYou;
            for (float t = 0; t < 2f; t += Time.unscaledDeltaTime) { rollAlpha = t / 2f; if (Skip()) break; yield return null; }
            rollAlpha = 1f;
            for (float t = 0; t < 7f; t += Time.unscaledDeltaTime) { if (Skip()) break; yield return null; }
            for (float t = 0; t < 2.5f; t += Time.unscaledDeltaTime) { rollAlpha = 1f - t / 2.5f; yield return null; }
        }
        rolling = Roll.None;
        if (music.isPlaying) yield return FadeSource(music, 0f, 1.5f);
        music.Stop();
        EvictionSettings.RegisterMusic(music); // restore its volume for next time
        yield return new WaitForSecondsRealtime(0.8f);
    }

    void DrawOverlay(EvictionHud h)
    {
        if (!string.IsNullOrEmpty(caption) && captionA > 0f)
            h.TextCentered(h.H / 2 - 4, caption, EvictionHud.WithAlpha(EvictionHud.White, captionA));

        if (rolling == Roll.Credits)
        {
            int y = Mathf.RoundToInt(creditsY);
            bool heading = true;
            foreach (var line in creditLines)
            {
                if (line.Length == 0) { y += 14; heading = true; continue; }
                if (y > -12 && y < h.H)
                {
                    float edge = Mathf.Clamp01(Mathf.Min(y, h.H - 10 - y) / 30f);
                    bool isHeading = heading && line.ToUpperInvariant() == line;
                    h.TextCentered(y, line, EvictionHud.WithAlpha(isHeading ? EvictionHud.Grey : EvictionHud.White, edge * rollAlpha));
                }
                heading = false;
                y += 14;
            }
        }
        else if (rolling == Roll.ThankYou)
        {
            int y = h.H / 2 - thankLines.Count * 6;
            for (int i = 0; i < thankLines.Count; i++)
            {
                var c = i == 0 ? new Color32(200, 196, 188, 255) : EvictionHud.White;
                h.TextCentered(y + i * 12, thankLines[i], EvictionHud.WithAlpha(c, rollAlpha));
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    IEnumerator FadeTo(float target, float seconds)
    {
        float start = hud.fade;
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
        {
            hud.fade = Mathf.Lerp(start, target, t / seconds);
            yield return null;
        }
        hud.fade = target;
    }

    static IEnumerator FadeSource(AudioSource s, float target, float seconds)
    {
        float start = s.volume;
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
        {
            s.volume = Mathf.Lerp(start, target, t / seconds);
            yield return null;
        }
        s.volume = target;
    }
}
