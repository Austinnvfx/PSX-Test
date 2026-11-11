using UnityEngine;

/// <summary>
/// Rainy night: falling streaks and ground splashes around the player, a light haze, wet shiny
/// surfaces, the looping rain recording, and lightning flashes timed to the thunder in it.
/// One <see cref="intensity"/> slider drives the lot. One per scene.
/// </summary>
[ExecuteAlways]
public class PSXRain : MonoBehaviour
{
    /// <summary>Current wetness 0-1 (footsteps blend dry/wet with it).</summary>
    public static float Wetness { get; private set; }
    /// <summary>Time.time of the most recent lightning flash.</summary>
    public static float LastFlashTime { get; private set; } = -999f;
    /// <summary>0-1: how much to turn the rain down for music (set by PSXAudioDirector).</summary>
    public static float MusicDuck { get; set; }

    [Tooltip("0 = drizzle, 1 = downpour. Drives drops, splashes, haze, wetness and rain volume.")]
    [Range(0f, 1f)] public float intensity = 0.5f;
    [Tooltip("The rain follows this (usually the main camera).")]
    public Transform follow;

    [Header("Drops")]
    public Material rainMaterial;
    public int maxDropsPerSecond = 3200;
    public float fallSpeed = 13f;
    public Vector3 wind = new Vector3(1.2f, 0f, 0.6f);
    [Tooltip("Width of the box of rain around the player, in meters.")]
    public float areaSize = 34f;
    public float dropWidth = 0.025f;
    [Tooltip("How much the drops stretch with speed.")]
    public float dropStretch = 0.05f;

    [Header("Splashes")]
    public Material splashMaterial;
    public int maxSplashesPerSecond = 260;
    public float splashRadius = 14f;
    public float splashSize = 0.16f;

    [Header("Haze")]
    [Tooltip("Fog settings are driven from here while playing.")]
    public PSXManager manager;
    [Range(0f, 0.3f)] public float hazeLight = 0.014f;
    [Range(0f, 0.3f)] public float hazeHeavy = 0.03f;

    [Header("Wetness")]
    [Tooltip("Wetness at intensity 0. Reaches 1 at full intensity.")]
    [Range(0f, 1f)] public float minWetness = 0.55f;

    [Header("Audio")]
    public AudioSource rainSource;
    [Range(0f, 1f)] public float volumeLight = 0.45f;
    [Range(0f, 1f)] public float volumeHeavy = 1f;
    [Tooltip("Under a roof the rain sounds muffled and quieter.")]
    public bool muffleUnderCover = true;
    [Range(0f, 1f)] public float coveredVolume = 0.55f;
    public float coveredCutoff = 900f;

    [Header("Lightning (synced to the thunder in the rain recording)")]
    public bool lightning = true;
    [Range(0f, 2f)] public float flashBrightness = 1f;
    [Tooltip("Seconds into the rain clip where each thunder clap starts.")]
    public float[] thunderTimes = new float[0];
    [Tooltip("0-1 loudness of each clap. Louder = closer = brighter flash, shorter gap before the boom.")]
    public float[] thunderStrengths = new float[0];
    [Tooltip("How long the flash comes before the thunder: x for the closest strikes, y for the farthest.")]
    public Vector2 flashLead = new Vector2(0.25f, 1.3f);

    ParticleSystem drops, splashes;
    AudioLowPassFilter lowPass;
    float cover, coverTarget, nextCoverCheck;

    /// <summary>Is there a roof over the player? Checked a few times a second, eased so it never snaps.</summary>
    void UpdateCover()
    {
        if (!muffleUnderCover || !follow) { cover = 0f; return; }
        if (rainSource && !lowPass)
        {
            lowPass = rainSource.GetComponent<AudioLowPassFilter>();
            if (!lowPass) lowPass = rainSource.gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 22000f;
        }
        if (Time.time >= nextCoverCheck)
        {
            nextCoverCheck = Time.time + 0.2f;
            bool roof = Physics.Raycast(follow.position + Vector3.up * 0.3f, Vector3.up, 40f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            coverTarget = roof ? 1f : 0f;
        }
        cover = Mathf.MoveTowards(cover, coverTarget, Time.deltaTime * 1.5f);
    }
    float prevClipTime = -1f;
    float flashStart = -999f, flashStrength;

    void OnEnable()
    {
        if (Application.isPlaying) CreateParticles();
    }

    void OnDisable()
    {
        if (drops) Destroy(drops.gameObject);
        if (splashes) Destroy(splashes.gameObject);
        Wetness = 0f;
        Shader.SetGlobalVector("_PSXWeather", Vector4.zero);
    }

    void Update()
    {
        Wetness = Mathf.Lerp(minWetness, 1f, intensity);
        float flash = Application.isPlaying ? UpdateLightning() : 0f;
        Shader.SetGlobalVector("_PSXWeather", new Vector4(Wetness, flash, 0f, 0f));

        if (!Application.isPlaying) return;

        if (manager) manager.fogDensity = Mathf.Lerp(hazeLight, hazeHeavy, intensity);
        UpdateCover();
        if (rainSource)
        {
            float vol = Mathf.Lerp(volumeLight, volumeHeavy, intensity);
            rainSource.volume = vol * Mathf.Lerp(1f, coveredVolume, cover) * (1f - MusicDuck);
            if (lowPass) lowPass.cutoffFrequency = Mathf.Lerp(22000f, coveredCutoff, Mathf.Sqrt(cover));
        }

        if (drops)
        {
            var em = drops.emission;
            em.rateOverTime = maxDropsPerSecond * Mathf.Lerp(0.15f, 1f, intensity);
        }
        EmitSplashes();
    }

    void LateUpdate()
    {
        if (!Application.isPlaying || !follow) return;
        if (drops) drops.transform.position = follow.position + Vector3.up * 12f - new Vector3(wind.x, 0f, wind.z) * 0.6f;
    }

    // ------------------------------------------------------------------ lightning

    float UpdateLightning()
    {
        if (lightning && rainSource && rainSource.clip && rainSource.isPlaying && thunderTimes != null)
        {
            float len = rainSource.clip.length;
            float t = rainSource.time;
            if (prevClipTime >= 0f)
            {
                for (int i = 0; i < thunderTimes.Length; i++)
                {
                    float s = i < thunderStrengths.Length ? thunderStrengths[i] : 0.5f;
                    float ft = Mathf.Repeat(thunderTimes[i] - Mathf.Lerp(flashLead.y, flashLead.x, s), len);
                    bool crossed = t >= prevClipTime
                        ? ft > prevClipTime && ft <= t
                        : ft > prevClipTime || ft <= t; // the clip looped this frame
                    if (crossed) { flashStart = Time.time; flashStrength = s; LastFlashTime = Time.time; }
                }
            }
            prevClipTime = t;
        }

        // Flicker-flash-flicker, then a quick fade: how lightning looks through a window.
        float dt = Time.time - flashStart;
        float env;
        if (dt < 0f) env = 0f;
        else if (dt < 0.06f) env = 1f;
        else if (dt < 0.11f) env = 0.2f;
        else if (dt < 0.19f) env = 0.85f;
        else env = Mathf.Exp(-(dt - 0.19f) * 7f) * 0.6f;
        return env * flashStrength * flashBrightness;
    }

    // ------------------------------------------------------------------ particles

    void CreateParticles()
    {
        if (rainMaterial)
        {
            var go = new GameObject("Rain Drops");
            drops = go.AddComponent<ParticleSystem>();
            drops.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = drops.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.startLifetime = 1.5f;
            main.startSpeed = 0f;
            main.startSize = dropWidth;
            main.startColor = Color.white;
            main.maxParticles = 8000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;

            var shape = drops.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(areaSize, 0.1f, areaSize);

            var vel = drops.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(wind.x * 0.8f, wind.x * 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-fallSpeed * 1.1f, -fallSpeed * 0.9f);
            vel.z = new ParticleSystem.MinMaxCurve(wind.z * 0.8f, wind.z * 1.2f);

            // Drops die on whatever they hit first: roofs, awnings, walls, the street.
            var collision = drops.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Low; // cached voxel hits: cheap for thousands of drops
            collision.voxelSize = 0.5f;
            collision.collidesWith = ~(1 << 2); // not the player (Ignore Raycast layer)
            collision.lifetimeLoss = 1f;
            collision.radiusScale = 0.5f;
            collision.enableDynamicColliders = false;
            collision.sendCollisionMessages = false;

            var r = drops.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 1f;
            r.velocityScale = dropStretch;
            r.sharedMaterial = rainMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            drops.Play();
        }

        if (splashMaterial)
        {
            var go = new GameObject("Rain Splashes");
            splashes = go.AddComponent<ParticleSystem>();
            splashes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = splashes.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 0.2f;
            main.startSpeed = 0f;
            main.startSize = splashSize;
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = splashes.emission;
            em.rateOverTime = 0f;
            var splashShape = splashes.shape;
            splashShape.enabled = false;

            var size = splashes.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.3f));

            var col = splashes.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var r = splashes.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = splashMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            splashes.Play();
        }
    }

    float splashBudget;

    void EmitSplashes()
    {
        if (!splashes || !follow) return;
        splashBudget += maxSplashesPerSecond * Mathf.Lerp(0.15f, 1f, intensity) * Time.deltaTime;
        int n = Mathf.Min((int)splashBudget, 40);
        splashBudget -= n;

        var p = new ParticleSystem.EmitParams();
        for (int i = 0; i < n; i++)
        {
            Vector2 c = Random.insideUnitCircle * splashRadius;
            Vector3 from = follow.position + new Vector3(c.x, 25f, c.y);
            if (!Physics.Raycast(from, Vector3.down, out var hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                continue;
            if (hit.normal.y < 0.5f) continue;
            p.position = hit.point + Vector3.up * splashSize * 0.35f;
            p.startSize = splashSize * Random.Range(0.7f, 1.2f);
            splashes.Emit(p, 1);
        }
    }
}
