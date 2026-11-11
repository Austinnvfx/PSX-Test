using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Background audio: music that fades in and out with PSXMusicZones (and picks up where it left off),
/// plus a "dread" drone that swells now and then when no music is playing. One per scene.
/// Rain and thunder live on PSXRain.
/// </summary>
public class PSXAudioDirector : MonoBehaviour
{
    [Tooltip("Where the player hears from (usually the main camera).")]
    public Transform listener;

    [Header("Music")]
    [Tooltip("Used by zones that don't name their own track.")]
    public AudioClip defaultMusic;
    [Range(0f, 1f)] public float musicVolume = 0.8f;
    [Tooltip("How much the rain is turned down while music plays (0 = not at all, 1 = silent).")]
    [Range(0f, 1f)] public float rainDuck = 0.4f;
    [Tooltip("How fast music volume follows the zones, per second.")]
    public float musicFadeSpeed = 0.6f;

    [Header("Dread swells")]
    public AudioClip droneClip;
    [Range(0f, 1f)] public float droneVolume = 0.45f;
    [Tooltip("Seconds between swells (random in this range).")]
    public Vector2 droneInterval = new Vector2(60f, 180f);
    public Vector2 dronePitch = new Vector2(0.88f, 1.04f);
    public float droneFadeIn = 1.5f;
    public float droneFadeOut = 2f;
    [Tooltip("No swell within this many seconds of a lightning flash.")]
    public float quietAfterLightning = 6f;

    readonly Dictionary<AudioClip, AudioSource> musicSources = new Dictionary<AudioClip, AudioSource>();
    readonly Dictionary<AudioClip, float> weights = new Dictionary<AudioClip, float>();
    readonly HashSet<AudioClip> started = new HashSet<AudioClip>();
    AudioSource droneSource;
    float nextDrone;
    float musicWeight;
    bool droning;

    void Start()
    {
        if (!listener && Camera.main) listener = Camera.main.transform;
        droneSource = NewSource("Drone");
        ScheduleDrone();
    }

    void Update()
    {
        if (!listener) return;
        UpdateMusic();
        UpdateDrone();
        PSXRain.MusicDuck = musicWeight * rainDuck; // the rain makes room for the music
    }

    void UpdateMusic()
    {
        weights.Clear();
        musicWeight = 0f;
        foreach (var zone in PSXMusicZone.All)
        {
            if (!zone || !zone.isActiveAndEnabled) continue;
            var clip = zone.track ? zone.track : defaultMusic;
            if (!clip) continue;
            float w = zone.WeightAt(listener.position);
            weights[clip] = Mathf.Max(weights.TryGetValue(clip, out var cur) ? cur : 0f, w);
            musicWeight = Mathf.Max(musicWeight, w);
        }

        foreach (var kv in weights)
            if (kv.Value > 0f && !musicSources.ContainsKey(kv.Key))
            {
                var s = NewSource("Music: " + kv.Key.name);
                s.clip = kv.Key;
                s.loop = true;
                musicSources[kv.Key] = s;
            }

        foreach (var kv in musicSources)
        {
            var s = kv.Value;
            float target = (weights.TryGetValue(kv.Key, out var w) ? w : 0f) * musicVolume;
            s.volume = Mathf.MoveTowards(s.volume, target, musicFadeSpeed * Time.deltaTime);

            if (s.volume > 0.0001f && !s.isPlaying)
            {
                if (started.Add(kv.Key)) s.Play();
                else s.UnPause(); // carry on from where it was
            }
            else if (s.volume <= 0.0001f && target <= 0f && s.isPlaying)
            {
                s.Pause();
            }
        }
    }

    void UpdateDrone()
    {
        if (droning || !droneClip || Time.time < nextDrone) return;
        bool musicOn = musicWeight > 0.05f;
        bool recentFlash = Time.time - PSXRain.LastFlashTime < quietAfterLightning;
        if (musicOn || recentFlash)
        {
            nextDrone = Time.time + 8f; // try again shortly
            return;
        }
        StartCoroutine(PlayDrone());
    }

    IEnumerator PlayDrone()
    {
        droning = true;
        droneSource.clip = droneClip;
        droneSource.pitch = Random.Range(dronePitch.x, dronePitch.y);
        droneSource.volume = 0f;
        droneSource.Play();

        float length = droneClip.length / Mathf.Max(0.01f, droneSource.pitch);
        float t = 0f;
        while (t < length && droneSource.isPlaying)
        {
            t += Time.deltaTime;
            float fade = Mathf.Min(Mathf.Clamp01(t / droneFadeIn), Mathf.Clamp01((length - t) / droneFadeOut));
            droneSource.volume = fade * droneVolume * (1f - musicWeight); // music walking in ducks the swell
            yield return null;
        }
        droneSource.Stop();
        droning = false;
        ScheduleDrone();
    }

    void ScheduleDrone() => nextDrone = Time.time + Random.Range(droneInterval.x, droneInterval.y);

    AudioSource NewSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.volume = 0f;
        return s;
    }
}
