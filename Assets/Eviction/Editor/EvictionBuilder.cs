using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Menu: PSX > Build Eviction Scene. Builds Assets/Scenes/Eviction_Intro.unity from scratch:
/// the office (clock out), the office parking lot (her text, his S13), and the apartment (the notice),
/// with rain, PSX materials, the pixel HUD, phone, documents and the sequence that runs it all.
/// The three places sit far apart in one scene; the player is moved between them during fades.
/// Asset Store packs are never modified (materials/textures are copied). Safe to re-run.
/// Batch: Unity.exe -batchmode -projectPath . -executeMethod EvictionBuilder.BuildBatch -quit
/// </summary>
public static class EvictionBuilder
{
    public const string ScenePath = "Assets/Scenes/Eviction_Intro.unity";
    const string Root = "Assets/Eviction";
    const string MatDir = Root + "/Materials";
    const string TexDir = Root + "/Textures";      // copies of pack textures (gitignored, same license as packs)
    const string GenDir = Root + "/Generated";     // textures/meshes made here (ours)
    const string AudioDir = Root + "/Audio";
    const string MeshAsset = GenDir + "/EvictionMeshes.asset";
    const string CityMatDir = "Assets/PSX/City/Materials";
    const string CityTexDir = "Assets/PSX/City/Textures";
    const string PSXAudio = "Assets/PSX/Audio";

    const string Nappin = "Assets/nappin/OfficeEssentialsPack/Prefabs/";
    const string CarsDir = "Assets/Awbmecreations/Mobile Optimize-Free Low Poly Cars/Prefabs/";
    const string HisCar = "Assets/Polyeler/Simple Retro Car/Prefabs/Simple Retro Car.prefab";
    const string Apartment = "Assets/Arabic Neoclassical/Buildings/Prefabs/Building 04.prefab";

    static readonly Vector3 OfficeOrigin = Vector3.zero;
    static readonly Vector3 LotOrigin = new Vector3(300f, 0f, 0f);
    static readonly Vector3 HomeOrigin = new Vector3(700f, 0f, 0f);

    static readonly Color SodiumYellow = new Color(1f, 0.72f, 0.35f);
    static readonly Color Fluorescent = new Color(0.82f, 0.9f, 1f);
    static readonly Color PorchLight = new Color(1f, 0.82f, 0.55f);

    static readonly float[] ThunderTimes = { 5.00f, 40.00f, 52.00f, 98.50f, 141.75f, 172.00f, 196.00f, 209.50f, 221.00f, 240.50f, 261.75f, 315.00f, 326.50f, 343.50f, 359.25f, 377.50f, 390.25f, 403.00f, 450.25f, 462.75f, 504.50f, 518.75f, 534.25f, 572.25f };
    static readonly float[] ThunderDb = { -22, -35, -16, -27, -30, -19, -35, -31, -34, -33, -21, -21, -37, -26, -37, -30, -33, -21, -29, -21, -26, -38, -26, -18 };

    static List<Mesh> meshes;
    static Dictionary<Material, Material> matCache;
    static AudioClip buzzClip;

    [MenuItem("PSX/Build Eviction Scene")]
    public static void BuildMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    public static void BuildBatch() => Build();

    // ===================================================================================== build

    static void Build()
    {
        foreach (var d in new[] { MatDir, TexDir, GenDir }) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();
        ConfigureAudio();
        meshes = new List<Mesh>();
        matCache = new Dictionary<Material, Material>();
        buzzClip = Load<AudioClip>(PSXAudio + "/streetlamp_flicker.ogg");
        if (AssetDatabase.LoadAssetAtPath<Object>(MeshAsset)) AssetDatabase.DeleteAsset(MeshAsset);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.fog = false;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;

        var manager = new GameObject("PSX Settings").AddComponent<PSXManager>();
        manager.fogColor = new Color(0.085f, 0.095f, 0.115f);
        manager.fogDensity = 0.022f;
        manager.fogNoise = 0.15f;
        manager.fogNoiseScale = 0.08f;
        manager.fogWind = new Vector3(1.2f, 0.05f, 0.6f);
        manager.ambient = new Color(0.15f, 0.16f, 0.19f);

        var office = BuildOffice(OfficeOrigin);
        var lot = BuildLot(LotOrigin);
        var home = BuildHome(HomeOrigin);

        var cam = BuildPlayer(office.spawn, manager, out var player);

        // ---------- Weather ----------
        var weather = new GameObject("PSX Weather");
        var rain = weather.AddComponent<PSXRain>();
        rain.follow = cam.transform;
        rain.manager = manager;
        rain.rainMaterial = Load<Material>("Assets/PSX/Materials/PSX_Rain.mat");
        rain.splashMaterial = Load<Material>("Assets/PSX/Materials/PSX_RainSplash.mat");
        rain.thunderTimes = ThunderTimes;
        rain.thunderStrengths = ThunderDb.Select(db => Mathf.Clamp(Mathf.InverseLerp(-40f, -16f, db), 0.25f, 1f)).ToArray();
        var rainSrc = weather.AddComponent<AudioSource>();
        rainSrc.clip = Load<AudioClip>(PSXAudio + "/rain_city_loop.ogg");
        rainSrc.loop = true;
        rainSrc.playOnAwake = true;
        rainSrc.spatialBlend = 0f;
        rainSrc.volume = 0.7f;
        rain.rainSource = rainSrc;

        // ---------- Story systems ----------
        var sys = new GameObject("Eviction");
        var hud = sys.AddComponent<EvictionHud>();
        var sfx = sys.AddComponent<AudioSource>();
        sfx.playOnAwake = false; sfx.spatialBlend = 0f;
        var engineGo = new GameObject("Engine");
        engineGo.transform.SetParent(sys.transform);
        var engine = engineGo.AddComponent<AudioSource>();
        engine.playOnAwake = false; engine.spatialBlend = 0f;

        var phoneGo = new GameObject("Phone");
        phoneGo.transform.SetParent(sys.transform);
        var phone = phoneGo.AddComponent<EvictionPhone>();
        var phoneSrc = phoneGo.AddComponent<AudioSource>();
        phoneSrc.playOnAwake = false; phoneSrc.spatialBlend = 0f;
        phone.source = phoneSrc;
        phone.buzz = Clip("phone_buzz");
        phone.flipOpen = Clip("flip_open");
        phone.flipClose = Clip("flip_close");
        phone.sent = Clip("message_sent");
        phone.keyClicks = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir + "/Keys" })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).Select(Load<AudioClip>).ToArray();

        var doc = sys.AddComponent<EvictionDocument>();
        doc.source = sfx;
        doc.paperSound = Clip("paper");

        var menu = sys.AddComponent<EvictionMenu>();
        var ui = new GameObject("UI Sound").AddComponent<AudioSource>();
        ui.transform.SetParent(sys.transform);
        ui.playOnAwake = false; ui.spatialBlend = 0f;
        menu.uiSource = ui;
        menu.moveSound = phone.keyClicks.Length > 2 ? phone.keyClicks[2] : null;
        menu.selectSound = Clip("flip_close");
        menu.phone = phone;
        menu.document = doc;

        var seq = sys.AddComponent<EvictionSequence>();
        menu.sequence = seq;
        seq.menu = menu;
        seq.titleCamA = home.titleA;
        seq.titleCamB = home.titleB;
        seq.titleLookAt = home.titleLook;
        seq.player = player;
        seq.rain = rain;
        seq.hud = hud;
        seq.phone = phone;
        seq.document = doc;
        seq.sfx = sfx;
        seq.engine = engine;
        seq.officeSpawn = office.spawn;
        seq.lotSpawn = lot.spawn;
        seq.apartmentSpawn = home.spawn;
        seq.punchClock = office.punchClock;
        seq.exitDoor = office.exitDoor;
        seq.car = lot.car;
        seq.notice = home.notice;
        seq.punchClockClip = Clip("punch_clock");
        seq.doorOpen = Clip("door_open");
        seq.doorClose = Clip("door_close");
        seq.carDoorOpen = Clip("car_door_open");
        seq.carDoorShut = Clip("car_door_shut");
        seq.engineStart = Clip("engine_start");
        seq.engineIdle = Clip("engine_idle_loop");
        seq.engineDriveAway = Clip("engine_drive_away");
        seq.engineStop = Clip("engine_stop");

        // ---------- Save ----------
        SaveMeshes();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        AssetDatabase.Refresh();
        Debug.Log("[Eviction] Done: " + ScenePath);
    }

    // ===================================================================================== 1. office

    class Office { public Transform spawn; public Interactable punchClock, exitDoor; }

    static Office BuildOffice(Vector3 o)
    {
        var root = new GameObject("1 - Office").transform;
        root.position = o;
        var result = new Office();

        const float hw = 7f, hd = 5f, ht = 3f, t = 0.2f;
        var carpet = Mat("PSX_Carpet", GenTex("Carpet", 32, 32, Carpet), Color.white, affine: 0f);
        var wall = Mat("PSX_OfficeWall", GenTex("OfficeWall", 32, 32, OfficeWall), Color.white, affine: 0f);
        var ceiling = Mat("PSX_CeilingTile", GenTex("CeilingTile", 32, 32, CeilingTile), Color.white, affine: 0f);
        var trim = Mat("PSX_Trim", null, new Color(0.22f, 0.22f, 0.24f));

        Block(root, "Floor", new Vector3(0, -t / 2, 0), new Vector3(hw * 2, t, hd * 2), carpet, 2f);
        Block(root, "Ceiling", new Vector3(0, ht + t / 2, 0), new Vector3(hw * 2, t, hd * 2), ceiling, 1.2f);
        Block(root, "Roof", new Vector3(0, ht + 1.2f, 0), new Vector3(hw * 2 + 4, 0.4f, hd * 2 + 4), trim, 4f); // keeps rain off
        Block(root, "Wall N", new Vector3(0, ht / 2, hd + t / 2), new Vector3(hw * 2 + t * 2, ht, t), wall, 2f);
        Block(root, "Wall W", new Vector3(-hw - t / 2, ht / 2, 0), new Vector3(t, ht, hd * 2), wall, 2f);
        // East wall with the exit door (z 2.45..3.55).
        const float doorZ = 3f, doorW = 1.1f, doorH = 2.15f;
        float d0 = doorZ - doorW / 2, d1 = doorZ + doorW / 2;
        Block(root, "Wall E 1", new Vector3(hw + t / 2, ht / 2, (-hd + d0) / 2), new Vector3(t, ht, d0 + hd), wall, 2f);
        Block(root, "Wall E 2", new Vector3(hw + t / 2, ht / 2, (d1 + hd) / 2), new Vector3(t, ht, hd - d1), wall, 2f);
        Block(root, "Wall E lintel", new Vector3(hw + t / 2, (doorH + ht) / 2, doorZ), new Vector3(t, ht - doorH, doorW), wall, 2f);
        // Baseboards
        Block(root, "Baseboard N", new Vector3(0, 0.05f, hd - 0.01f), new Vector3(hw * 2, 0.1f, 0.02f), trim, 2f, false);
        Block(root, "Baseboard W", new Vector3(-hw + 0.01f, 0.05f, 0), new Vector3(0.02f, 0.1f, hd * 2), trim, 2f, false);

        // South wall: a row of tall office windows looking out at the rain.
        var window = Prefab(Nappin + "(Prb)Window.prefab");
        float wx = -hw;
        int n = 0;
        while (wx + 2.23f <= hw + 0.01f)
        {
            Place(window, root, new Vector3(wx, 0, -hd - 0.04f), 90f, "Window " + n++);
            wx += 2.23f;
        }
        if (wx < hw) Block(root, "Wall S end", new Vector3((wx + hw) / 2, ht / 2, -hd - t / 2), new Vector3(hw - wx + t, ht, t), wall, 2f);
        Block(root, "Wall S top", new Vector3(0, ht + 0.0f, -hd - t / 2), new Vector3(hw * 2 + t * 2, 0.02f, t), wall, 2f);
        // Outside the windows: a strip of wet pavement and a streetlamp, so there's something out there.
        var pave = Mat("PSX_Pavement", CityTex("sideway_tile"), new Color(0.75f, 0.75f, 0.78f), wet: 1f, affine: 0f);
        Block(root, "Outside pavement", new Vector3(0, -0.15f, -hd - 12f), new Vector3(40f, 0.3f, 22f), pave, 3f);
        StreetLamp(root, new Vector3(3f, 0, -hd - 9f), 0f, flicker: false, range: 12f);

        // Fluorescent ceiling panels, each a PSX lamp. The one by the punch clock flickers and buzzes.
        var panelMat = Mat("PSX_FluorescentPanel", GenTex("FluorescentPanel", 16, 32, FluorescentPanel), Color.white, emission: Fluorescent * 0.9f);
        foreach (var px in new[] { -4.5f, 0f, 4.5f })
        foreach (var pz in new[] { -2.2f, 2.2f })
        {
            var panel = Block(root, "Ceiling Light", new Vector3(px, ht - 0.02f, pz), new Vector3(0.62f, 0.04f, 1.22f), panelMat, 0f, false, frontUV: false, topUV: true);
            var lamp = new GameObject("Fluorescent").AddComponent<PSXLamp>();
            lamp.transform.SetParent(panel.transform, false);
            lamp.transform.localPosition = new Vector3(0, -0.1f, 0);
            lamp.color = Fluorescent;
            lamp.intensity = 1.25f;
            lamp.range = 6.5f;
            lamp.haloStrength = 0.03f;
            lamp.bulbOffset = Vector3.zero;
            lamp.glowRenderer = panel.GetComponent<MeshRenderer>();
            lamp.glowMaterialIndex = 0;
            if (px > 4f && pz > 0f) MakeFlicker(lamp, 0.5f);
        }

        // ---------- Furniture ----------
        var desk = Prefab(Nappin + "(Prb)Desk1.prefab");
        var beige = Mat("PSX_BeigePlastic", null, new Color(0.78f, 0.75f, 0.66f));
        var crtOff = Mat("PSX_CRTOff", GenTex("CRTOff", 16, 14, (x, y, w, h) => CrtFace(x, y, w, h, false)), Color.white);
        var crtOn = Mat("PSX_CRTOn", GenTex("CRTOn", 16, 14, (x, y, w, h) => CrtFace(x, y, w, h, true)), Color.white, emission: new Color(0.35f, 0.45f, 0.8f) * 0.6f);
        // A 2002 desk: beige CRT facing the chair (-X), keyboard in front of it.
        void Computer(Vector3 at, bool on, int i)
        {
            var mon = Block(root, "CRT " + i, at + new Vector3(0.05f, 0.2f, 0), new Vector3(0.42f, 0.38f, 0.42f), beige, 0f, true);
            mon.transform.rotation = Quaternion.Euler(0, -90f, 0);
            var scr = Block(mon.transform, "Screen", new Vector3(0, 0.01f, 0.212f), new Vector3(0.36f, 0.31f, 0.004f), on ? crtOn : crtOff, 0f, false, frontUV: true);
            Block(mon.transform, "Back", new Vector3(0, -0.02f, -0.3f), new Vector3(0.3f, 0.28f, 0.2f), beige, 0f, false);
            Block(root, "Keyboard " + i, at + new Vector3(-0.42f, 0.015f, 0), new Vector3(0.16f, 0.03f, 0.46f), beige, 0f, false);
            if (on)
            {
                var glow = new GameObject("Screen glow").AddComponent<PSXLamp>();
                glow.transform.SetParent(root, false);
                glow.transform.localPosition = at + new Vector3(-0.4f, 0.25f, 0);
                glow.color = new Color(0.45f, 0.55f, 1f);
                glow.intensity = 0.45f; glow.range = 1.8f; glow.haloStrength = 0f; glow.bulbOffset = Vector3.zero;
            }
        }
        var chair = Prefab(Nappin + "(Prb)OfficeChair.prefab");
        var mug = Prefab(Nappin + "(Prb)Mug.prefab");
        var lampDesk = Prefab(Nappin + "(Prb)DeskLight.prefab");
        var holder = Prefab(Nappin + "(Prb)DocumentHolder.prefab");
        var book = Prefab(Nappin + "(Prb)BookOpen.prefab");
        // Seven desks: three pairs plus the supervisor's desk by the windows. Everyone left at once:
        // chairs pushed back, turned, rolled away; a couple of monitors still on.
        var desks = new (float x, float z, bool computer, bool on, string extra, float cx, float cz, float crot)[]
        {
            (-4.2f, -2.3f, true,  true,  "mug",    -0.95f,  0.10f,  75f),  // his desk
            (-4.2f,  0.9f, true,  false, "lamp",   -1.35f,  0.40f, 150f),
            (-0.6f, -2.3f, true,  false, "papers", -1.60f, -0.50f,  40f),
            (-0.6f,  0.9f, true,  true,  "",       -0.70f,  0.00f,  90f),
            ( 3.0f, -2.3f, false, false, "lamp+book", -1.05f, -0.30f, 200f),
            ( 3.0f,  0.9f, true,  false, "mug",    -1.10f,  0.45f,  10f),
            ( 5.6f, -3.3f, true,  false, "papers", -0.95f,  0.00f, 115f),  // supervisor
        };
        for (int di = 0; di < desks.Length; di++)
        {
            var s = desks[di];
            var d = Place(desk, root, new Vector3(s.x, 0, s.z), 0f, "Desk " + di);
            float top = TopOf(d);
            if (s.computer) Computer(new Vector3(s.x + 0.15f, top, s.z), s.on, di);
            Place(chair, root, new Vector3(s.x + s.cx, 0, s.z + s.cz), s.crot, "Chair " + di);
            if (s.extra.Contains("mug")) Place(mug, root, new Vector3(s.x - 0.2f, top, s.z + 0.55f), 40f + di * 30f, "Mug");
            if (s.extra.Contains("lamp")) Place(lampDesk, root, new Vector3(s.x + 0.25f, top, s.z - 0.6f), 200f, "Desk lamp");
            if (s.extra.Contains("papers")) Place(holder, root, new Vector3(s.x - 0.15f, top, s.z - 0.5f), 10f + di * 7f, "Papers");
            if (s.extra.Contains("book")) Place(book, root, new Vector3(s.x - 0.1f, top, s.z + 0.15f), 75f, "Open binder");
        }
        Place(Prefab(Nappin + "(Prb)BigDrawer.prefab"), root, new Vector3(-hw + 0.48f, 0, -1.2f), 0f, "Filing cabinets");
        Place(Prefab(Nappin + "(Prb)Shelves1.prefab"), root, new Vector3(-hw + 0.25f, 0, 2.6f), 0f, "Shelves");
        Place(Prefab(Nappin + "(Prb)Printer.prefab"), root, new Vector3(-2.6f, 0, hd - 0.9f), 90f, "Copier");
        Place(Prefab(Nappin + "(Prb)WaterDispenser.prefab"), root, new Vector3(1.4f, 0, hd - 0.3f), 90f, "Water cooler");
        // Break counter: the coffee table stretched up to counter height (~0.9 m).
        var table = Place(Prefab(Nappin + "(Prb)CoffeTable.prefab"), root, new Vector3(-0.2f, 0, hd - 0.45f), 90f, "Break counter");
        table.transform.localScale = new Vector3(1f, 1.5f, 1f);
        float tt = TopOf(table);
        var pot = Place(Prefab(Nappin + "(Prb)CoffePot.prefab"), root, new Vector3(-0.5f, tt, hd - 0.45f), 90f, "Coffee pot");
        var black = Mat("PSX_BlackPlastic", null, new Color(0.07f, 0.07f, 0.08f));
        foreach (var r in pot.GetComponentsInChildren<Renderer>()) r.sharedMaterials = Enumerable.Repeat(black, r.sharedMaterials.Length).ToArray();
        Place(Prefab(Nappin + "(Prb)Microwave.prefab"), root, new Vector3(0.2f, tt, hd - 0.45f), 90f, "Microwave");
        // Four-drawer filing cabinet: two drawer units stacked (the top unit sinks over the lower one's lid).
        var drawerUnit = Prefab(Nappin + "(Prb)Drawer3.prefab");
        var lower = Place(drawerUnit, root, new Vector3(3.2f, 0, hd - 0.4f), 90f, "Filing cabinet (lower)");
        float unitH = WorldBounds(lower).size.y;
        Place(drawerUnit, root, new Vector3(3.2f, unitH - 0.035f, hd - 0.4f), 90f, "Filing cabinet (upper)");
        Place(Prefab(Nappin + "(Prb)TrashCan.prefab"), root, new Vector3(hw - 0.4f, 0, 1.9f), 0f, "Trash");
        Place(Prefab(Nappin + "(Prb)SmokeDetector.prefab"), root, new Vector3(1.8f, ht, 0f), 0f, "Smoke detector", flipUpsideDown: true);

        // Rain can't fall inside: drops that get into the room vanish.
        var dry = new GameObject("No Rain Zone") { layer = 2 };
        dry.transform.SetParent(root, false);
        dry.transform.localPosition = new Vector3(0, ht / 2 + 0.6f, 0);
        var dryBox = dry.AddComponent<BoxCollider>();
        dryBox.isTrigger = true;
        dryBox.size = new Vector3(hw * 2 + 0.4f, ht + 1.2f, hd * 2 + 0.4f);
        dry.AddComponent<PSXNoRainZone>();

        // ---------- Punch clock, card rack, wall clock (north wall, by the exit) ----------
        var clockMat = Mat("PSX_TimeClock", GenTex("TimeClockFace", 32, 40, TimeClockFace), Color.white);
        var clockBody = Mat("PSX_TimeClockBody", null, new Color(0.62f, 0.6f, 0.54f));
        var clock = Block(root, "Punch Clock", new Vector3(5.3f, 1.25f, hd - 0.12f), new Vector3(0.34f, 0.44f, 0.22f), clockBody, 0f, true);
        // Face on the side looking into the room (-Z).
        var face = Block(clock.transform, "Face", new Vector3(0, 0, -0.112f), new Vector3(0.32f, 0.42f, 0.004f), clockMat, 0f, false, frontUV: true, local: true);
        face.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        var punch = clock.AddComponent<Interactable>();
        punch.prompt = "Clock out";
        result.punchClock = punch;

        var rackMat = Mat("PSX_CardRack", GenTex("CardRack", 16, 40, CardRack), Color.white);
        var rack = Block(root, "Card Rack", new Vector3(4.75f, 1.3f, hd - 0.04f), new Vector3(0.26f, 0.62f, 0.07f), rackMat, 0f, false, frontUV: true);
        rack.transform.rotation = Quaternion.Euler(0, 180f, 0);
        var wallClockMat = Mat("PSX_WallClock", GenTex("WallClock", 32, 32, WallClockFace), Color.white);
        wallClockMat.SetFloat("_AlphaClip", 1f);
        wallClockMat.EnableKeyword("_ALPHATEST_ON");
        wallClockMat.SetFloat("_Cutoff", 0.5f);
        wallClockMat.renderQueue = (int)RenderQueue.AlphaTest;
        var wallClock = Block(root, "Wall Clock (6:30)", new Vector3(5.3f, 2.35f, hd - 0.02f), new Vector3(0.42f, 0.42f, 0.03f), wallClockMat, 0f, false, frontUV: true);
        wallClock.transform.rotation = Quaternion.Euler(0, 180f, 0);
        Block(root, "Notice board", new Vector3(3.8f, 1.62f, hd - 0.02f), new Vector3(1.2f, 0.9f, 0.03f),
              Mat("PSX_Corkboard", GenTex("Corkboard", 128, 96, Corkboard), Color.white), 0f, false, frontUV: true).transform.rotation = Quaternion.Euler(0, 180f, 0);

        // ---------- Exit door ----------
        var doorMat = Mat("PSX_ExitDoor", GenTex("ExitDoor", 24, 48, MetalDoor), Color.white);
        var door = Block(root, "Exit Door", new Vector3(hw + 0.05f, doorH / 2, doorZ), new Vector3(doorW - 0.06f, doorH - 0.04f, 0.06f), doorMat, 0f, true, frontUV: true);
        door.transform.rotation = Quaternion.Euler(0, -90f, 0);
        var exit = door.AddComponent<Interactable>();
        exit.prompt = "Leave";
        exit.lockedText = "Should clock out first.";
        exit.available = false;
        result.exitDoor = exit;
        Block(root, "Door frame top", new Vector3(hw + 0.02f, doorH + 0.04f, doorZ), new Vector3(0.1f, 0.08f, doorW + 0.12f), trim, 0f, false);
        Block(root, "Door frame L", new Vector3(hw + 0.02f, doorH / 2, d0 - 0.03f), new Vector3(0.1f, doorH, 0.06f), trim, 0f, false);
        Block(root, "Door frame R", new Vector3(hw + 0.02f, doorH / 2, d1 + 0.03f), new Vector3(0.1f, doorH, 0.06f), trim, 0f, false);
        var exitSignMat = Mat("PSX_ExitSign", GenTex("ExitSign", 24, 10, ExitSign), Color.white, emission: new Color(1f, 0.25f, 0.2f) * 0.9f);
        var sign = Block(root, "EXIT sign", new Vector3(hw - 0.05f, doorH + 0.32f, doorZ), new Vector3(0.42f, 0.17f, 0.06f), exitSignMat, 0f, false, frontUV: true);
        sign.transform.rotation = Quaternion.Euler(0, -90f, 0);
        var exitGlow = new GameObject("Exit glow").AddComponent<PSXLamp>();
        exitGlow.transform.SetParent(root, false);
        exitGlow.transform.localPosition = new Vector3(hw - 0.3f, doorH + 0.2f, doorZ);
        exitGlow.color = new Color(1f, 0.2f, 0.15f);
        exitGlow.intensity = 0.5f; exitGlow.range = 2.6f; exitGlow.haloStrength = 0.02f; exitGlow.bulbOffset = Vector3.zero;

        // Start at his desk, looking across the room toward the clock and the exit.
        result.spawn = Marker(root, "Office Spawn", new Vector3(-4.2f, 0.05f, -3.9f), new Vector3(1f, 0, 0.75f));
        Bounds(root, new Vector3(0, 1.5f, 0), new Vector3(hw * 2, 3, hd * 2), false);
        return result;
    }

    // ===================================================================================== 2. office parking lot

    class Lot { public Transform spawn; public Interactable car; }

    static Lot BuildLot(Vector3 o)
    {
        var root = new GameObject("2 - Office Parking Lot").transform;
        root.position = o;
        var result = new Lot();

        // The office tower; its back wall (garage doors, staff door) faces the lot at z = -15.5.
        var tower = Place(Prefab(VS("office_building_2")), root, Vector3.zero, 0f, "Office Building");
        AddColliders(tower);
        const float backZ = -15.5f, staffDoorX = 10.2f;

        var asphalt = Mat("PSX_Asphalt", GenTex("Asphalt", 128, 128, (x, y, w, h) => Asphalt(x, y, w, h, false)), Color.white, wet: 1f, affine: 0f);
        var worn = Decal(Mat("PSX_AsphaltWorn", GenTex("AsphaltWorn", 128, 128, (x, y, w, h) => Asphalt(x, y, w, h, true)), Color.white, wet: 1f, affine: 0f));
        Block(root, "Lot", new Vector3(0, -0.2f, -60f), new Vector3(260f, 0.4f, 260f), asphalt, 6f);
        var curbMat = Mat("PSX_Curb", CityTex("sideway_tile"), new Color(0.8f, 0.8f, 0.82f), wet: 1f);
        Block(root, "Sidewalk", new Vector3(0, 0.06f, backZ - 1.1f), new Vector3(36f, 0.12f, 2.2f), curbMat, 2f);

        // Staff door: our own steel door over the painted one, a light above it.
        var doorMat = Mat("PSX_StaffDoor", GenTex("StaffDoor", 24, 48, MetalDoor), new Color(0.7f, 0.72f, 0.75f), wet: 0.3f);
        var door = Block(root, "Staff Door", new Vector3(staffDoorX, 1.1f, backZ - 0.04f), new Vector3(1.1f, 2.2f, 0.06f), doorMat, 0f, true, frontUV: true);
        door.transform.rotation = Quaternion.Euler(0, 180f, 0);
        Block(root, "Staff Door frame", new Vector3(staffDoorX, 1.13f, backZ - 0.02f), new Vector3(1.26f, 2.34f, 0.03f), Mat("PSX_Trim", null, new Color(0.22f, 0.22f, 0.24f)), 0f, false);
        WallLight(root, new Vector3(staffDoorX, 2.75f, backZ - 0.12f), PorchLight, 2f, 9f);

        // Parking stalls: two rows facing each other across a lane.
        // The stalls were repaved at some point: a worn, patched slab under both rows.
        Block(root, "Parking (worn)", new Vector3(0, 0.0175f, -28.5f), new Vector3(32f, 0.035f, 18f), worn, 6f, false);
        var paint = Decal(Mat("PSX_LinePaint", null, new Color(0.78f, 0.78f, 0.72f), wet: 0.8f));
        for (int i = -5; i <= 5; i++)
        {
            float x = i * 2.8f;
            Block(root, "Line A", new Vector3(x, 0.045f, -22.5f), new Vector3(0.12f, 0.01f, 5f), paint, 0f, false);
            Block(root, "Line B", new Vector3(x, 0.045f, -34.5f), new Vector3(0.12f, 0.01f, 5f), paint, 0f, false);
        }
        var cars = new[] { "Hatchback Car_15", "Sport Car_39", "Classic Car_9", "N_Muscle Car_10", "N Van_10", "Pick Up_11" };
        void Parked(string name, int stall, bool rowA, float jitter)
        {
            float x = (stall + 0.5f) * 2.8f;
            var c = Place(Prefab(CarsDir + name + ".prefab"), root, new Vector3(x + jitter * 0.2f, 0, rowA ? -22.4f : -34.6f), (rowA ? 0f : 180f) + jitter * 4f, "Parked " + name);
            AddColliders(c);
        }
        Parked(cars[0], -4, true, 0.5f);
        Parked(cars[4], -2, true, -0.8f);
        Parked(cars[1], 5, true, 0.3f);
        Parked(cars[2], -3, false, -0.4f);
        Parked(cars[5], 4, false, 0.9f);

        // His S13, nose-in on the far row.
        var his = Place(Prefab(HisCar), root, new Vector3(1.4f, 0, -34.6f), 180f, "His Car (S13)");
        AddColliders(his);
        var use = his.AddComponent<Interactable>();
        use.prompt = "Get in";
        use.maxDistance = 2.4f;
        result.car = use;

        // Lights: two poles in the middle, one of them failing.
        StreetLamp(root, new Vector3(-7f, 0, -28.5f), 90f, flicker: false, range: 18f);
        StreetLamp(root, new Vector3(12f, 0, -28.5f), 90f, flicker: true, range: 18f);

        // Edges: hedges and concrete blocks; houses and trees across the road, lost in the rain.
        var hedge = Prefab(VS("hedge_with_base"));
        for (int i = -3; i <= 3; i++) Place(hedge, root, new Vector3(i * 6.2f, 0, -41f), 0f, "Hedge");
        var block = Prefab(VS("concrete_block"));
        for (int i = 0; i < 6; i++)
        {
            Place(block, root, new Vector3(-21f, 0, -19f - i * 3.4f), 90f, "Block");
            Place(block, root, new Vector3(21f, 0, -19f - i * 3.4f), 90f, "Block");
        }
        Place(Prefab(VS("mid_house_1")), root, new Vector3(-14f, 0, -58f), 180f, "House");
        Place(Prefab(VS("small_house_1")), root, new Vector3(6f, 0, -56f), 180f, "House");
        Place(Prefab(VS("mid_house_3")), root, new Vector3(24f, 0, -60f), 180f, "House");
        Place(Prefab(VS("tree_2")), root, new Vector3(-4f, 0, -48f), 30f, "Tree");
        Place(Prefab(VS("tree_1")), root, new Vector3(14f, 0, -47f), 100f, "Tree");
        StreetLamp(root, new Vector3(-3f, 0, -51f), 180f, flicker: false, range: 16f);

        result.spawn = Marker(root, "Lot Spawn", new Vector3(staffDoorX, 0.15f, backZ - 1.6f), new Vector3(-0.3f, 0, -1f));
        Bounds(root, new Vector3(0, 0, -29f), new Vector3(42f, 0, 26f), true);
        return result;
    }

    // ===================================================================================== 3. apartment

    class Home { public Transform spawn, titleA, titleB, titleLook; public Interactable notice; }

    static Home BuildHome(Vector3 o)
    {
        var root = new GameObject("3 - Apartment").transform;
        root.position = o;
        var result = new Home();

        var building = Place(Prefab(Apartment), root, Vector3.zero, -90f, "Apartment Building"); // balcony side toward the lot
        // Its own collider is a tiny broken box on the base: replace with real mesh colliders.
        foreach (var c in building.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        AddColliders(building);
        var b = WorldBounds(building);

        var asphalt = Mat("PSX_Asphalt", GenTex("Asphalt", 128, 128, (x, y, w, h) => Asphalt(x, y, w, h, false)), Color.white, wet: 1f, affine: 0f);
        var worn = Decal(Mat("PSX_AsphaltWorn", GenTex("AsphaltWorn", 128, 128, (x, y, w, h) => Asphalt(x, y, w, h, true)), Color.white, wet: 1f, affine: 0f));
        Block(root, "Lot", new Vector3(0, -0.2f, -40f), new Vector3(260f, 0.4f, 260f), asphalt, 6f);

        // Find a flat stretch of the front wall (facing -Z) near the middle for his door.
        Physics.SyncTransforms();
        Vector3 doorPos = new Vector3(0, 0, b.min.z);
        float bestScore = float.MaxValue;
        float cx = b.center.x - o.x;
        for (float x = cx - 7f; x <= cx + 7f; x += 0.25f)
        {
            float[] ds = new float[9];
            bool ok = true;
            int k = 0;
            for (float dx = -0.6f; dx <= 0.61f && ok; dx += 0.6f)
            foreach (float h in new[] { 0.4f, 1.2f, 2.0f })
            {
                var from = o + new Vector3(x + dx, h, b.min.z - o.z - 6f);
                if (!Physics.Raycast(from, Vector3.forward, out var hit, 30f) || hit.normal.z > -0.9f) { ok = false; break; }
                ds[k++] = hit.point.z;
            }
            if (!ok) continue;
            float spread = ds.Max() - ds.Min();
            float score = spread * 10f + Mathf.Abs(x - cx) * 0.05f;
            if (spread < 0.03f && score < bestScore) { bestScore = score; doorPos = new Vector3(x, 0, ds.Min() - o.z); }
        }
        Debug.Log($"[Eviction] Apartment door at {doorPos} (flatness score {bestScore:0.000}), building bounds {b.center} {b.size}");

        var doorMat = Mat("PSX_AptDoor", GenTex("AptDoor", 24, 48, AptDoor), Color.white, wet: 0.2f);
        var door = Block(root, "Apartment Door 1B", doorPos + new Vector3(0, 1.05f, -0.035f), new Vector3(0.95f, 2.1f, 0.06f), doorMat, 0f, true, frontUV: true);
        door.transform.rotation = Quaternion.Euler(0, 180f, 0);
        var trim = Mat("PSX_DoorTrim", null, new Color(0.3f, 0.28f, 0.26f));
        Block(root, "Door frame", doorPos + new Vector3(0, 1.1f, -0.015f), new Vector3(1.12f, 2.24f, 0.03f), trim, 0f, false);
        Block(root, "Step", doorPos + new Vector3(0, 0.06f, -0.45f), new Vector3(1.6f, 0.12f, 0.9f), Mat("PSX_Curb", CityTex("sideway_tile"), new Color(0.8f, 0.8f, 0.82f), wet: 1f), 1.5f);
        Block(root, "Awning", doorPos + new Vector3(0, 2.55f, -0.45f), new Vector3(1.6f, 0.08f, 0.9f), trim, 0f);
        WallLight(root, doorPos + new Vector3(0.75f, 2.2f, -0.08f), PorchLight, 1.7f, 7f);

        // The notice, taped to the door.
        var noticeMat = Mat("PSX_NoticePaper", GenTex("NoticePaper", 16, 20, NoticePaper), Color.white);
        var paper = Block(root, "Eviction Notice", doorPos + new Vector3(-0.05f, 1.45f, -0.072f), new Vector3(0.22f, 0.28f, 0.004f), noticeMat, 0f, false, frontUV: true);
        paper.transform.rotation = Quaternion.Euler(0, 180f, 2.5f);
        var col = paper.AddComponent<BoxCollider>();
        col.size = new Vector3(0.34f, 0.42f, 0.08f);
        col.isTrigger = true;
        var notice = paper.AddComponent<Interactable>();
        notice.prompt = "Read";
        notice.maxDistance = 2.2f;
        result.notice = notice;

        // Lot: stalls, his car, neighbors' cars, a dumpster, lights.
        var paint = Decal(Mat("PSX_LinePaint", null, new Color(0.78f, 0.78f, 0.72f), wet: 0.8f));
        float rowZ = doorPos.z - 14f;
        Block(root, "Parking (worn)", new Vector3(doorPos.x, 0.0175f, rowZ - 0.5f), new Vector3(32f, 0.035f, 9f), worn, 6f, false);
        for (int i = -5; i <= 5; i++)
            Block(root, "Line", new Vector3(i * 2.8f, 0.045f, rowZ), new Vector3(0.12f, 0.01f, 5f), paint, 0f, false);
        var his = Place(Prefab(HisCar), root, new Vector3(doorPos.x + 4.2f, 0, rowZ + 0.1f), 2f, "His Car (S13)");
        AddColliders(his);
        void Parked(string name, float x, float rot)
        {
            var c = Place(Prefab(CarsDir + name + ".prefab"), root, new Vector3(x, 0, rowZ + 0.1f), rot, "Parked " + name);
            AddColliders(c);
        }
        Parked("Classic Car_9", doorPos.x - 4.2f, -3f);
        Parked("Hatchback Car_15", doorPos.x - 9.8f, 2f);
        Parked("Pick Up_11", doorPos.x + 9.8f, 181f);

        var dumpMat = Mat("PSX_Dumpster", GenTex("Dumpster", 32, 24, Dumpster), Color.white, wet: 0.6f);
        var dump = Block(root, "Dumpster", new Vector3(b.max.x - o.x + 2.5f, 0.65f, doorPos.z - 2.5f), new Vector3(1.9f, 1.3f, 1.2f), dumpMat, 0f, true, frontUV: true);
        dump.transform.rotation = Quaternion.Euler(0, 180f + 8f, 0);
        StreetLamp(root, new Vector3(doorPos.x - 7f, 0, rowZ - 4.5f), 90f, flicker: false, range: 18f);
        StreetLamp(root, new Vector3(doorPos.x + 14f, 0, rowZ - 9f), -90f, flicker: false, range: 18f);

        Place(Prefab(VS("mid_house_2")), root, new Vector3(-6f, 0, rowZ - 30f), 180f, "House");
        Place(Prefab(VS("small_house_2")), root, new Vector3(16f, 0, rowZ - 27f), 180f, "House");
        Place(Prefab(VS("tree_3")), root, new Vector3(b.min.x - o.x - 4f, 0, doorPos.z - 6f), 20f, "Tree");
        Place(Prefab(VS("tree_1")), root, new Vector3(b.max.x - o.x + 6f, 0, rowZ - 6f), 70f, "Tree");

        // Title screen: the camera drifts slowly across the lot, looking up at his lit door.
        result.titleA = Marker(root, "Title Camera A", doorPos + new Vector3(-9f, 2.4f, -19f), Vector3.forward);
        result.titleB = Marker(root, "Title Camera B", doorPos + new Vector3(7f, 3.2f, -17f), Vector3.forward);
        result.titleLook = Marker(root, "Title Look At", doorPos + new Vector3(0f, 4.5f, 0f), Vector3.forward);

        // Spawn beside the driver's door (car faces +Z, so the driver's side is -X), looking at the building.
        result.spawn = Marker(root, "Apartment Spawn", new Vector3(doorPos.x + 4.2f - 1.7f, 0.15f, rowZ - 0.6f), new Vector3(-0.35f, 0, 1f));
        Bounds(root, new Vector3(doorPos.x, 0, (doorPos.z + rowZ) / 2 - 3f), new Vector3(36f, 0, Mathf.Abs(doorPos.z - rowZ) + 16f), true);
        return result;
    }

    // ===================================================================================== player

    static Camera BuildPlayer(Transform spawn, PSXManager manager, out PSXPlayer pc)
    {
        var player = new GameObject("Player");
        player.layer = 2;
        player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.75f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.875f, 0); cc.stepOffset = 0.35f;

        var pivot = new GameObject("Camera Pivot").transform;
        pivot.SetParent(player.transform, false);
        pivot.localPosition = new Vector3(0, 1.6f, 0);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        camGo.transform.SetParent(pivot, false);
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.1f; // better depth precision far away (no flicker between ground and paint)
        cam.farClipPlane = 130f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = manager.fogColor;
        cam.allowMSAA = false;
        cam.allowHDR = false;
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = false;
        camData.antialiasing = AntialiasingMode.None;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<PSXRetroCamera>();
        camGo.AddComponent<PlayerInteractor>();

        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "Fog Dome";
        Object.DestroyImmediate(dome.GetComponent<Collider>());
        dome.transform.SetParent(camGo.transform, false);
        dome.transform.localScale = Vector3.one * 240f;
        var dr = dome.GetComponent<MeshRenderer>();
        dr.sharedMaterial = Load<Material>("Assets/PSX/Materials/PSX_FogSky.mat");
        dr.shadowCastingMode = ShadowCastingMode.Off;
        dr.receiveShadows = false;

        pc = player.AddComponent<PSXPlayer>();
        pc.cameraPivot = pivot;
        var steps = player.AddComponent<AudioSource>();
        steps.playOnAwake = false;
        steps.spatialBlend = 0f;
        pc.footstepSource = steps;
        pc.dryFootsteps = Steps("step_dry_");
        pc.wetFootsteps = Steps("step_wet_");

        manager.cameras = new[] { cam };
        return cam;
    }

    static AudioClip[] Steps(string prefix) =>
        AssetDatabase.FindAssets(prefix + " t:AudioClip", new[] { PSXAudio + "/Footsteps" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileName(p).StartsWith(prefix))
            .OrderBy(p => p).Select(Load<AudioClip>).ToArray();

    // ===================================================================================== pieces

    static void StreetLamp(Transform parent, Vector3 local, float yRot, bool flicker, float range)
    {
        var pole = Place(Prefab(VS("lamp_pole_single_white")), parent, local, yRot, "Lamp Post");
        Vector3 bulb = pole.transform.TransformPoint(new Vector3(0, 9.6f, 3.6f));
        Renderer glow = null; int glowIndex = -1;
        foreach (var r in pole.GetComponentsInChildren<MeshRenderer>())
        {
            var ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++) if (ms[i] && ms[i].name.Contains("_light")) { glow = r; glowIndex = i; }
        }
        var lamp = new GameObject("Street Lamp").AddComponent<PSXLamp>();
        lamp.transform.SetParent(pole.transform, false);
        lamp.transform.position = bulb;
        lamp.color = SodiumYellow;
        lamp.intensity = 3f;
        lamp.range = range;
        lamp.haloStrength = 0.5f;
        lamp.bulbOffset = Vector3.zero;
        lamp.glowRenderer = glow;
        lamp.glowMaterialIndex = glowIndex;
        if (flicker) MakeFlicker(lamp, 0.8f);
        AddColliders(pole);
    }

    static void WallLight(Transform parent, Vector3 local, Color color, float intensity, float range)
    {
        var fixtureMat = Mat("PSX_WallLight", null, Color.white, emission: color * 1.2f);
        var f = Block(parent, "Wall Light", local, new Vector3(0.22f, 0.14f, 0.14f), fixtureMat, 0f, false);
        var lamp = new GameObject("Light").AddComponent<PSXLamp>();
        lamp.transform.SetParent(f.transform, false);
        lamp.transform.localPosition = new Vector3(0, -0.12f, -0.15f);
        lamp.color = color;
        lamp.intensity = intensity;
        lamp.range = range;
        lamp.haloStrength = 0.35f;
        lamp.bulbOffset = Vector3.zero;
        lamp.glowRenderer = f.GetComponent<MeshRenderer>();
        lamp.glowMaterialIndex = 0;
    }

    static void MakeFlicker(PSXLamp lamp, float volume)
    {
        lamp.flicker = true;
        if (!buzzClip) return;
        var src = lamp.gameObject.AddComponent<AudioSource>();
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
        lamp.buzzVolume = volume;
    }

    static void SetClockHands(GameObject clock, int hour, int minute)
    {
        // The hands spin about the clock's local X (its face points along X). 12 o'clock = no rotation.
        foreach (var t in clock.GetComponentsInChildren<Transform>())
        {
            if (t.name.EndsWith("_hour")) t.localRotation = Quaternion.AngleAxis((hour % 12 + minute / 60f) * 30f, Vector3.right);
            else if (t.name.EndsWith("_minute")) t.localRotation = Quaternion.AngleAxis(minute * 6f, Vector3.right);
            else if (t.name.EndsWith("_seconds")) t.localRotation = Quaternion.AngleAxis(17 * 6f, Vector3.right);
        }
    }

    static Transform Marker(Transform parent, string name, Vector3 local, Vector3 forward)
    {
        var m = new GameObject(name).transform;
        m.SetParent(parent, false);
        m.localPosition = local;
        m.rotation = Quaternion.LookRotation(forward.normalized);
        return m;
    }

    /// <summary>Invisible walls around an area (outdoor lots), so the player stays where the story is.</summary>
    static void Bounds(Transform parent, Vector3 centerLocal, Vector3 size, bool walls)
    {
        if (!walls) return;
        var root = new GameObject("Bounds").transform;
        root.SetParent(parent, false);
        float h = 8f, t = 1f;
        void Wall(Vector3 pos, Vector3 s)
        {
            var go = new GameObject("Invisible Wall") { layer = 2 }; // Ignore Raycast: rain passes through, the player still bumps into it
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.AddComponent<BoxCollider>().size = s;
        }
        var c = centerLocal + Vector3.up * h / 2;
        Wall(c + new Vector3(0, 0, -size.z / 2 - t / 2), new Vector3(size.x + 2 * t, h, t));
        Wall(c + new Vector3(0, 0, size.z / 2 + t / 2), new Vector3(size.x + 2 * t, h, t));
        Wall(c + new Vector3(-size.x / 2 - t / 2, 0, 0), new Vector3(t, h, size.z + 2 * t));
        Wall(c + new Vector3(size.x / 2 + t / 2, 0, 0), new Vector3(t, h, size.z + 2 * t));
    }

    // ===================================================================================== placing prefabs

    static GameObject Prefab(string path)
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!p) throw new FileNotFoundException("Missing prefab (import the asset pack): " + path);
        return p;
    }

    static string VS(string name)
    {
        var path = AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets/Versatile Studio Assets" })
            .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name);
        if (path == null) throw new FileNotFoundException("Missing Demo City prefab: " + name);
        return path;
    }

    /// <summary>Instantiate, unpack, strip lights/probes/cameras, convert materials to PSX.</summary>
    static GameObject Place(GameObject prefab, Transform parent, Vector3 local, float yRot, string name, bool flipUpsideDown = false)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(flipUpsideDown ? 180f : 0f, yRot, 0f);
        foreach (var c in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<LightProbeGroup>(true)) Object.DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<ReflectionProbe>(true)) Object.DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(c.gameObject);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
                if (ms[i] && !ms[i].shader.name.StartsWith("PSX/"))
                    ms[i] = Convert(ms[i], r.name);
            r.sharedMaterials = ms;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        go.isStatic = true;
        return go;
    }

    static void AddColliders(GameObject go)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (!mf.sharedMesh || mf.GetComponent<Collider>()) continue;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr && mr.sharedMaterials.All(m => m && m.name.Contains("vegetation_main"))) continue;
            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Height of the top surface under the object's center (for putting things on desks/tables).</summary>
    static float TopOf(GameObject go)
    {
        Physics.SyncTransforms();
        var b = WorldBounds(go);
        var from = new Vector3(b.center.x, b.max.y + 0.5f, b.center.z);
        foreach (var hit in Physics.RaycastAll(from, Vector3.down, 3f).OrderBy(h => h.distance))
            if (hit.collider.transform.IsChildOf(go.transform)) return hit.point.y - go.transform.parent.position.y;
        return b.max.y - go.transform.parent.position.y;
    }

    // ===================================================================================== materials

    static Material Mat(string name, Texture tex, Color color, float wet = 0f, Color? emission = null, float affine = 0.12f)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("PSX/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find("PSX/Lit");
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", color);
        m.SetColor("_EmissionColor", emission ?? Color.black);
        m.SetTexture("_EmissionMap", null);
        m.SetFloat("_AffineAmount", affine);
        m.SetFloat("_SnapAmount", 1f);
        m.SetFloat("_WetAmount", wet);
        m.SetFloat("_AlphaClip", 0f);
        m.DisableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Cull", 2f);
        m.SetFloat("_SrcBlend", (float)BlendMode.One);
        m.SetFloat("_DstBlend", (float)BlendMode.Zero);
        m.SetFloat("_ZWrite", 1f);
        m.renderQueue = -1;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Convert(Material src, string rendererName)
    {
        if (matCache.TryGetValue(src, out var done)) return done;
        string srcPath = AssetDatabase.GetAssetPath(src);
        string clean = string.Concat(src.name.Split(Path.GetInvalidFileNameChars())).Replace("(", "").Replace(")", "").Replace(" ", "");
        // Different packs reuse names like "Color" or "Glass": keep them apart.
        clean += "_" + (Mathf.Abs(srcPath.GetHashCode()) % 10000).ToString("0000");
        string lower = src.name.ToLowerInvariant();

        // Demo City materials: reuse the ones the city conversion already made.
        if (srcPath.StartsWith("Assets/Versatile Studio Assets"))
        {
            var city = AssetDatabase.LoadAssetAtPath<Material>($"{CityMatDir}/PSX_{src.name}.mat");
            if (city) return matCache[src] = city;
        }

        Texture main = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
        if (!main && src.HasProperty("_MainTex")) main = src.GetTexture("_MainTex");
        Color col = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;

        bool pack = srcPath.StartsWith("Assets/nappin") ? true : false;
        Texture tex;
        float wet = 0.35f;
        if (srcPath.StartsWith("Assets/Arabic Neoclassical"))
        {
            // Big baked atlases: use our 1024px 8-bit copies (the originals are 16-bit 4K).
            string n = src.name.Replace("B04_M", "");
            tex = PSXTex(TexDir + "/Building04/B04_M0" + n + ".png", 1024);
            wet = 0.45f;
            col = new Color(0.85f, 0.83f, 0.8f);
        }
        else tex = CopyTex(main, pack ? 64 : lower.Contains("color") ? 128 : 256);

        bool glass = lower.Contains("glass");
        bool emissive = lower.Contains("emissive");
        bool car = srcPath.Contains("Car");
        if (car) wet = 0.8f;
        if (pack) wet = 0f;

        var m = Mat("PSX_" + (pack ? "Office_" : car ? "Car_" : "") + clean, glass ? null : tex,
                    glass ? new Color(0.1f, 0.12f, 0.15f, 0.55f) : col, wet,
                    emissive ? new Color(1f, 0.92f, 0.8f) * 0.8f : (Color?)null);
        if (glass)
        {
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_WetAmount", 0.6f);
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        EditorUtility.SetDirty(m);
        return matCache[src] = m;
    }

    /// <summary>Copy a pack texture into Assets/Eviction/Textures, shrunk and point-filtered. Original untouched.</summary>
    static Texture CopyTex(Texture src, int maxSize)
    {
        if (!src) return null;
        string srcPath = AssetDatabase.GetAssetPath(src);
        if (string.IsNullOrEmpty(srcPath) || !srcPath.StartsWith("Assets/")) return src;
        string dst = $"{TexDir}/{Path.GetFileName(srcPath).Replace("(", "").Replace(")", "")}";
        if (!File.Exists(dst)) AssetDatabase.CopyAsset(srcPath, dst);
        return PSXTex(dst, maxSize);
    }

    static Texture2D PSXTex(string path, int maxSize)
    {
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (!imp) { Debug.LogWarning("[Eviction] Missing texture " + path); return null; }
        imp.textureType = TextureImporterType.Default;
        imp.maxTextureSize = maxSize;
        imp.filterMode = FilterMode.Point;
        imp.mipmapEnabled = false;
        imp.anisoLevel = 0;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>Paint, stains, repaved patches: drawn just in front of the surface under them so they never flicker.</summary>
    static Material Decal(Material m)
    {
        m.SetFloat("_OffsetFactor", -1f);
        m.SetFloat("_OffsetUnits", -2f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Texture2D CityTex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{CityTexDir}/{name}.png");

    // ===================================================================================== generated textures

    static Texture2D GenTex(string name, int w, int h, System.Func<int, int, int, int, Color> fn)
    {
        string path = $"{GenDir}/{name}.png";
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            tex.SetPixel(x, y, fn(x, y, w, h));
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.filterMode = FilterMode.Point;
        imp.mipmapEnabled = false;
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static float Hash(int x, int y, int s)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + s * 982451653;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }

    static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

    static Color Carpet(int x, int y, int w, int h)
    {
        var c = new Color(0.30f, 0.33f, 0.40f);
        float n = Hash(x, y, 1) * 0.25f + Hash(x / 2, y / 2, 2) * 0.15f;
        return Shade(c, 0.8f + n);
    }

    static Color OfficeWall(int x, int y, int w, int h)
    {
        var c = new Color(0.72f, 0.69f, 0.6f);
        return Shade(c, 0.94f + Hash(x, y, 3) * 0.08f - (y < 2 ? 0.1f : 0f));
    }

    static Color CeilingTile(int x, int y, int w, int h)
    {
        if (x % 16 == 0 || y % 16 == 0) return new Color(0.55f, 0.55f, 0.55f);
        float speck = Hash(x, y, 4) > 0.88f ? 0.85f : 1f;
        return Shade(new Color(0.8f, 0.79f, 0.75f), speck);
    }

    static Color FluorescentPanel(int x, int y, int w, int h)
    {
        if (x == 0 || y == 0 || x == w - 1 || y == h - 1) return new Color(0.6f, 0.6f, 0.6f);
        bool grid = x % 3 == 0 || y % 3 == 0;
        return grid ? new Color(0.85f, 0.88f, 0.9f) : Color.white;
    }

    static Color MetalDoor(int x, int y, int w, int h)
    {
        var c = new Color(0.55f, 0.57f, 0.6f);
        float n = 0.92f + Hash(x, y, 5) * 0.1f;
        if (x == 0 || x == w - 1 || y == 0 || y == h - 1) return Shade(c, 0.6f);
        if (y == h / 2 + 1 && x > 3 && x < w - 3) return new Color(0.2f, 0.2f, 0.22f);       // push bar
        if (y == h / 2 && x > 3 && x < w - 3) return new Color(0.75f, 0.75f, 0.75f);
        if (x >= w - 5 && x <= w - 4 && y >= h / 2 - 2 && y <= h / 2 + 2) return new Color(0.15f, 0.15f, 0.15f);
        if (y < 6) return Shade(c, 0.75f * n); // kick plate
        return Shade(c, n);
    }

    static Color AptDoor(int x, int y, int w, int h)
    {
        var c = new Color(0.36f, 0.16f, 0.12f);
        float grain = 0.85f + Hash(x / 3, y, 6) * 0.2f;
        if (x == 0 || x == w - 1 || y == 0 || y == h - 1) return Shade(c, 0.55f);
        bool panel = (x == 3 || x == w - 4) && (y > 4 && y < h - 5) || (y == 5 || y == h / 2 || y == h - 6) && (x > 3 && x < w - 4);
        if (panel) return Shade(c, 0.6f);
        // "1B" plate, peephole, knob
        if (y >= h - 15 && y <= h - 11 && x >= w / 2 - 4 && x <= w / 2 + 3)
        {
            bool one = x == w / 2 - 3 || (x == w / 2 - 4 && y == h - 12);
            int bx = x - (w / 2);
            bool bee = (bx == 0) || (bx >= 0 && bx <= 2 && (y == h - 15 || y == h - 13 || y == h - 11)) || (bx == 3 && y != h - 15 && y != h - 13 && y != h - 11);
            return one || bee ? new Color(0.85f, 0.72f, 0.35f) : new Color(0.2f, 0.18f, 0.16f);
        }
        if (x == w / 2 && y == h - 19) return new Color(0.05f, 0.05f, 0.05f);
        if (x >= w - 6 && x <= w - 5 && y >= h / 2 - 1 && y <= h / 2) return new Color(0.8f, 0.68f, 0.3f);
        return Shade(c, grain);
    }

    static Color NoticePaper(int x, int y, int w, int h)
    {
        var paper = new Color(0.88f, 0.87f, 0.82f);
        if (y >= h - 4 && y <= h - 3 && x >= 2 && x <= w - 3) return new Color(0.15f, 0.15f, 0.15f); // title
        if (y < h - 6 && y > 2 && y % 2 == 0 && x >= 2 && x <= w - 3 && Hash(x, y, 7) > 0.25f) return new Color(0.35f, 0.35f, 0.35f);
        return paper;
    }

    static Color TimeClockFace(int x, int y, int w, int h)
    {
        var body = new Color(0.66f, 0.64f, 0.58f);
        // Dial in the upper part, center (16, 26), radius 10.
        float dx = x - 15.5f, dy = y - 26f, r = Mathf.Sqrt(dx * dx + dy * dy);
        if (r < 10.5f)
        {
            if (r > 9.5f) return new Color(0.25f, 0.25f, 0.25f);
            // Hands at 6:30: hour between 6 and 7, minute straight down.
            if (Mathf.Abs(dx) < 0.6f && dy < 0 && dy > -8.5f) return new Color(0.1f, 0.1f, 0.1f);
            float ha = Mathf.Atan2(dx, -dy), want = Mathf.Deg2Rad * 15f; // slightly past 6, toward 7
            if (r < 6f && Mathf.Abs(Mathf.DeltaAngle(ha * Mathf.Rad2Deg, -want * Mathf.Rad2Deg)) < 9f) return new Color(0.1f, 0.1f, 0.1f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                if (Mathf.Abs(dx - Mathf.Sin(a) * 8f) < 0.7f && Mathf.Abs(dy - Mathf.Cos(a) * 8f) < 0.7f) return new Color(0.2f, 0.2f, 0.2f);
            }
            return new Color(0.92f, 0.9f, 0.82f);
        }
        if (y >= 8 && y <= 10 && x >= 7 && x <= 24) return new Color(0.08f, 0.08f, 0.08f); // card slot
        if (y >= 3 && y <= 5 && x >= 10 && x <= 21) return new Color(0.5f, 0.15f, 0.12f);   // label
        if (x == 0 || x == w - 1 || y == 0 || y == h - 1) return Shade(body, 0.7f);
        return Shade(body, 0.95f + Hash(x, y, 8) * 0.08f);
    }

    /// <summary>Round office wall clock stopped at 6:30 (transparent outside the rim).</summary>
    static Color WallClockFace(int x, int y, int w, int h)
    {
        float cx = (w - 1) / 2f, cy = (h - 1) / 2f;
        float dx = x - cx, dy = y - cy, r = Mathf.Sqrt(dx * dx + dy * dy);
        float R = w / 2f - 0.5f;
        if (r > R) return new Color(0, 0, 0, 0);
        if (r > R - 2f) return new Color(0.1f, 0.1f, 0.1f);
        bool Hand(float clockDeg, float len, float width)
        {
            float a = clockDeg * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a)); // 12 o'clock = up (+y)
            var p = new Vector2(dx, dy);
            float t = Mathf.Clamp(Vector2.Dot(p, dir), 0f, len);
            return (p - dir * t).magnitude < width;
        }
        if (Hand(180f, R - 4f, 0.7f) || Hand(195f, R - 8f, 1.1f)) return new Color(0.08f, 0.08f, 0.08f);
        if (Hand(100f, R - 3.5f, 0.4f)) return new Color(0.7f, 0.1f, 0.1f); // second hand
        for (int i = 0; i < 12; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            if (Mathf.Abs(dx - Mathf.Sin(a) * (R - 3.5f)) < 0.75f && Mathf.Abs(dy - Mathf.Cos(a) * (R - 3.5f)) < 0.75f)
                return new Color(0.15f, 0.15f, 0.15f);
        }
        return new Color(0.93f, 0.92f, 0.88f);
    }

    static Color CrtFace(int x, int y, int w, int h, bool on)
    {
        var bezel = new Color(0.74f, 0.71f, 0.62f);
        if (x < 2 || x >= w - 2 || y < 2 || y >= h - 1) return y == 0 && x > w - 5 && x < w - 2 ? new Color(0.2f, 0.8f, 0.3f) : bezel;
        if (!on) return new Color(0.08f, 0.09f, 0.1f) * (0.9f + Hash(x, y, 14) * 0.2f);
        // Left on: a dim blue desktop with a couple of icons.
        if (x == 3 && (y == h - 4 || y == h - 7)) return new Color(0.9f, 0.9f, 0.6f);
        if (y == 2) return new Color(0.6f, 0.6f, 0.6f);
        return new Color(0.12f, 0.2f, 0.45f);
    }

    static Color CardRack(int x, int y, int w, int h)
    {
        var metal = new Color(0.3f, 0.31f, 0.33f);
        int slot = y % 8;
        if (x > 1 && x < w - 2 && slot >= 5 && slot <= 7 && y < h - 2) return new Color(0.82f, 0.76f, 0.6f) * (0.9f + Hash(x, y, 9) * 0.1f);
        if (slot == 4) return new Color(0.15f, 0.15f, 0.16f);
        return Shade(metal, 0.9f + Hash(x, y, 10) * 0.15f);
    }

    /// <summary>Value noise that wraps every <paramref name="period"/> cells, so textures built from it tile seamlessly.</summary>
    static float PNoise(float x, float y, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int W(int v) => ((v % period) + period) % period;
        float a = Hash(W(x0), W(y0), seed), b = Hash(W(x0 + 1), W(y0), seed);
        float c = Hash(W(x0), W(y0 + 1), seed), d = Hash(W(x0 + 1), W(y0 + 1), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    /// <summary>Old asphalt: gravel speckle, hairline cracks, tar-sealed seams, oil stains. Tiles seamlessly.
    /// The worn variant (parking stalls) has more of everything plus a repaired patch.</summary>
    static Color Asphalt(int x, int y, int w, int h, bool worn)
    {
        float v = 0.25f;
        v += (PNoise(x / 32f, y / 32f, w / 32, 30) - 0.5f) * 0.07f;
        v += (PNoise(x / 8f, y / 8f, w / 8, 31) - 0.5f) * 0.05f;
        float g = Hash(x, y, 32);
        if (g > 0.88f) v += 0.1f * (g - 0.88f) / 0.12f + 0.03f; // light stones
        else if (g < 0.07f) v -= 0.06f;                         // dark pits

        // Hairline cracks: thin contour lines of a noise field, only in some areas.
        float crackField = PNoise(x / 16f, y / 16f, w / 16, 33);
        float crackMask = PNoise(x / 64f, y / 64f, w / 64, 34);
        if (Mathf.Abs(crackField - 0.5f) < (worn ? 0.016f : 0.011f) && crackMask > (worn ? 0.35f : 0.6f)) v *= 0.55f;

        // Tar-sealed seam: a wider, smoother black line.
        float seam = PNoise(x / 32f, y / 32f, w / 32, 35);
        if (Mathf.Abs(seam - 0.5f) < 0.018f && PNoise(x / 64f, y / 64f, w / 64, 36) > (worn ? 0.45f : 0.62f)) v = 0.1f + Hash(x, y, 37) * 0.03f;

        // Oil stains: soft dark blots.
        float oil = PNoise(x / 12f, y / 12f, w / 12, 38) * 0.7f + PNoise(x / 6f, y / 6f, w / 6, 39) * 0.3f;
        float oilAmt = Mathf.InverseLerp(worn ? 0.6f : 0.7f, worn ? 0.78f : 0.85f, oil);
        v *= 1f - oilAmt * 0.45f;

        // Repaired patch (worn only): slightly darker, newer asphalt with a sealed edge.
        if (worn && x >= 66 && x < 108 && y >= 18 && y < 50)
        {
            bool edge = x == 66 || x == 107 || y == 18 || y == 49;
            v = edge ? 0.12f : v * 0.82f;
        }
        v = Mathf.Clamp(v, 0.04f, 0.5f);
        return new Color(v * 0.97f, v, v * 1.05f);
    }

    static Color32[] corkPixels;
    static int corkW;

    /// <summary>The office notice board: calendar, memos, a picnic flyer, and a faded MISSING flyer half under a memo.</summary>
    static Color Corkboard(int x, int y, int w, int h)
    {
        if (corkPixels == null || corkPixels.Length != w * h) BuildCorkboard(w, h);
        return corkPixels[y * w + x];
    }

    static void BuildCorkboard(int w, int h)
    {
        corkW = w;
        var px = new Color32[w * h];
        // Top-down drawing helpers (PSXFont also draws top-down into a bottom-up buffer).
        void R(int x, int y, int rw, int rh, Color c)
        {
            for (int yy = y; yy < y + rh; yy++)
            for (int xx = x; xx < x + rw; xx++)
                if (xx >= 0 && yy >= 0 && xx < w && yy < h) px[(h - 1 - yy) * w + xx] = c;
        }
        void T(int x, int y, string s, Color c) => PSXFont.Draw(px, w, h, x, y, s, c);
        void Lines(int x, int y, int lw, int count, int gap, Color c, int seed)
        {
            for (int i = 0; i < count; i++) R(x, y + i * gap, Mathf.RoundToInt(lw * (0.55f + Hash(i, seed, 20) * 0.45f)), 1, c);
        }
        void Pin(int x, int y, Color c) { R(x, y, 2, 2, c); R(x, y + 2, 1, 1, new Color(0.15f, 0.12f, 0.1f)); }

        // Cork and frame.
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            px[y * w + x] = new Color(0.6f, 0.43f, 0.27f) * (0.82f + Hash(x, y, 11) * 0.3f);
        R(0, 0, w, 3, new Color(0.33f, 0.24f, 0.16f)); R(0, h - 3, w, 3, new Color(0.33f, 0.24f, 0.16f));
        R(0, 0, 3, h, new Color(0.33f, 0.24f, 0.16f)); R(w - 3, 0, 3, h, new Color(0.33f, 0.24f, 0.16f));

        var paper = new Color(0.9f, 0.89f, 0.84f);
        var ink = new Color(0.25f, 0.25f, 0.28f);

        // Calendar, October 2002 (the 18th, today, circled).
        R(7, 7, 44, 50, paper);
        R(7, 7, 44, 11, new Color(0.55f, 0.15f, 0.15f));
        T(9, 9, "OCT 2002", new Color(0.95f, 0.92f, 0.85f));
        for (int r = 0; r < 5; r++)
        for (int c = 0; c < 7; c++)
        {
            int cx = 9 + c * 6, cy = 21 + r * 7;
            int day = r * 7 + c - 1; // Oct 1 2002 was a Tuesday
            if (day < 0 || day >= 31) continue;
            R(cx, cy, 4, 4, day == 17 ? new Color(0.75f, 0.2f, 0.2f) : new Color(0.7f, 0.7f, 0.7f));
        }
        Pin(28, 8, new Color(0.8f, 0.15f, 0.15f));

        // Yellow sticky memo.
        R(56, 9, 26, 24, new Color(0.95f, 0.88f, 0.45f));
        Lines(59, 13, 20, 5, 4, new Color(0.35f, 0.3f, 0.2f), 1);

        // Company picnic flyer.
        R(88, 7, 33, 46, new Color(0.78f, 0.86f, 0.93f));
        T(90, 10, "PICNIC", new Color(0.15f, 0.3f, 0.55f));
        R(92, 22, 25, 12, new Color(0.45f, 0.65f, 0.4f)); // a little park drawing
        R(103, 18, 3, 6, new Color(0.4f, 0.28f, 0.18f));
        Lines(91, 38, 27, 3, 4, ink, 2);
        Pin(103, 8, new Color(0.2f, 0.35f, 0.8f));

        // Typed memo bottom left.
        R(8, 63, 36, 27, paper);
        T(10, 65, "MEMO", ink);
        Lines(10, 76, 30, 3, 4, ink, 3);
        Pin(25, 64, new Color(0.2f, 0.6f, 0.25f));

        // The MISSING flyer: old, sun-bleached, pinned under a newer memo.
        var faded = new Color(0.82f, 0.8f, 0.7f);
        R(52, 40, 34, 50, faded);
        T(54, 42, "MISSING", new Color(0.62f, 0.38f, 0.35f));
        R(58, 53, 18, 18, new Color(0.55f, 0.55f, 0.52f));               // photo
        R(63, 56, 8, 8, new Color(0.42f, 0.42f, 0.4f));                  // a face, barely
        R(61, 64, 12, 7, new Color(0.45f, 0.45f, 0.43f));                // shoulders
        Lines(55, 75, 28, 3, 4, new Color(0.6f, 0.58f, 0.52f), 4);
        Pin(68, 41, new Color(0.5f, 0.45f, 0.4f));
        // Newer memo pinned over its right side.
        R(76, 58, 28, 30, paper);
        Lines(79, 62, 22, 5, 4, ink, 5);
        Pin(89, 59, new Color(0.8f, 0.15f, 0.15f));

        // Business card and a torn corner of something.
        R(108, 62, 14, 9, new Color(0.95f, 0.95f, 0.93f));
        R(110, 64, 8, 1, ink);
        R(110, 67, 6, 1, ink);
        R(106, 76, 16, 12, new Color(0.85f, 0.82f, 0.78f));
        corkPixels = px;
    }

    static Color ExitSign(int x, int y, int w, int h)
    {
        if (x == 0 || y == 0 || x == w - 1 || y == h - 1) return new Color(0.75f, 0.75f, 0.72f);
        var buf = new Color32[w * h];
        PSXFont.Draw(buf, w, h, 1, 1, "EXIT", new Color32(255, 255, 255, 255));
        return buf[y * w + x].a > 0 ? new Color(1f, 0.15f, 0.1f) : new Color(0.08f, 0.02f, 0.02f);
    }

    static Color Dumpster(int x, int y, int w, int h)
    {
        var g = new Color(0.16f, 0.3f, 0.2f);
        float rust = Hash(x / 2, y / 2, 12) > 0.8f ? 0.6f : 1f;
        if (y > h - 4) return Shade(g, 0.7f);
        if (x % 8 == 0) return Shade(g, 0.75f);
        return Shade(g, (0.85f + Hash(x, y, 13) * 0.2f) * rust);
    }

    // ===================================================================================== meshes

    /// <summary>A box with world-scaled tiling UVs (tile = meters per texture repeat, 0 = stretch once per face).
    /// frontUV: the whole texture on the +Z face only (signs, doors, faces). topUV: whole texture on the -Y face (ceiling panels).</summary>
    static GameObject Block(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, float tile,
                            bool collider = true, bool frontUV = false, bool topUV = false, bool local = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var mesh = BoxMesh(name, size, tile, frontUV, topUV);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        if (collider) go.AddComponent<BoxCollider>().size = size;
        go.isStatic = true;
        return go;
    }

    static Mesh BoxMesh(string name, Vector3 size, float tile, bool frontUV, bool topUV)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        Vector3 h = size * 0.5f;

        void Face(Vector3 n, Vector3 u, Vector3 v, float uh, float vh, float d)
        {
            int start = verts.Count;
            bool whole = (frontUV && n == Vector3.forward) || (topUV && n == Vector3.down);
            bool edge = (frontUV && !whole) || (topUV && !whole);
            for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                verts.Add(n * d + u * ((i - 0.5f) * 2f * uh) + v * ((j - 0.5f) * 2f * vh));
                norms.Add(n);
                Vector2 uv;
                if (whole) uv = new Vector2(n == Vector3.forward ? 1f - i : i, j);
                else if (edge) uv = new Vector2(0.02f, 0.02f);
                else if (tile > 0f) uv = new Vector2(i * uh * 2f / tile, j * vh * 2f / tile);
                else uv = new Vector2(i, j);
                uvs.Add(uv);
            }
            void Tri(int a, int b, int c)
            {
                if (Vector3.Dot(Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]), n) < 0) (b, c) = (c, b);
                tris.Add(a); tris.Add(b); tris.Add(c);
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
        meshes.Add(mesh);
        return mesh;
    }

    static void SaveMeshes()
    {
        if (meshes.Count == 0) return;
        AssetDatabase.CreateAsset(meshes[0], MeshAsset);
        for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshes[0]);
    }

    // ===================================================================================== audio

    static AudioClip Clip(string name) => Load<AudioClip>($"{AudioDir}/{name}.ogg");

    static void ConfigureAudio()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var imp = AssetImporter.GetAtPath(p) as AudioImporter;
            if (!imp) continue;
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            s.preloadAudioData = true;
            imp.defaultSampleSettings = s;
            imp.forceToMono = false;
            imp.SaveAndReimport();
        }
    }

    static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
}
