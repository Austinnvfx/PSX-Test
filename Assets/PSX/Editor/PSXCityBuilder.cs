using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Menu: PSX > Build City Scene. Turns the Versatile Studio "Demo City" into a rainy PSX night:
/// copies its layout into Assets/Scenes/PSXCity.unity, swaps every material for a PSX/Lit copy with
/// shrunken pixelated textures, drops the baked lighting, puts PSX lamps on the lamp posts, adds
/// colliders, rain, audio, music zones around the factories and street signs leading to them.
/// The pack's own files are never modified. Safe to re-run.
/// Batch: Unity.exe -batchmode -projectPath . -executeMethod PSXCityBuilder.BuildBatch -quit
/// </summary>
public static class PSXCityBuilder
{
    const string PackRoot = "Assets/Versatile Studio Assets/Demo City By Versatile Studio";
    const string SourceScene = PackRoot + "/Scenes/demo_city_night.unity";
    const string CityRoot = "Assets/PSX/City";
    const string CityMatDir = CityRoot + "/Materials";
    const string CityTexDir = CityRoot + "/Textures";
    const string CityMeshPath = CityRoot + "/PSXCityMeshes.asset";
    const string PSXMatDir = "Assets/PSX/Materials";
    const string PSXTexDir = "Assets/PSX/Textures";
    const string AudioDir = "Assets/PSX/Audio";
    public const string ScenePath = "Assets/Scenes/PSXCity.unity";

    const float AffineAmount = 0.15f;
    // Every lamp in the city: sodium-vapor yellow, like the old test street.
    static readonly Color SodiumYellow = new Color(1f, 0.72f, 0.35f);
    const double FlickerChance = 1.0 / 25.0;
    const float MaxRouteLength = 450f;

    // Thunder claps found in rain_city_loop.ogg (seconds, loudness in dB of the low end).
    static readonly float[] ThunderTimes = { 5.00f, 40.00f, 52.00f, 98.50f, 141.75f, 172.00f, 196.00f, 209.50f, 221.00f, 240.50f, 261.75f, 315.00f, 326.50f, 343.50f, 359.25f, 377.50f, 390.25f, 403.00f, 450.25f, 462.75f, 504.50f, 518.75f, 534.25f, 572.25f };
    static readonly float[] ThunderDb = { -22, -35, -16, -27, -30, -19, -35, -31, -34, -33, -21, -21, -37, -26, -37, -30, -33, -21, -29, -21, -26, -38, -26, -18 };

    [MenuItem("PSX/Build City Scene")]
    public static void BuildMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    public static void BuildBatch() => Build();

    // ===================================================================================== build

    static void Build()
    {
        if (!File.Exists(SourceScene))
            throw new FileNotFoundException("Import 'Demo City By Versatile Studio' first. Missing: " + SourceScene);

        foreach (var d in new[] { CityMatDir, CityTexDir, PSXTexDir }) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();
        ConfigureAudioImport();

        var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);

        // ---------- Lighting: PSX shaders do their own, so drop everything baked ----------
        Lightmapping.lightingDataAsset = null;
        LightmapSettings.lightmaps = new LightmapData[0];
        RenderSettings.fog = false;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;

        var lampSpecs = CollectLampSpecs();
        foreach (var l in Find<Light>()) { if (IsInPrefab(l)) l.enabled = false; else Object.DestroyImmediate(l); }
        foreach (var p in Find<ReflectionProbe>()) { if (IsInPrefab(p)) p.enabled = false; else Object.DestroyImmediate(p); }
        foreach (var g in Find<LightProbeGroup>()) { if (IsInPrefab(g)) g.enabled = false; else Object.DestroyImmediate(g); }
        foreach (var c in Find<Camera>()) if (!IsInPrefab(c)) Object.DestroyImmediate(c.gameObject);
        foreach (var a in Find<AudioListener>()) if (a && !IsInPrefab(a)) Object.DestroyImmediate(a);

        // ---------- Materials ----------
        var matMap = new Dictionary<Material, Material>();
        int renderers = 0;
        foreach (var r in Find<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!m || m.shader.name.StartsWith("PSX/")) continue;
                if (!matMap.TryGetValue(m, out var psx)) matMap[m] = psx = ConvertMaterial(m);
                mats[i] = psx;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderers++;
        }
        Debug.Log($"[PSXCity] Converted {matMap.Count} materials on {renderers} renderers.");

        // ---------- Colliders (the pack only has one, for the ground) ----------
        var walkBounds = new Bounds();
        bool haveBounds = false;
        int colliders = 0;
        foreach (var mf in Find<MeshFilter>())
        {
            if (!mf.sharedMesh) continue;
            var mr = mf.GetComponent<MeshRenderer>();
            if (!mr || IsBackground(mf.gameObject)) continue;
            if (!haveBounds) { walkBounds = mr.bounds; haveBounds = true; } else walkBounds.Encapsulate(mr.bounds);
            if (mf.GetComponent<Collider>()) continue;
            if (mr.sharedMaterials.All(m => m && (m.name.Contains("vegetation_main") || m.name.Contains("_light")))) continue;
            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            colliders++;
        }
        Debug.Log($"[PSXCity] Added {colliders} mesh colliders. Walkable area: {walkBounds.size.x:0}m x {walkBounds.size.z:0}m.");

        var city = new GameObject("PSX City").transform;
        AddBoundaryWalls(city, walkBounds);

        // ---------- Lamps ----------
        var lampsRoot = new GameObject("Lamps").transform;
        lampsRoot.SetParent(city);
        var buzzClip = Load<AudioClip>(AudioDir + "/streetlamp_flicker.ogg");
        var rng = new System.Random(1999);
        var flickerRoots = new HashSet<GameObject>();
        var buzzRoots = new HashSet<GameObject>();
        foreach (var spec in lampSpecs)
            if (!spec.onBuilding && !flickerRoots.Contains(spec.root) && rng.NextDouble() < FlickerChance)
                flickerRoots.Add(spec.root);
        foreach (var spec in lampSpecs)
        {
            var go = new GameObject(spec.onBuilding ? "Wall Lamp" : "Street Lamp");
            go.transform.SetParent(lampsRoot);
            go.transform.position = spec.position;
            var lamp = go.AddComponent<PSXLamp>();
            lamp.color = SodiumYellow;
            lamp.intensity = spec.onBuilding ? 2.2f : 3f;
            lamp.range = spec.onBuilding ? 13f : 20f;
            lamp.haloStrength = spec.onBuilding ? 0.25f : 0.45f; // lots of lamps close together: keep halos subtle
            lamp.bulbOffset = Vector3.zero;
            lamp.glowRenderer = spec.glowRenderer;
            lamp.glowMaterialIndex = spec.glowMaterialIndex;
            if (flickerRoots.Contains(spec.root))
            {
                lamp.flicker = true;
                if (buzzClip && buzzRoots.Add(spec.root)) // one buzz per pole
                {
                    var src = go.AddComponent<AudioSource>();
                    src.clip = buzzClip;
                    src.loop = true;
                    src.playOnAwake = true;
                    src.volume = 0f;
                    src.spatialBlend = 1f;
                    src.rolloffMode = AudioRolloffMode.Linear;
                    src.minDistance = 1.5f;
                    src.maxDistance = 12f;
                    src.dopplerLevel = 0f;
                    lamp.buzzSource = src;
                }
            }
        }
        Debug.Log($"[PSXCity] {lampSpecs.Count} PSX lamps, {flickerRoots.Count} posts flickering.");

        // ---------- Route: spawn far away, walk to the facility ----------
        var factories = FindFactoryClusters();
        Debug.Log($"[PSXCity] {factories.Count} factory group(s).");

        // The facility is the biggest factory group you can actually walk to.
        var navInstance = BuildNavMesh(walkBounds);
        Route route = null;
        Cluster facility = null;
        foreach (var f in factories)
        {
            route = FindRoute(lampSpecs, f);
            if (route != null) { facility = f; break; }
        }
        NavMesh.RemoveNavMeshData(navInstance);
        if (facility != null)
        {
            factories.Remove(facility);
            factories.Insert(0, facility);
            Debug.Log($"[PSXCity] Facility at {facility.center} (radius {facility.radius:0}m, {facility.count} buildings).");
        }
        else facility = factories.Count > 0 ? factories[0] : new Cluster { center = walkBounds.center, radius = 20f };

        // ---------- Music zones ----------
        var zonesRoot = new GameObject("Music Zones").transform;
        zonesRoot.SetParent(city);
        for (int i = 0; i < factories.Count; i++)
        {
            var f = factories[i];
            var zgo = new GameObject(i == 0 ? "Music Zone - The Facility" : "Music Zone - Factories " + i);
            zgo.transform.SetParent(zonesRoot);
            zgo.transform.position = f.center;
            var z = zgo.AddComponent<PSXMusicZone>();
            z.innerRadius = f.radius + 10f;
            z.outerRadius = f.radius + 45f;
            if (i == 0 && route != null)
            {
                // Full volume from 10m before the end of the route, fading in over the 40m before that.
                float arrive = Vector3.Distance(Flat(f.center), Flat(route.points[route.points.Count - 1]));
                z.innerRadius = Mathf.Max(z.innerRadius, arrive + 10f);
                z.outerRadius = z.innerRadius + 40f;
            }
        }

        // ---------- Signs ----------
        if (route != null) PlaceSigns(city, route, facility);

        // ---------- PSX rig ----------
        var manager = new GameObject("PSX Settings").AddComponent<PSXManager>();
        manager.fogColor = new Color(0.085f, 0.095f, 0.115f);
        manager.fogDensity = 0.022f;
        manager.fogNoise = 0.15f;
        manager.fogNoiseScale = 0.08f;
        manager.fogWind = new Vector3(1.2f, 0.05f, 0.6f);
        manager.ambient = new Color(0.16f, 0.17f, 0.2f);

        Vector3 spawn = route != null ? route.points[0] : walkBounds.center;
        Vector3 facing = route != null && route.points.Count > 1 ? Flat(route.points[1] - route.points[0]) : Vector3.forward;
        var cam = BuildPlayer(spawn, facing, manager);

        var weather = new GameObject("PSX Weather");
        var rain = weather.AddComponent<PSXRain>();
        rain.follow = cam.transform;
        rain.manager = manager;
        rain.rainMaterial = RainMaterial("PSX_Rain", RainStreakTex(), new Color(0.55f, 0.6f, 0.7f, 0.32f));
        rain.splashMaterial = RainMaterial("PSX_RainSplash", RainSplashTex(), new Color(0.6f, 0.65f, 0.75f, 0.5f));
        rain.thunderTimes = ThunderTimes;
        rain.thunderStrengths = ThunderDb.Select(db => Mathf.Clamp(Mathf.InverseLerp(-40f, -16f, db), 0.25f, 1f)).ToArray();
        var rainSrc = weather.AddComponent<AudioSource>();
        rainSrc.clip = Load<AudioClip>(AudioDir + "/rain_city_loop.ogg");
        rainSrc.loop = true;
        rainSrc.playOnAwake = true;
        rainSrc.spatialBlend = 0f;
        rainSrc.volume = 0.7f;
        rain.rainSource = rainSrc;

        var director = new GameObject("PSX Audio").AddComponent<PSXAudioDirector>();
        director.listener = cam.transform;
        director.defaultMusic = Load<AudioClip>(AudioDir + "/the_facility_outskirts.ogg");
        director.droneClip = Load<AudioClip>(AudioDir + "/ambient_drone.ogg");

        // ---------- Save ----------
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.Refresh();
        Debug.Log("[PSXCity] Done: " + ScenePath + (route != null ? $". Route to the facility: {route.length:0}m." : ". No route found!"));
    }

    // ===================================================================================== lamps

    class LampSpec
    {
        public GameObject root;
        public Vector3 position, basePosition;
        public Color color;
        public bool onBuilding;
        public Renderer glowRenderer;
        public int glowMaterialIndex = -1;
    }

    static List<LampSpec> CollectLampSpecs()
    {
        var list = new List<LampSpec>();
        foreach (var l in Find<Light>())
        {
            if (l.type != LightType.Spot) continue;
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(l.gameObject);
            if (!root) continue;
            string src = SourceName(root);
            bool pole = src.StartsWith("lamp_pole"), wall = src.StartsWith("building_lamp");
            if (!pole && !wall) continue;

            var spec = new LampSpec
            {
                root = root,
                position = l.transform.position,
                basePosition = root.transform.position,
                color = l.color,
                onBuilding = wall,
            };
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] && mats[i].name.Contains("_light")) { spec.glowRenderer = r; spec.glowMaterialIndex = i; }
            }
            list.Add(spec);
        }
        return list;
    }

    // ===================================================================================== materials

    static Material ConvertMaterial(Material m)
    {
        string name = string.Concat(m.name.Split(Path.GetInvalidFileNameChars()));
        string path = $"{CityMatDir}/PSX_{name}.mat";
        var p = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!p)
        {
            p = new Material(Shader.Find("PSX/Lit"));
            AssetDatabase.CreateAsset(p, path);
        }
        p.shader = Shader.Find("PSX/Lit");
        string lower = m.name.ToLowerInvariant();
        bool atlas = lower.Contains("building") || lower.Contains("highrise") || lower.Contains("facade") || lower.Contains("interior");

        bool ground = lower.Contains("asphalt") || lower.Contains("sideway") || lower.Contains("tile");
        var main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        // No mipmaps anywhere (they blur the distance). Ground gets extra-small textures instead,
        // which shimmer far less at a distance and look chunkier up close.
        p.SetTexture("_BaseMap", PSXTexture(main, atlas ? 512 : ground ? 128 : 256));
        if (m.HasProperty("_MainTex"))
        {
            p.SetTextureScale("_BaseMap", m.GetTextureScale("_MainTex"));
            p.SetTextureOffset("_BaseMap", m.GetTextureOffset("_MainTex"));
        }
        p.SetColor("_BaseColor", m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white);

        var e = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
        var emap = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;
        bool emissive = e.maxColorComponent > 0.001f && (m.IsKeywordEnabled("_EMISSION") || emap || lower.Contains("light"));
        bool bulb = lower.EndsWith("_light"); // white/orange/pink/blue lamp bulbs -> all sodium yellow
        if (bulb && emissive) e = SodiumYellow * e.maxColorComponent;
        p.SetColor("_EmissionColor", emissive ? e : Color.black);
        p.SetTexture("_EmissionMap", emissive && emap ? PSXTexture(emap, atlas ? 512 : 256) : null);

        p.SetFloat("_AffineAmount", AffineAmount);
        p.SetFloat("_SnapAmount", 1f);
        p.SetFloat("_WetAmount",
            lower.Contains("asphalt") || lower.Contains("sideway") || lower.Contains("tile") || lower.Contains("road") ? 1f :
            lower.Contains("light") ? 0f :
            lower.Contains("vegetation") ? 0.2f : 0.4f);

        int mode = m.HasProperty("_Mode") ? Mathf.RoundToInt(m.GetFloat("_Mode")) : 0;
        int queue = m.renderQueue;
        bool cutout = mode == 1 || (queue >= 2450 && queue < 3000);
        bool transparent = mode >= 2 || queue >= 3000;

        p.SetFloat("_AlphaClip", cutout ? 1 : 0);
        if (cutout) p.EnableKeyword("_ALPHATEST_ON"); else p.DisableKeyword("_ALPHATEST_ON");
        p.SetFloat("_Cutoff", m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f);
        p.SetFloat("_Cull", cutout ? 0f : 2f);
        p.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
        p.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
        p.SetFloat("_ZWrite", transparent ? 0f : 1f);
        p.renderQueue = transparent ? (int)RenderQueue.Transparent : cutout ? (int)RenderQueue.AlphaTest : -1;

        EditorUtility.SetDirty(p);
        Debug.Log($"[PSXCity]   {m.name} ({m.shader.name}) -> PSX_{name}" +
                  (cutout ? " [cutout]" : transparent ? " [glass]" : "") + (emissive ? " [glow]" : ""));
        return p;
    }

    /// <summary>Copy a pack texture into Assets/PSX/City/Textures, shrunk and point-filtered. Original untouched.</summary>
    /// <param name="mips">Mipmaps (still point-filtered) stop big tiled ground textures from sparkling far away.</param>
    static Texture PSXTexture(Texture src, int maxSize, bool mips = false)
    {
        if (!src) return null;
        string srcPath = AssetDatabase.GetAssetPath(src);
        if (string.IsNullOrEmpty(srcPath) || !srcPath.StartsWith("Assets/")) return src;
        string dst = $"{CityTexDir}/{Path.GetFileName(srcPath)}";
        if (!File.Exists(dst)) AssetDatabase.CopyAsset(srcPath, dst);

        var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
        imp.textureType = TextureImporterType.Default;
        imp.maxTextureSize = maxSize;
        imp.filterMode = FilterMode.Point;
        imp.mipmapEnabled = mips;
        imp.anisoLevel = 0;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
    }

    static Material RainMaterial(string name, Texture2D tex, Color color)
    {
        string path = $"{PSXMatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(Shader.Find("PSX/Rain"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("PSX/Rain");
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", color);
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D RainStreakTex() => Tex("RainStreak", 4, 32, (x, y) =>
    {
        float a = Mathf.Pow(y / 31f, 1.5f) * (x == 0 || x == 3 ? 0.5f : 1f); // bright head, fading tail
        return new Color(1, 1, 1, a);
    });

    static Texture2D RainSplashTex() => Tex("RainSplash", 16, 16, (x, y) =>
    {
        // A little crown of droplets: half ring opening upward plus a couple of flying specks.
        float dx = (x - 7.5f) / 7.5f, dy = (y - 2f) / 7f;
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        bool ring = y >= 2 && r > 0.6f && r < 0.95f && (x + y) % 3 != 0;
        bool speck = (x == 3 && y == 12) || (x == 12 && y == 11) || (x == 8 && y == 14);
        return new Color(1, 1, 1, ring || speck ? 1f : 0f);
    });

    static Texture2D Tex(string name, int w, int h, System.Func<int, int, Color> fn)
    {
        string path = $"{PSXTexDir}/{name}.png";
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            tex.SetPixel(x, y, fn(x, y));
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.filterMode = FilterMode.Point;
        imp.mipmapEnabled = false;
        imp.alphaIsTransparency = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ===================================================================================== audio

    static void ConfigureAudioImport()
    {
        void Set(string path, AudioClipLoadType load, bool mono)
        {
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!imp) { Debug.LogWarning("[PSXCity] Missing audio: " + path); return; }
            var s = imp.defaultSampleSettings;
            s.loadType = load;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            s.preloadAudioData = load != AudioClipLoadType.Streaming;
            imp.defaultSampleSettings = s;
            imp.forceToMono = mono;
            imp.loadInBackground = load == AudioClipLoadType.Streaming;
            imp.SaveAndReimport();
        }

        Set(AudioDir + "/rain_city_loop.ogg", AudioClipLoadType.Streaming, false);
        Set(AudioDir + "/the_facility_outskirts.ogg", AudioClipLoadType.Streaming, false);
        Set(AudioDir + "/ambient_drone.ogg", AudioClipLoadType.CompressedInMemory, false);
        Set(AudioDir + "/streetlamp_flicker.ogg", AudioClipLoadType.CompressedInMemory, true);
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir + "/Footsteps" }))
            Set(AssetDatabase.GUIDToAssetPath(guid), AudioClipLoadType.DecompressOnLoad, true);
    }

    static AudioClip[] LoadSteps(string prefix) =>
        AssetDatabase.FindAssets(prefix + " t:AudioClip", new[] { AudioDir + "/Footsteps" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetFileName(p).StartsWith(prefix))
            .OrderBy(p => p)
            .Select(Load<AudioClip>)
            .ToArray();

    // ===================================================================================== player

    static Camera BuildPlayer(Vector3 spawn, Vector3 facing, PSXManager manager)
    {
        var player = new GameObject("Player");
        player.layer = 2; // Ignore Raycast: rain splashes shouldn't land on your head
        player.transform.position = spawn + Vector3.up * 0.1f;
        player.transform.rotation = Quaternion.LookRotation(facing.sqrMagnitude > 0.001f ? facing.normalized : Vector3.forward);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.75f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0, 0.875f, 0);
        cc.stepOffset = 0.35f;

        var pivot = new GameObject("Camera Pivot").transform;
        pivot.SetParent(player.transform, false);
        pivot.localPosition = new Vector3(0, 1.6f, 0);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        camGo.transform.SetParent(pivot, false);
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 130f; // the haze is thinner than the old fog, so see further
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = manager.fogColor;
        cam.allowMSAA = false;
        cam.allowHDR = false;
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = false;
        camData.antialiasing = AntialiasingMode.None;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<PSXRetroCamera>();

        // Haze "sky" dome riding with the camera so lamp halos and lightning show against open sky.
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "Fog Dome";
        Object.DestroyImmediate(dome.GetComponent<Collider>());
        dome.transform.SetParent(camGo.transform, false);
        dome.transform.localScale = Vector3.one * 240f; // radius 120, inside the 130m far plane
        var domeRenderer = dome.GetComponent<MeshRenderer>();
        domeRenderer.sharedMaterial = Load<Material>(PSXMatDir + "/PSX_FogSky.mat");
        domeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        domeRenderer.receiveShadows = false;

        var flashGo = new GameObject("Flashlight");
        flashGo.transform.SetParent(pivot, false);
        flashGo.transform.localPosition = new Vector3(0.22f, -0.25f, 0.15f);
        var flash = flashGo.AddComponent<PSXFlashlight>();
        flash.follow = camGo.transform;
        flash.range = 22f;

        var pc = player.AddComponent<PSXPlayer>();
        pc.cameraPivot = pivot;
        var steps = player.AddComponent<AudioSource>();
        steps.playOnAwake = false;
        steps.spatialBlend = 0f;
        pc.footstepSource = steps;
        pc.dryFootsteps = LoadSteps("step_dry_");
        pc.wetFootsteps = LoadSteps("step_wet_");

        manager.cameras = new[] { cam };
        return cam;
    }

    static void AddBoundaryWalls(Transform parent, Bounds b)
    {
        var root = new GameObject("Boundary Walls").transform;
        root.SetParent(parent);
        float h = 60f, t = 2f;
        Vector3 c = b.center, s = b.size;
        void Wall(Vector3 pos, Vector3 size)
        {
            var go = new GameObject("Invisible Wall");
            go.transform.SetParent(root);
            go.transform.position = pos;
            go.AddComponent<BoxCollider>().size = size;
        }
        float y = b.min.y + h * 0.5f - 5f;
        Wall(new Vector3(c.x, y, b.min.z - t * 0.5f), new Vector3(s.x + t * 2, h, t));
        Wall(new Vector3(c.x, y, b.max.z + t * 0.5f), new Vector3(s.x + t * 2, h, t));
        Wall(new Vector3(b.min.x - t * 0.5f, y, c.z), new Vector3(t, h, s.z + t * 2));
        Wall(new Vector3(b.max.x + t * 0.5f, y, c.z), new Vector3(t, h, s.z + t * 2));
    }

    // ===================================================================================== facility & route

    class Cluster { public Vector3 center; public float radius; public int count; public List<Bounds> parts = new List<Bounds>(); }

    static List<Cluster> FindFactoryClusters()
    {
        var parts = new List<Bounds>();
        var seen = new HashSet<GameObject>();
        foreach (var r in Find<MeshRenderer>())
        {
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
            if (!root || !seen.Add(root)) continue;
            string src = SourceName(root);
            if (!src.StartsWith("factory_") || src.Contains("_bgr")) continue;
            var rs = root.GetComponentsInChildren<MeshRenderer>();
            var b = rs[0].bounds;
            foreach (var x in rs) b.Encapsulate(x.bounds);
            parts.Add(b);
        }

        // Single-link clustering: factories within 60m of each other belong to the same group.
        var clusters = new List<Cluster>();
        var used = new bool[parts.Count];
        for (int i = 0; i < parts.Count; i++)
        {
            if (used[i]) continue;
            var c = new Cluster();
            var stack = new Stack<int>();
            stack.Push(i); used[i] = true;
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                c.parts.Add(parts[k]);
                for (int j = 0; j < parts.Count; j++)
                    if (!used[j] && Vector3.Distance(Flat(parts[k].center), Flat(parts[j].center)) < 60f) { used[j] = true; stack.Push(j); }
            }
            var all = c.parts[0];
            foreach (var p in c.parts) all.Encapsulate(p);
            c.center = new Vector3(all.center.x, all.min.y, all.center.z);
            c.radius = Mathf.Max(all.extents.x, all.extents.z);
            c.count = c.parts.Count;
            clusters.Add(c);
        }
        return clusters.OrderByDescending(c => c.count).ThenByDescending(c => c.radius).ToList();
    }

    static NavMeshDataInstance BuildNavMesh(Bounds bounds)
    {
        bounds.Expand(new Vector3(4f, 40f, 4f));
        var sources = new List<NavMeshBuildSource>();
        UnityEngine.AI.NavMeshBuilder.CollectSources(bounds, ~(1 << 2), NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        var settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = 0.4f;
        settings.agentClimb = 0.35f;
        settings.overrideVoxelSize = true;
        settings.voxelSize = 0.2f;
        var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        Debug.Log($"[PSXCity] NavMesh built from {sources.Count} sources.");
        return NavMesh.AddNavMeshData(data);
    }

    class Route { public List<Vector3> points; public float length; }

    static Route FindRoute(List<LampSpec> lamps, Cluster facility)
    {
        // The goal: walkable ground next to the facility, on the side people would arrive from.
        // Ring around the facility at street level (not its roofs); keep the spot most streets reach.
        var goals = new List<Vector3>();
        for (float r = facility.radius + 4f; r < facility.radius + 40f; r += 6f)
            for (int a = 0; a < 16; a++)
            {
                float ang = a / 16f * Mathf.PI * 2f;
                var p = facility.center + new Vector3(Mathf.Cos(ang) * r, 0.5f, Mathf.Sin(ang) * r);
                if (NavMesh.SamplePosition(p, out var hit, 5f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - facility.center.y) < 2.5f)
                    goals.Add(hit.position);
            }
        if (goals.Count == 0) { Debug.Log($"[PSXCity] Factories at {facility.center}: no walkable ground around them, skipping."); return null; }
        var probe = new NavMeshPath();
        Vector3 goal = goals.OrderByDescending(g => lamps.Count(l =>
            !l.onBuilding && Vector3.Distance(l.basePosition, g) < MaxRouteLength * 0.8f &&
            NavMesh.SamplePosition(l.basePosition + Vector3.up * 0.3f, out var s, 3f, NavMesh.AllAreas) &&
            NavMesh.CalculatePath(s.position, g, NavMesh.AllAreas, probe) && probe.status == NavMeshPathStatus.PathComplete))
            .First();
        Debug.Log($"[PSXCity] Facility entrance at {goal} (from {goals.Count} candidates).");

        Route best = null;
        var path = new NavMeshPath();
        foreach (var l in lamps)
        {
            if (l.onBuilding) continue;
            if (!NavMesh.SamplePosition(l.basePosition + Vector3.up * 0.3f, out var start, 3f, NavMesh.AllAreas)) continue;
            if (Mathf.Abs(start.position.y - goal.y) > 3f) continue; // street level, not up on the highway
            if (!NavMesh.CalculatePath(start.position, goal, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            float len = PathLength(path.corners);
            if (len > MaxRouteLength) continue;
            if (best == null || len > best.length) best = new Route { points = path.corners.ToList(), length = len };
        }
        if (best == null) Debug.Log($"[PSXCity] Factories at {facility.center}: no lamp-lit street connects, skipping.");
        else Debug.Log($"[PSXCity] Spawn {best.points[0]}, route {best.length:0}m with {best.points.Count} corners.");
        return best;
    }

    static float PathLength(IList<Vector3> pts)
    {
        float len = 0f;
        for (int i = 1; i < pts.Count; i++) len += Vector3.Distance(pts[i - 1], pts[i]);
        return len;
    }

    // ===================================================================================== signs

    static Mesh postMesh, plateMesh;

    static void PlaceSigns(Transform parent, Route route, Cluster facility)
    {
        BuildSignMeshes();
        var root = new GameObject("Signs").transform;
        root.SetParent(parent);
        var signMat = SignMaterial("PSX_Sign", Color.white);
        var postMat = SignMaterial("PSX_SignPost", new Color(0.36f, 0.37f, 0.39f));
        var placed = new List<Vector3>();

        var pts = Simplify(route.points, 3f);
        var dist = new float[pts.Count]; // distance from each point to the goal along the route
        for (int i = pts.Count - 2; i >= 0; i--) dist[i] = dist[i + 1] + Vector3.Distance(pts[i], pts[i + 1]);

        string Meters(float d) => $"{Mathf.Max(10, Mathf.RoundToInt(d / 10f) * 10)}M";

        bool TryPlace(Vector3 at, Vector3 travelDir, PSXSign.Style style, string l1, string l2)
        {
            if (placed.Any(p => Vector3.Distance(p, at) < 14f)) return false;
            travelDir = Flat(travelDir).normalized;
            var right = Vector3.Cross(Vector3.up, travelDir);
            // Stand it at the side of the way, clear of walls, on walkable ground.
            foreach (float side in new[] { 6f, -6f, 4.5f, -4.5f, 3f, -3f })
            {
                var p = at + right * side;
                if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.8f) continue;
                if (Physics.CheckCapsule(hit.point + Vector3.up * 0.3f, hit.point + Vector3.up * 2.8f, 0.25f, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                MakeSign(root, hit.point, -travelDir, style, l1, l2, signMat, postMat);
                placed.Add(at);
                return true;
            }
            return false;
        }

        // Turns: a sign just before each corner, arrow pointing the new way.
        float lastSignDist = float.MaxValue;
        for (int i = 1; i < pts.Count - 1; i++)
        {
            var din = Flat(pts[i] - pts[i - 1]);
            var dout = Flat(pts[i + 1] - pts[i]);
            if (din.magnitude < 6f || dout.magnitude < 6f) continue;
            float angle = Vector3.SignedAngle(din, dout, Vector3.up);
            if (Mathf.Abs(angle) < 35f) continue;
            var at = pts[i] - din.normalized * Mathf.Min(4f, din.magnitude * 0.4f);
            string arrow = angle > 0 ? ">" : "<";
            if (TryPlace(at, din, PSXSign.Style.Road, "THE FACILITY", $"{arrow} {Meters(dist[i])}"))
                lastSignDist = dist[i];
        }

        // Long straights: reassurance signs every ~70m.
        float acc = 0f;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            var seg = pts[i + 1] - pts[i];
            float segLen = seg.magnitude;
            for (float s = 70f - acc; s < segLen; s += 70f)
            {
                var at = pts[i] + seg / segLen * s;
                float remaining = dist[i] - s;
                if (remaining < facility.radius + 50f) break;
                TryPlace(at, seg, PSXSign.Style.Road, "THE FACILITY", $"^ {Meters(remaining)}");
            }
            acc = (acc + segLen) % 70f;
        }

        // Near the facility: warnings, then the name at the gate.
        float warnAt = Mathf.Min(route.length - 5f, facility.radius + 40f);
        PlaceAlong(pts, dist, warnAt, (at, dir) => TryPlace(at, dir, PSXSign.Style.Warning, "RESTRICTED AREA", "KEEP OUT"));
        PlaceAlong(pts, dist, Mathf.Min(route.length - 5f, facility.radius + 15f),
            (at, dir) => TryPlace(at, dir, PSXSign.Style.Warning, "THE FACILITY", "AUTHORIZED ONLY"));

        Debug.Log($"[PSXCity] Placed {placed.Count} signs.");
    }

    /// <summary>Find the point on the route that is <paramref name="remaining"/> meters from the goal.</summary>
    static void PlaceAlong(List<Vector3> pts, float[] dist, float remaining, System.Func<Vector3, Vector3, bool> place)
    {
        for (int i = 0; i < pts.Count - 1; i++)
        {
            if (dist[i] < remaining || dist[i + 1] > remaining) continue;
            float t = Mathf.InverseLerp(dist[i], dist[i + 1], remaining);
            var dir = pts[i + 1] - pts[i];
            // Nudge back along the route until one fits.
            for (float back = 0f; back < 20f; back += 4f)
                if (place(Vector3.Lerp(pts[i], pts[i + 1], t) - dir.normalized * back, dir)) return;
            return;
        }
    }

    static void MakeSign(Transform parent, Vector3 ground, Vector3 faceDir, PSXSign.Style style, string l1, string l2, Material signMat, Material postMat)
    {
        var sign = new GameObject("Sign - " + l1 + " " + l2);
        sign.transform.SetParent(parent);
        sign.transform.position = ground;
        sign.transform.rotation = Quaternion.LookRotation(faceDir);
        sign.isStatic = true;

        var post = new GameObject("Post");
        post.transform.SetParent(sign.transform, false);
        post.AddComponent<MeshFilter>().sharedMesh = postMesh;
        post.AddComponent<MeshRenderer>().sharedMaterial = postMat;
        var col = post.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 1.3f, 0);
        col.size = new Vector3(0.1f, 2.6f, 0.1f);

        var plate = new GameObject("Plate");
        plate.transform.SetParent(sign.transform, false);
        plate.transform.localPosition = new Vector3(0, 2.35f, 0.07f);
        plate.AddComponent<MeshFilter>().sharedMesh = plateMesh;
        var pr = plate.AddComponent<MeshRenderer>();
        pr.sharedMaterial = signMat;
        pr.shadowCastingMode = ShadowCastingMode.Off;
        var s = plate.AddComponent<PSXSign>();
        s.style = style;
        s.line1 = l1;
        s.line2 = l2;
        s.dirt = style == PSXSign.Style.Warning ? 0.7f : 0.5f;
    }

    static Material SignMaterial(string name, Color color)
    {
        string path = $"{CityMatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(Shader.Find("PSX/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("PSX/Lit");
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_AffineAmount", 0.1f);
        mat.SetFloat("_WetAmount", 0.5f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void BuildSignMeshes()
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(CityMeshPath)) AssetDatabase.DeleteAsset(CityMeshPath);
        postMesh = BoxMesh("Sign Post", new Vector3(0.09f, 2.6f, 0.09f), new Vector3(0, 1.3f, 0), false);
        plateMesh = BoxMesh("Sign Plate", new Vector3(2.4f, 0.8f, 0.04f), Vector3.zero, true);
        AssetDatabase.CreateAsset(postMesh, CityMeshPath);
        AssetDatabase.AddObjectToAsset(plateMesh, postMesh);
    }

    /// <summary>Box mesh. With <paramref name="frontUV"/>, the +Z face shows the whole texture (reading
    /// correctly from the front) and every other face samples the texture's edge color.</summary>
    static Mesh BoxMesh(string name, Vector3 size, Vector3 center, bool frontUV)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        Vector3 h = size * 0.5f;

        void Face(Vector3 n, Vector3 u, Vector3 v, float uh, float vh, float d)
        {
            int start = verts.Count;
            Vector3 c = center + n * d;
            bool front = frontUV && n == Vector3.forward;
            for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                float fu = i, fv = j;
                verts.Add(c + u * ((fu - 0.5f) * 2f * uh) + v * ((fv - 0.5f) * 2f * vh));
                norms.Add(n);
                // Seen from the front (+Z), screen-right is local -X, so u runs along -X.
                uvs.Add(front ? new Vector2(1f - fu, fv) : new Vector2(0.01f, 0.01f));
            }
            void Tri(int a, int b, int cc)
            {
                if (Vector3.Dot(Vector3.Cross(verts[b] - verts[a], verts[cc] - verts[a]), n) < 0) (b, cc) = (cc, b);
                tris.Add(a); tris.Add(b); tris.Add(cc);
            }
            Tri(start, start + 2, start + 1);
            Tri(start + 1, start + 2, start + 3);
        }

        Face(Vector3.forward, Vector3.right, Vector3.up, h.x, h.y, h.z);
        Face(Vector3.back, Vector3.right, Vector3.up, h.x, h.y, h.z);
        Face(Vector3.up, Vector3.right, Vector3.forward, h.x, h.z, h.y);
        Face(Vector3.down, Vector3.right, Vector3.forward, h.x, h.z, h.y);
        Face(Vector3.right, Vector3.forward, Vector3.up, h.z, h.y, h.x);
        Face(Vector3.left, Vector3.forward, Vector3.up, h.z, h.y, h.x);

        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Ramer-Douglas-Peucker on the flat route, so tiny navmesh wiggles don't count as turns.</summary>
    static List<Vector3> Simplify(List<Vector3> pts, float eps)
    {
        if (pts.Count < 3) return new List<Vector3>(pts);
        float maxD = 0f; int idx = 0;
        for (int i = 1; i < pts.Count - 1; i++)
        {
            float d = DistToSegment(Flat(pts[i]), Flat(pts[0]), Flat(pts[pts.Count - 1]));
            if (d > maxD) { maxD = d; idx = i; }
        }
        if (maxD <= eps) return new List<Vector3> { pts[0], pts[pts.Count - 1] };
        var a = Simplify(pts.GetRange(0, idx + 1), eps);
        var b = Simplify(pts.GetRange(idx, pts.Count - idx), eps);
        a.RemoveAt(a.Count - 1);
        a.AddRange(b);
        return a;
    }

    static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector3.Distance(p, a + ab * t);
    }

    // ===================================================================================== helpers

    static T[] Find<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    static bool IsInPrefab(Component c) => c && PrefabUtility.IsPartOfPrefabInstance(c);

    static string SourceName(GameObject instanceRoot)
    {
        var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(instanceRoot);
        return (src ? src.name : instanceRoot.name).ToLowerInvariant();
    }

    /// <summary>Far-off backdrop buildings: never reachable, no colliders needed.</summary>
    static bool IsBackground(GameObject go)
    {
        var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
        if (root && SourceName(root).Contains("_bgr")) return true;
        for (var t = go.transform; t; t = t.parent)
            if (t.name.Contains("BGR")) return true;
        return false;
    }

    // ===================================================================================== spawn

    /// <summary>Batch: -executeMethod PSXCityBuilder.SetSpawnBatch -spawn x,y,z [-face x,z]. Moves the player.</summary>
    public static void SetSpawnBatch()
    {
        var args = System.Environment.GetCommandLineArgs();
        Vector3 Arg(string key, Vector3 fallback)
        {
            int i = System.Array.IndexOf(args, key);
            if (i < 0 || i + 1 >= args.Length) return fallback;
            var f = args[i + 1].Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            return f.Length == 3 ? new Vector3(f[0], f[1], f[2]) : new Vector3(f[0], 0f, f[1]);
        }
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = Object.FindFirstObjectByType<PSXPlayer>();
        var pos = Arg("-spawn", player.transform.position);
        var face = Arg("-face", pos + player.transform.forward);
        player.transform.position = pos;
        var dir = Flat(face - pos);
        if (dir.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(dir);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[PSXCity] Player moved to {pos}");
    }

    // ===================================================================================== diagnostics

    public static void DiagnoseBatch()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var big = Find<MeshRenderer>().Where(r => !IsBackground(r.gameObject))
            .OrderByDescending(r => r.bounds.size.x * r.bounds.size.z).Take(8);
        foreach (var r in big)
            Debug.Log($"[Diag] big renderer {r.name} (root {SourceName(PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject) ?? r.gameObject)}) bounds {r.bounds.center} size {r.bounds.size}");

        var lamps = Object.FindObjectsByType<PSXLamp>(FindObjectsSortMode.None);
        var b = new Bounds(lamps[0].transform.position, Vector3.zero);
        foreach (var l in lamps) b.Encapsulate(l.transform.position);
        Debug.Log($"[Diag] lamp bounds center {b.center} size {b.size}");

        foreach (var c in FindFactoryClusters())
            Debug.Log($"[Diag] factory cluster at {c.center} r={c.radius:0} n={c.count}");

        var inst = BuildNavMesh(b);
        var tri = NavMesh.CalculateTriangulation();
        Debug.Log($"[Diag] navmesh verts {tri.vertices.Length} tris {tri.indices.Length / 3}");
        var fac = FindFactoryClusters()[0];
        for (float r = 5; r < 80; r += 10)
            if (NavMesh.SamplePosition(fac.center, out var h, r, NavMesh.AllAreas)) { Debug.Log($"[Diag] facility sample r={r} -> {h.position}"); break; }

        var path = new NavMeshPath();
        int sampled = 0, complete = 0, partial = 0;
        NavMesh.SamplePosition(fac.center, out var goal, 60f, NavMesh.AllAreas);
        foreach (var l in lamps.Where(l => l.name == "Street Lamp").Take(300))
        {
            var p = l.transform.position;
            bool ok = Physics.Raycast(p + Vector3.down * 1.5f, Vector3.down, out var hit, 20f, ~(1 << 2));
            var basePos = ok ? hit.point : p + Vector3.down * 9f;
            if (!NavMesh.SamplePosition(basePos, out var s, 3f, NavMesh.AllAreas)) continue;
            sampled++;
            NavMesh.CalculatePath(s.position, goal.position, NavMesh.AllAreas, path);
            if (path.status == NavMeshPathStatus.PathComplete) complete++; else partial++;
            if (sampled <= 5) Debug.Log($"[Diag] lamp {p} ground {basePos} navmesh {s.position} -> {path.status} len {PathLength(path.corners):0}");
        }
        Debug.Log($"[Diag] lamps sampled {sampled}, complete {complete}, partial {partial}");
        NavMesh.RemoveNavMeshData(inst);
    }

    // ===================================================================================== screenshots

    /// <summary>
    /// Batch: -executeMethod PSXCityBuilder.CaptureBatch -shotDir &lt;folder&gt;
    /// Renders a few views of the city (spawn, first signs, the facility) to PNG for a quick look.
    /// </summary>
    public static void CaptureBatch()
    {
        var args = System.Environment.GetCommandLineArgs();
        int ai = System.Array.IndexOf(args, "-shotDir");
        string dir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "Library/PSXShots";
        Directory.CreateDirectory(dir);

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var manager = Object.FindFirstObjectByType<PSXManager>();
        var cam = Camera.main;
        Shader.SetGlobalVector("_PSXWeather", new Vector4(0.8f, 0f, 0f, 0f));
        Shader.SetGlobalVector("_PSXFlashPos", Vector4.zero);

        var views = new List<(string name, Vector3 pos, Quaternion rot)>
        {
            ("01_spawn", cam.transform.position, cam.transform.rotation),
        };
        var spawnPos = cam.transform.position;
        var signs = Object.FindObjectsByType<PSXSign>(FindObjectsSortMode.None).OrderBy(s => Vector3.Distance(s.transform.position, spawnPos)).ToArray();
        foreach (var s in signs.Take(2).Concat(signs.Where(x => x.style == PSXSign.Style.Warning).Take(1)))
        {
            var plate = s.transform.position;
            var pos = plate + s.transform.forward * 5f + Vector3.down * 0.6f;
            views.Add(("sign_" + views.Count, pos, Quaternion.LookRotation(plate - pos)));
        }
        var zone = Object.FindObjectsByType<PSXMusicZone>(FindObjectsSortMode.None).FirstOrDefault(z => z.name.Contains("The Facility"));
        if (zone)
        {
            var pos = zone.transform.position + new Vector3(0, 25f, -zone.outerRadius);
            views.Add(("facility", pos, Quaternion.LookRotation(zone.transform.position - pos)));
        }

        var rt = new RenderTexture(426, 240, 24) { filterMode = FilterMode.Point };
        var tex = new Texture2D(426, 240, TextureFormat.RGB24, false);
        foreach (var v in views)
        {
            cam.transform.SetPositionAndRotation(v.pos, v.rot);
            manager.Apply();
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 426, 240), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, v.name + ".png"), tex.EncodeToPNG());
            Debug.Log("[PSXCity] Shot " + v.name + " at " + v.pos);
        }
        cam.targetTexture = null;
        RenderTexture.active = null;
    }
}
