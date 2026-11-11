using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch: -executeMethod EvictionSmokeTest.Run -shotDir &lt;folder&gt; (no -quit).
/// Plays the demo in Play mode without a person: uses each thing in order, types a reply, rides the
/// drive, reads the notice, watches the ending. Logs [Smoke] lines, saves HUD snapshots, exits 0/1.
/// </summary>
[InitializeOnLoad]
public static class EvictionSmokeTest
{
    const string Key = "EvictionSmoke.Running";
    const string DirKey = "EvictionSmoke.Dir";
    static int errors, step;
    static double stepAt;
    static bool sawPlay;

    static EvictionSmokeTest()
    {
        if (!SessionState.GetBool(Key, false)) return;
        Application.logMessageReceived += OnLog;
        EditorApplication.update += Tick;
    }

    public static void Run()
    {
        var args = System.Environment.GetCommandLineArgs();
        int ai = System.Array.IndexOf(args, "-shotDir");
        SessionState.SetString(DirKey, ai >= 0 ? args[ai + 1] : "Library/EvictionSmoke");
        Directory.CreateDirectory(SessionState.GetString(DirKey, ""));
        SessionState.SetBool(Key, true);
        SessionState.SetInt("EvictionSmoke.Errors", 0);
        EditorSceneManager.OpenScene(EvictionBuilder.ScenePath, OpenSceneMode.Single);
        Application.logMessageReceived += OnLog;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error)
        {
            if (msg.Contains("Licensing") || msg.Contains("Access token")) return;
            SessionState.SetInt("EvictionSmoke.Errors", SessionState.GetInt("EvictionSmoke.Errors", 0) + 1);
            Debug.Log("[Smoke] ERROR seen: " + msg);
        }
    }

    static void Log(string s) => Debug.Log("[Smoke] " + s);

    static T Get<T>(object o, string field) => (T)o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(o);
    static void Call(object o, string method, params object[] a) => o.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Invoke(o, a);

    static void Snap(string name)
    {
        var hud = EvictionHud.Instance;
        if (!hud) return;
        var tex = Get<Texture2D>(hud, "tex");
        var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
        var px = tex.GetPixels32();
        // Put the HUD over a mid grey so transparent areas are visible.
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i]; float a = c.a / 255f;
            px[i] = new Color32((byte)(c.r * a + 60 * (1 - a)), (byte)(c.g * a + 64 * (1 - a)), (byte)(c.b * a + 70 * (1 - a)), 255);
        }
        copy.SetPixels32(px);
        copy.Apply();
        File.WriteAllBytes(Path.Combine(SessionState.GetString(DirKey, ""), name + ".png"), copy.EncodeToPNG());
        Object.DestroyImmediate(copy);
        Log("snapshot " + name);
    }

    static void Next(double wait) { step++; stepAt = EditorApplication.timeSinceStartup + wait; }

    static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            if (sawPlay || step > 0)
            {
                int e = SessionState.GetInt("EvictionSmoke.Errors", 0);
                Log(step >= 24 ? $"Play mode ended by the demo itself after step {step}. Errors: {e}" : $"Play mode ended early at step {step}. Errors: {e}");
                SessionState.SetBool(Key, false);
                EditorApplication.update -= Tick;
                EditorApplication.Exit(e == 0 && step >= 24 ? 0 : 1);
            }
            return;
        }
        sawPlay = true;
        if (EditorApplication.timeSinceStartup < stepAt) return;

        var seq = Object.FindFirstObjectByType<EvictionSequence>();
        if (!seq) { Next(0.5); return; }
        var player = seq.player.transform;
        if (step == 22 && !EvictionMenu.Paused) Time.timeScale = 4f;

        var menu = Object.FindFirstObjectByType<EvictionMenu>();
        object Page(string n) => System.Enum.Parse(menu.GetType().GetNestedType("Page", BindingFlags.NonPublic), n);
        string CurPage() => menu.GetType().GetField("page", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(menu).ToString();
        switch (step)
        {
            case 0: Log("title page: " + CurPage() + ", cursor free: " + PSXPlayer.CursorFree); Next(3.5); break;
            case 1: Snap("00_title"); Call(menu, "Open", Page("Settings")); Next(0.5); break;
            case 2: Snap("00b_settings"); Call(menu, "Open", Page("Title")); seq.NewGame(); Next(6.5); break;
            case 3: if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fast") >= 0) { seq.creditsScrollSpeed = 300f; Log("fast: skipping to the notice"); step = 15; Next(0.5); break; }
                Log($"in game: {seq.InGame}, player at {player.position}, fade {seq.hud.fade:0.00}"); Call(menu, "Pause"); Next(0.5); break;
            case 4: Snap("00c_pause"); Log("paused: " + EvictionMenu.Paused + " timeScale " + Time.timeScale); Call(menu, "Resume"); Next(0.5); break;
            case 5: seq.punchClock.Use(); Next(0.8); break;
            case 6: Snap("01_time_card"); seq.exitDoor.Use(); Next(4.5); break;
            case 7: Log($"after leaving: player at {player.position}"); Next(seq.phoneBuzzDelay + 0.5); break;
            case 8: Log("phone unread: " + seq.phone.HasUnread); Snap("02_phone_prompt"); Call(seq.phone, "Open"); Next(0.75); break;
            case 9: Snap("03_new_message"); Next(1.0); break;
            case 10: Snap("03b_thread"); Call(seq.phone, "StartTyping"); Next(0.3); break;
            case 11: foreach (char c in "On my way, love you too!") Call(seq.phone, "OnText", c); Next(0.4); break;
            case 12: Snap("04_typing"); Call(seq.phone, "Send", "On my way, love you too!"); Next(0.8); break;
            case 13: Snap("05_sending"); Next(seq.phone.replyDelay + 3.5); break;
            case 14: Snap("06_reply"); Call(seq.phone, "Close"); Next(1); break;
            case 15: seq.car.Use(); Next(33); break;
            case 16: Log($"after drive: player at {player.position}, fade {seq.hud.fade:0.00}"); seq.notice.Use(); Next(1); break;
            case 17: Snap("08_notice"); seq.document.Close(); Next(seq.pauseBeforeLastText + 1.2); break;
            case 18: Log("last text unread: " + seq.phone.HasUnread); Snap("09_last_prompt"); Call(seq.phone, "Open"); Next(1.6); break;
            case 19: Snap("10_glitch"); Next(0.9); break;
            case 20: Snap("10b_glitch"); Next(1.6); break;
            case 21: Snap("11_settled"); Next(4); break;
            case 22: Log($"after cut: in game {seq.InGame}, fade {seq.hud.fade:0.00}"); Time.timeScale = 1f; Next(12); break;
            case 23: Snap("12_credits"); Next(1); break;
            case 24:
                // The ending reloads the scene into the title screen: wait for that, then stop.
                if (CurPage() == "Title" && !seq.InGame && !seq.RollingCredits) { Log("back at the title screen"); Snap("13_title_again"); EditorApplication.isPlaying = false; Next(9999); }
                else Next(1);
                break;
        }
    }
}
