using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player settings, saved between sessions (PlayerPrefs) and applied right away:
/// volumes, mouse sensitivity, invert Y, fullscreen, and the PS1 wobble.
/// Music sources register here so they follow the music volume instead of the effects volume.
/// </summary>
public static class EvictionSettings
{
    public static float Master = 1f, Music = 0.8f, Effects = 1f, Sensitivity = 1f;
    public static bool InvertY, Fullscreen = true, Wobble = true;

    static readonly List<(AudioSource src, float baseVolume)> music = new List<(AudioSource, float)>();
    static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        Master = PlayerPrefs.GetFloat("ev.master", 1f);
        Music = PlayerPrefs.GetFloat("ev.music", 0.8f);
        Effects = PlayerPrefs.GetFloat("ev.effects", 1f);
        Sensitivity = PlayerPrefs.GetFloat("ev.sens", 1f);
        InvertY = PlayerPrefs.GetInt("ev.invert", 0) == 1;
        Fullscreen = PlayerPrefs.GetInt("ev.fullscreen", Screen.fullScreen ? 1 : 0) == 1;
        Wobble = PlayerPrefs.GetInt("ev.wobble", 1) == 1;
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat("ev.master", Master);
        PlayerPrefs.SetFloat("ev.music", Music);
        PlayerPrefs.SetFloat("ev.effects", Effects);
        PlayerPrefs.SetFloat("ev.sens", Sensitivity);
        PlayerPrefs.SetInt("ev.invert", InvertY ? 1 : 0);
        PlayerPrefs.SetInt("ev.fullscreen", Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt("ev.wobble", Wobble ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void Apply()
    {
        // Everything but music goes through the listener; music ignores it and is set directly.
        AudioListener.volume = Master * Effects;
        music.RemoveAll(m => !m.src);
        foreach (var (src, baseVolume) in music) src.volume = baseVolume * Master * Music;
        PSXPlayer.SensitivityScale = Sensitivity;
        PSXPlayer.InvertY = InvertY;
        if (!Application.isEditor && Screen.fullScreen != Fullscreen) Screen.fullScreen = Fullscreen;
        Shader.SetGlobalFloat("_PSXNoWobble", Wobble ? 0f : 1f);
    }

    /// <summary>Make <paramref name="src"/> a music source at <paramref name="baseVolume"/> (0-1, before settings).</summary>
    public static void RegisterMusic(AudioSource src, float baseVolume = 1f)
    {
        src.ignoreListenerVolume = true;
        music.RemoveAll(m => m.src == src);
        music.Add((src, baseVolume));
        src.volume = baseVolume * Master * Music;
    }
}
