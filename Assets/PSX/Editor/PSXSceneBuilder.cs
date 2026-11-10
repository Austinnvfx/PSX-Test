using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Menu: PSX > Build Test Scene. Generates low-res textures, PSX materials, subdivided meshes and a
/// foggy little street at Assets/Scenes/PSXTest.unity with a first-person player and flashlight.
/// </summary>
public static class PSXSceneBuilder
{
    const string Root = "Assets/PSX";
    const string TexDir = Root + "/Textures";
    const string MatDir = Root + "/Materials";
    const string MeshPath = Root + "/Meshes/PSXTestMeshes.asset";
    const string ScenePath = "Assets/Scenes/PSXTest.unity";

    static Mesh meshContainer;
    static int meshCount;

    [MenuItem("PSX/Build Test Scene")]
    public static void BuildMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    // For: Unity.exe -batchmode -projectPath . -executeMethod PSXSceneBuilder.BuildBatch -quit
    public static void BuildBatch() => Build();

    static void Build()
    {
        Directory.CreateDirectory(TexDir);
        Directory.CreateDirectory(MatDir);
        Directory.CreateDirectory(Path.GetDirectoryName(MeshPath));

        // ---------- Textures & materials ----------
        var asphalt  = Mat("Asphalt",  Tex("Asphalt", 64, AsphaltPixel), 1);
        var concrete = Mat("Concrete", Tex("Concrete", 64, ConcretePixel), 1);
        var brick    = Mat("Brick",    Tex("Brick", 64, BrickPixel), 1);
        var plaster  = Mat("Plaster",  Tex("Plaster", 64, PlasterPixel), 1);
        var dirt     = Mat("Dirt",     Tex("Dirt", 64, DirtPixel), 1);
        var wood     = Mat("Wood",     Tex("Wood", 32, WoodPixel), 1);
        var metal    = Mat("Metal",    Tex("Metal", 32, MetalPixel), 1);
        var fence    = Mat("Fence",    Tex("Fence", 32, FencePixel, alpha: true), 1, cutout: true, twoSided: true);
        var bark     = Mat("Bark",     Tex("Bark", 32, BarkPixel), 1);
        var lampGlow = Mat("LampGlow", Tex("White", 4, (x, y, s) => Color.white), 1);
        lampGlow.SetColor("_EmissionColor", new Color(1f, 0.82f, 0.5f));
        lampGlow.SetColor("_BaseColor", new Color(0.2f, 0.17f, 0.1f));

        // ---------- Scene ----------
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.fog = false;                  // PSX/Lit does its own fog
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.skybox = null;

        meshContainer = null;
        meshCount = 0;
        if (AssetDatabase.LoadAssetAtPath<Object>(MeshPath)) AssetDatabase.DeleteAsset(MeshPath);

        var env = new GameObject("Environment").transform;

        // Ground (dirt), road (asphalt), sidewalks (concrete)
        Box("Ground", env, new Vector3(0, -0.5f, 0), new Vector3(160, 0.5f, 160), dirt, 4f, 4f);
        Box("Road", env, new Vector3(0, -0.02f, 0), new Vector3(9, 0.04f, 130), asphalt, 3f, 1.5f);
        Box("Sidewalk L", env, new Vector3(-6.5f, 0, 0), new Vector3(4, 0.15f, 130), concrete, 2f, 1.5f);
        Box("Sidewalk R", env, new Vector3(6.5f, 0, 0), new Vector3(4, 0.15f, 130), concrete, 2f, 1.5f);
        // Cross street
        Box("Cross Road", env, new Vector3(0, -0.01f, 10), new Vector3(130, 0.04f, 9), asphalt, 3f, 1.5f);

        // Buildings lining the street, with alley gaps.
        var rng = new System.Random(1999);
        Material[] wallMats = { brick, plaster, concrete, brick, plaster };
        foreach (int side in new[] { -1, 1 })
        {
            float z = -62f;
            while (z < 62f)
            {
                float depth = 8f + (float)rng.NextDouble() * 6f;
                float width = 7f + (float)rng.NextDouble() * 7f;
                float height = 5f + (float)rng.NextDouble() * 9f;
                // keep the cross street clear
                if (z + width > 4.5f && z < 15.5f) { z = 15.5f; continue; }
                var mat = wallMats[rng.Next(wallMats.Length)];
                float x = side * (8.5f + depth * 0.5f);
                Box("Building", env, new Vector3(x, 0, z + width * 0.5f), new Vector3(depth, height, width), mat, 3f, 2f);
                // dark doorway / window strips for silhouette detail
                Box("Door", env, new Vector3(side * 8.45f, 0, z + width * 0.5f), new Vector3(0.12f, 2.2f, 1.3f), wood, 1.2f, 2f);
                z += width + (rng.NextDouble() < 0.35 ? 2.5f : 0f);
            }
        }

        // Lamp posts: real yellow light sources that glow through the fog. One of them is failing.
        int lampIndex = 0;
        for (float z = -55f; z <= 55f; z += 14f)
        {
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * 4.9f;
                Box("Lamp Pole", env, new Vector3(x, 0.15f, z), new Vector3(0.18f, 4.6f, 0.18f), metal, 1f, 1f);
                Box("Lamp Arm", env, new Vector3(x - side * 0.5f, 4.6f, z), new Vector3(1.1f, 0.12f, 0.14f), metal, 1f, 1f);
                var head = Box("Lamp Head", env, new Vector3(x - side * 0.95f, 4.45f, z), new Vector3(0.45f, 0.18f, 0.3f), lampGlow, 1f, 1f);
                var lamp = head.AddComponent<PSXLamp>();
                lamp.glowRenderer = head.GetComponent<MeshRenderer>();
                lamp.flicker = lampIndex == 9;
                lampIndex++;
            }
        }

        // Props
        Crate(env, new Vector3(-6.2f, 0.15f, -38f), 0f, wood);
        Crate(env, new Vector3(-6.6f, 0.15f, -36.9f), 25f, wood);
        Crate(env, new Vector3(-6.3f, 1.15f, -37.5f), 10f, wood, 0.8f);
        Box("Dumpster", env, new Vector3(6.8f, 0.15f, -20f), new Vector3(1.6f, 1.3f, 2.6f), metal, 1f, 1f);
        Box("Car Body", env, new Vector3(-2.6f, 0f, -12f), new Vector3(1.8f, 0.8f, 4.3f), metal, 1.5f, 1f, 8f);
        Box("Car Cabin", env, new Vector3(-2.6f, 0.8f, -12.3f), new Vector3(1.6f, 0.6f, 2.2f), metal, 1.5f, 1f, 8f);
        Box("Bench", env, new Vector3(6.9f, 0.15f, 2f), new Vector3(0.6f, 0.45f, 2f), wood, 1f, 1f);

        // A dead tree
        var tree = new GameObject("Dead Tree").transform;
        tree.SetParent(env);
        tree.position = new Vector3(-6.8f, 0.15f, 20f);
        Box("Trunk", tree, tree.position, new Vector3(0.45f, 4.2f, 0.45f), bark, 1f, 1f);
        Box("Branch", tree, tree.position + new Vector3(0.6f, 3f, 0), new Vector3(1.6f, 0.14f, 0.14f), bark, 1f, 1f, 30f, Vector3.forward);
        Box("Branch", tree, tree.position + new Vector3(-0.4f, 3.6f, 0.2f), new Vector3(1.2f, 0.12f, 0.12f), bark, 1f, 1f, -40f, Vector3.forward);
        Box("Branch", tree, tree.position + new Vector3(0, 2.4f, -0.5f), new Vector3(0.12f, 0.12f, 1.3f), bark, 1f, 1f, 35f, Vector3.right);

        // The road just... ends. Chain-link fence and barriers across the far end.
        Box("Fence", env, new Vector3(0, 0, 58f), new Vector3(17f, 3f, 0.05f), fence, 1.5f, 1.5f);
        Box("Fence Post", env, new Vector3(-8.5f, 0, 58f), new Vector3(0.1f, 3f, 0.1f), metal, 1f, 1f);
        Box("Fence Post", env, new Vector3(0, 0, 58f), new Vector3(0.1f, 3f, 0.1f), metal, 1f, 1f);
        Box("Fence Post", env, new Vector3(8.5f, 0, 58f), new Vector3(0.1f, 3f, 0.1f), metal, 1f, 1f);
        Box("Barrier", env, new Vector3(-2f, 0, 55f), new Vector3(3f, 0.9f, 0.3f), concrete, 1f, 1f);
        Box("Barrier", env, new Vector3(2.5f, 0, 55.5f), new Vector3(3f, 0.9f, 0.3f), concrete, 1f, 1f, 8f);
        // Fences closing off the cross street.
        Box("Fence", env, new Vector3(-30f, 0, 10f), new Vector3(0.05f, 3f, 9f), fence, 1.5f, 1.5f);
        Box("Fence", env, new Vector3(30f, 0, 10f), new Vector3(0.05f, 3f, 9f), fence, 1.5f, 1.5f);
        Box("Fence", env, new Vector3(0, 0, -62f), new Vector3(17f, 3f, 0.05f), fence, 1.5f, 1.5f);

        // Invisible walls so you can't wander off.
        Wall(env, new Vector3(0, 0, 59f), new Vector3(20, 6, 1));
        Wall(env, new Vector3(0, 0, -63f), new Vector3(20, 6, 1));
        Wall(env, new Vector3(-30.5f, 0, 10f), new Vector3(1, 6, 12));
        Wall(env, new Vector3(30.5f, 0, 10f), new Vector3(1, 6, 12));

        // ---------- Settings ----------
        var settingsGo = new GameObject("PSX Settings");
        var manager = settingsGo.AddComponent<PSXManager>();

        // ---------- Player ----------
        var player = new GameObject("Player");
        player.transform.position = new Vector3(0, 0.1f, -52f);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.75f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0, 0.875f, 0);
        cc.stepOffset = 0.3f;

        var pivot = new GameObject("Camera Pivot").transform;
        pivot.SetParent(player.transform, false);
        pivot.localPosition = new Vector3(0, 1.6f, 0);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(pivot, false);
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 60f;  // fog hides everything past ~30m anyway
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = manager.fogColor;
        cam.allowMSAA = false;
        cam.allowHDR = false;
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = false;
        camData.antialiasing = AntialiasingMode.None;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<PSXRetroCamera>();

        // Fog "sky" dome riding with the camera, so lamp halos show against open fog too.
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "Fog Dome";
        Object.DestroyImmediate(dome.GetComponent<Collider>());
        dome.transform.SetParent(camGo.transform, false);
        dome.transform.localScale = Vector3.one * 110f; // radius 55, inside the 60m far plane
        var domeRenderer = dome.GetComponent<MeshRenderer>();
        domeRenderer.sharedMaterial = FogSkyMaterial();
        domeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        domeRenderer.receiveShadows = false;

        var flashGo = new GameObject("Flashlight");
        flashGo.transform.SetParent(pivot, false);
        flashGo.transform.localPosition = new Vector3(0.22f, -0.25f, 0.15f);
        var flash = flashGo.AddComponent<PSXFlashlight>();
        flash.follow = camGo.transform;

        var pc = player.AddComponent<PSXPlayer>();
        pc.cameraPivot = pivot;

        manager.cameras = new[] { cam };

        // ---------- Save ----------
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        AssetDatabase.Refresh();
        Debug.Log("[PSX] Test scene built at " + ScenePath + ". Press Play: WASD + mouse, Shift to run, F for flashlight.");
    }

    // ===================================================================== helpers

    static void Crate(Transform parent, Vector3 pos, float yaw, Material mat, float size = 1f)
    {
        Box("Crate", parent, pos, Vector3.one * size, mat, size, size, yaw);
    }

    static void Wall(Transform parent, Vector3 pos, Vector3 size)
    {
        var go = new GameObject("Invisible Wall");
        go.transform.SetParent(parent);
        go.transform.position = pos + Vector3.up * size.y * 0.5f;
        go.AddComponent<BoxCollider>().size = size;
    }

    /// <summary>Box with its pivot at the bottom center, world-scaled UVs and subdivided faces.</summary>
    static GameObject Box(string name, Transform parent, Vector3 bottomCenter, Vector3 size, Material mat,
        float tileU, float tileV, float rotDeg = 0f, Vector3? rotAxis = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = bottomCenter;
        go.transform.rotation = Quaternion.AngleAxis(rotDeg, rotAxis ?? Vector3.up);
        go.isStatic = true;

        var mesh = BuildBoxMesh(size, tileU, tileV, cell: 2f);
        SaveMesh(mesh, name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var col = go.AddComponent<BoxCollider>();
        col.center = new Vector3(0, size.y * 0.5f, 0);
        col.size = size;
        return go;
    }

    static void SaveMesh(Mesh mesh, string name)
    {
        mesh.name = name + "_" + (meshCount++);
        if (meshContainer == null)
        {
            meshContainer = mesh;
            AssetDatabase.CreateAsset(mesh, MeshPath);
        }
        else
        {
            AssetDatabase.AddObjectToAsset(mesh, meshContainer);
        }
    }

    static Mesh BuildBoxMesh(Vector3 size, float tileU, float tileV, float cell)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        Vector3 h = size * 0.5f;
        Vector3 offset = new Vector3(0, h.y, 0);

        void Face(Vector3 n, Vector3 uAxis, float uLen, Vector3 vAxis, float vLen, float dist)
        {
            int nu = Mathf.Max(1, Mathf.CeilToInt(uLen / cell));
            int nv = Mathf.Max(1, Mathf.CeilToInt(vLen / cell));
            int start = verts.Count;
            Vector3 center = n * dist + offset;
            for (int j = 0; j <= nv; j++)
            for (int i = 0; i <= nu; i++)
            {
                float fu = (float)i / nu, fv = (float)j / nv;
                verts.Add(center + uAxis * ((fu - 0.5f) * uLen) + vAxis * ((fv - 0.5f) * vLen));
                norms.Add(n);
                uvs.Add(new Vector2(fu * uLen / tileU, fv * vLen / tileV));
            }
            for (int j = 0; j < nv; j++)
            for (int i = 0; i < nu; i++)
            {
                int a = start + j * (nu + 1) + i, b = a + 1, c = a + nu + 1, d = c + 1;
                AddTri(a, c, b, n);
                AddTri(b, c, d, n);
            }
        }

        void AddTri(int a, int b, int c, Vector3 n)
        {
            // Unity front faces: Cross(b - a, c - a) points along the normal.
            if (Vector3.Dot(Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]), n) < 0) (b, c) = (c, b);
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        Face(Vector3.up,      Vector3.right,   size.x, Vector3.forward, size.z, h.y);
        Face(Vector3.down,    Vector3.right,   size.x, Vector3.forward, size.z, h.y);
        Face(Vector3.forward, Vector3.right,   size.x, Vector3.up,      size.y, h.z);
        Face(Vector3.back,    Vector3.right,   size.x, Vector3.up,      size.y, h.z);
        Face(Vector3.right,   Vector3.forward, size.z, Vector3.up,      size.y, h.x);
        Face(Vector3.left,    Vector3.forward, size.z, Vector3.up,      size.y, h.x);

        var mesh = new Mesh { indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Material Mat(string name, Texture2D tex, float tiling, bool cutout = false, bool twoSided = false)
    {
        string path = $"{MatDir}/PSX_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("PSX/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("PSX/Lit");
        mat.SetTexture("_BaseMap", tex);
        mat.SetTextureScale("_BaseMap", Vector2.one * tiling);
        mat.SetFloat("_AffineAmount", 0.35f); // a hint of PS1 texture swim without getting seasick
        if (cutout)
        {
            mat.SetFloat("_AlphaClip", 1);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        if (twoSided) mat.SetFloat("_Cull", 0);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material FogSkyMaterial()
    {
        string path = $"{MatDir}/PSX_FogSky.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("PSX/FogSky"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("PSX/FogSky");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    delegate Color PixelFn(int x, int y, int size);

    static Texture2D Tex(string name, int size, PixelFn fn, bool alpha = false)
    {
        string path = $"{TexDir}/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            tex.SetPixel(x, y, fn(x, y, size));
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.filterMode = FilterMode.Point;   // no bilinear on the PS1
        imp.mipmapEnabled = false;           // ...and no mipmaps
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.alphaIsTransparency = alpha;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void AddToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == path)) return;
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ===================================================================== procedural pixels

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }

    // Tileable value noise
    static float Noise(float x, float y, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int Wrap(int v) => ((v % period) + period) % period;
        float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
        float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float Fbm(int x, int y, int size, int seed)
    {
        float v = 0, amp = 0.5f;
        for (int o = 0, p = 4; o < 4; o++, p *= 2, amp *= 0.5f)
            v += Noise(x * p / (float)size, y * p / (float)size, p, seed + o) * amp;
        return v / 0.9375f;
    }

    static Color Grey(float v) => new Color(v, v, v, 1);
    static Color Tint(Color c, float v) => new Color(c.r * v, c.g * v, c.b * v, 1);

    static Color AsphaltPixel(int x, int y, int s)
    {
        float v = 0.22f + Fbm(x, y, s, 1) * 0.12f + (Hash(x, y, 2) - 0.5f) * 0.08f;
        if (Hash(x, y, 3) > 0.97f) v += 0.12f; // gravel specks
        return Tint(new Color(1f, 0.98f, 0.95f), v);
    }

    static Color ConcretePixel(int x, int y, int s)
    {
        float v = 0.45f + Fbm(x, y, s, 10) * 0.2f + (Hash(x, y, 11) - 0.5f) * 0.05f;
        if (x % 32 == 0 || y % 32 == 0) v *= 0.6f; // slab seams
        float stain = Mathf.Clamp01(Fbm(x, y, s, 12) - 0.55f) * 1.5f;
        return Tint(new Color(0.95f, 0.93f, 0.88f), v * (1 - stain * 0.5f));
    }

    static Color BrickPixel(int x, int y, int s)
    {
        int bh = 8, bw = 16;
        int row = y / bh;
        int bx = (x + (row % 2) * (bw / 2)) % s;
        bool mortar = y % bh == 0 || bx % bw == 0;
        if (mortar) return Grey(0.32f + Hash(x, y, 20) * 0.05f);
        int id = row * 8 + bx / bw;
        float v = 0.75f + Hash(id, 0, 21) * 0.25f + (Fbm(x, y, s, 22) - 0.5f) * 0.25f;
        return Tint(new Color(0.42f, 0.2f, 0.15f), v);
    }

    static Color PlasterPixel(int x, int y, int s)
    {
        float v = 0.6f + Fbm(x, y, s, 30) * 0.18f;
        float drip = Mathf.Clamp01(Noise(x * 8f / s, y * 0.5f / s, 8, 31) - 0.5f) * 1.2f; // vertical grime streaks
        return Tint(new Color(0.82f, 0.78f, 0.68f), v * (1 - drip * 0.6f));
    }

    static Color DirtPixel(int x, int y, int s)
    {
        float v = 0.3f + Fbm(x, y, s, 40) * 0.2f + (Hash(x, y, 41) - 0.5f) * 0.08f;
        float grass = Mathf.Clamp01((Fbm(x, y, s, 42) - 0.5f) * 3f);
        var c = Color.Lerp(new Color(0.42f, 0.36f, 0.28f), new Color(0.3f, 0.34f, 0.2f), grass);
        return Tint(c, v * 1.6f);
    }

    static Color WoodPixel(int x, int y, int s)
    {
        int plank = y / 8;
        bool gap = y % 8 == 0;
        float grain = Noise(x * 1.5f / s * 4, y * 8f / s * 4 + plank * 3, 32, 50);
        float v = gap ? 0.35f : 0.75f + grain * 0.25f + Hash(plank, 0, 51) * 0.1f;
        if (x < 2 || x >= s - 2) v *= 0.7f; // frame
        return Tint(new Color(0.5f, 0.36f, 0.22f), v);
    }

    static Color MetalPixel(int x, int y, int s)
    {
        float v = 0.45f + Fbm(x, y, s, 60) * 0.12f;
        float rust = Mathf.Clamp01((Fbm(x, y, s, 61) - 0.52f) * 3f);
        var c = Color.Lerp(new Color(0.5f, 0.52f, 0.55f), new Color(0.45f, 0.24f, 0.12f), rust);
        return Tint(c, v * 1.4f);
    }

    static Color FencePixel(int x, int y, int s)
    {
        // diamond chain-link
        int a = (x + y) % 8, b = (x - y + 64) % 8;
        bool wire = a == 0 || b == 0;
        float v = 0.5f + Hash(x, y, 70) * 0.2f;
        return wire ? new Color(v, v, v * 1.05f, 1) : new Color(0, 0, 0, 0);
    }

    static Color BarkPixel(int x, int y, int s)
    {
        float v = 0.4f + Noise(x * 6f / s, y * 1.5f / s, 6, 80) * 0.4f + (Hash(x, y, 81) - 0.5f) * 0.1f;
        return Tint(new Color(0.32f, 0.27f, 0.22f), v);
    }
}
