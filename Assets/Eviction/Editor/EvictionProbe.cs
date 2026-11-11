using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Batch helper: logs size, pivot and parts of the models EVICTION uses (for laying out scenes).</summary>
public static class EvictionProbe
{
    public static void ProbeBatch()
    {
        string[] paths =
        {
            "Assets/Polyeler/Simple Retro Car/Prefabs/Simple Retro Car.prefab",
            "Assets/Awbmecreations/Mobile Optimize-Free Low Poly Cars/Prefabs/Sport Car_39.prefab",
            "Assets/Awbmecreations/Mobile Optimize-Free Low Poly Cars/Prefabs/Hatchback Car_15.prefab",
            "Assets/Awbmecreations/Mobile Optimize-Free Low Poly Cars/Prefabs/Classic Car_9.prefab",
            "Assets/Awbmecreations/Mobile Optimize-Free Low Poly Cars/Prefabs/N_Muscle Car_10.prefab",
            "Assets/Awbmecreations/Mobile Optimize-Free Low Poly Cars/Prefabs/Pick Up_11.prefab",
            "Assets/Awbmecreations/Mobile Optimize-Free Low Poly Cars/Prefabs/N Van_10.prefab",
            "Assets/Arabic Neoclassical/Buildings/Prefabs/Building 04.prefab",
        };
        var all = paths.Concat(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/nappin" }).Select(AssetDatabase.GUIDToAssetPath))
                       .Concat(new[] { "office_building_1_with_base", "office_building_2_with_base", "office_building_3_with_base", "office_building_4_with_base",
                                       "lamp_pole_single_white", "lamp_pole_dual_white", "lamp_pole_small", "bench", "tree_1", "concrete_block", "hedge_with_base", "mid_house_1", "small_house_1" }
                       .Select(n => AssetDatabase.FindAssets(n + " t:Prefab", new[] { "Assets/Versatile Studio Assets" }).Select(AssetDatabase.GUIDToAssetPath).First(p => System.IO.Path.GetFileNameWithoutExtension(p) == n)));

        foreach (var p in all)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (!prefab) { Debug.Log("[Probe] missing " + p); continue; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = Vector3.zero;
            var rs = go.GetComponentsInChildren<Renderer>().Where(x => x.enabled).ToArray();
            if (rs.Length == 0) { Debug.Log("[Probe] no renderers " + p); Object.DestroyImmediate(go); continue; }
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            int tris = go.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh).Sum(f => (int)f.sharedMesh.triangles.Length / 3);
            var mats = rs.SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name + (m.mainTexture ? "(" + m.mainTexture.name + " " + m.mainTexture.width + ")" : "") + ":" + ColorUtility.ToHtmlStringRGB(m.HasProperty("_Color") ? m.color : Color.white)).Distinct();
            var kids = go.GetComponentsInChildren<Transform>().Where(t => t != go.transform).Select(t => t.name).Take(25);
            Debug.Log($"[Probe] {System.IO.Path.GetFileNameWithoutExtension(p)} center {b.center} size {b.size} min.y {b.min.y:0.00} tris {tris} cols {go.GetComponentsInChildren<Collider>().Length}\n   mats: {string.Join(", ", mats)}\n   parts: {string.Join(", ", kids)}");
            Object.DestroyImmediate(go);
        }
    }
}

public static class EvictionPreview
{
    /// <summary>Batch: renders a few prefabs from 4 sides with plain unlit textures into -shotDir.</summary>
    public static void PreviewBatch()
    {
        var args = System.Environment.GetCommandLineArgs();
        int ai = System.Array.IndexOf(args, "-shotDir");
        string dir = args[ai + 1];
        System.IO.Directory.CreateDirectory(dir);
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        var items = new (string name, string path)[]
        {
            ("building04", "Assets/Arabic Neoclassical/Buildings/Prefabs/Building 04.prefab"),
            
            
            
        };
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        var camGo = new GameObject("cam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.3f, 0.35f, 0.4f);
        cam.fieldOfView = 40f;
        cam.farClipPlane = 1000f;
        var rt = new RenderTexture(640, 400, 24);
        var tex = new Texture2D(640, 400, TextureFormat.RGB24, false);
        foreach (var it in items)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(it.path));
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var gl in go.GetComponentsInChildren<Renderer>().Where(x => x.name == "Glass")) gl.enabled = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>().Where(x => x.enabled))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var m = new Material(unlit);
                    if (ms[i] && ms[i].mainTexture) m.SetTexture("_BaseMap", ms[i].mainTexture);
                    if (ms[i] && ms[i].HasProperty("_Color")) m.SetColor("_BaseColor", ms[i].color);
                    if (ms[i] && ms[i].mainTexture) { m.SetTextureScale("_BaseMap", ms[i].mainTextureScale); m.SetTextureOffset("_BaseMap", ms[i].mainTextureOffset); }
                    ms[i] = m;
                }
                r.sharedMaterials = ms;
            }
            var rs = go.GetComponentsInChildren<Renderer>().Where(x => x.enabled).ToArray();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float dist = b.extents.magnitude * 2.6f;
            string[] sides = { "pZ", "nZ", "pX", "nX" };
            Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            for (int s = 0; s < 4; s++)
            {
                var pos = b.center + dirs[s] * dist + Vector3.up * b.extents.y * 0.3f;
                cam.transform.position = pos;
                cam.transform.LookAt(b.center);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 640, 400), 0, 0);
                tex.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"{it.name}_{sides[s]}.png"), tex.EncodeToPNG());
            }
            // close-up of the ground floor from the +Z and -Z sides
            if (it.name != "retrocar")
                for (int s = 0; s < 4; s++)
                {
                    var target = s < 2 ? new Vector3(b.center.x, b.min.y + 4f, s == 0 ? b.max.z : b.min.z) : new Vector3(s == 2 ? b.max.x : b.min.x, b.min.y + 4f, b.center.z);
                    cam.transform.position = target + dirs[s] * 26f + Vector3.up * 1f;
                    cam.transform.LookAt(target);
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 640, 400), 0, 0);
                    tex.Apply();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"{it.name}_ground_{sides[s]}.png"), tex.EncodeToPNG());
                }
            Debug.Log($"[Preview] {it.name} bounds {b.center} {b.size}");
            Object.DestroyImmediate(go);
        }
        cam.targetTexture = null;
        RenderTexture.active = null;
    }
}
