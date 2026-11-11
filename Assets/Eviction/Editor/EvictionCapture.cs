using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Batch: -executeMethod EvictionCapture.CaptureBatch -shotDir &lt;folder&gt; [-views name,name]. PSX-look screenshots of the demo's places.</summary>
public static class EvictionCapture
{
    public static void CaptureBatch()
    {
        var args = System.Environment.GetCommandLineArgs();
        int ai = System.Array.IndexOf(args, "-shotDir");
        string dir = ai >= 0 ? args[ai + 1] : "Library/EvictionShots";
        Directory.CreateDirectory(dir);
        EditorSceneManager.OpenScene(EvictionBuilder.ScenePath, OpenSceneMode.Single);
        var manager = Object.FindFirstObjectByType<PSXManager>();
        var cam = Camera.main;
        Shader.SetGlobalVector("_PSXWeather", new Vector4(0.8f, 0f, 0f, 0f));
        Shader.SetGlobalVector("_PSXFlashPos", Vector4.zero);

        Transform M(string n) => Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(t => t.name == n);
        var office = M("Office Spawn"); var lot = M("Lot Spawn"); var home = M("Apartment Spawn");
        var clock = M("Punch Clock"); var door = M("Exit Door"); var car = M("His Car (S13)"); var notice = M("Eviction Notice");
        var aptDoor = M("Apartment Door 1B"); var building = M("Apartment Building");
        var views = new List<(string, Vector3, Vector3)>();
        void Look(string name, Vector3 eye, Vector3 at) => views.Add((name, eye, at));
        Vector3 Eye(Transform t) => t.position + Vector3.up * 1.6f;
        Look("office_spawn", Eye(office), Eye(office) + office.forward * 5f);
        Look("office_clock", clock.position + new Vector3(-0.6f, 0.35f, -1.8f), clock.position);
        Look("office_door", door.position + new Vector3(-3f, 0.5f, -1.2f), door.position);
        Look("office_desks", new Vector3(4f, 2.1f, -3.8f), new Vector3(-3f, 0.6f, 0.5f));
        Look("office_windows", new Vector3(-1f, 1.6f, 2.5f), new Vector3(1f, 1.4f, -5f));
        Look("lot_spawn", Eye(lot), Eye(lot) + lot.forward * 5f);
        Look("lot_back", Eye(lot) + lot.forward * 14f, Eye(lot) + Vector3.down * 0.5f);
        Look("lot_car", car.position + new Vector3(3f, 1.5f, 4f), car.position + Vector3.up * 0.6f);
        Look("home_spawn", Eye(home), Eye(home) + home.forward * 5f);
        Look("home_door", aptDoor.position + new Vector3(0.6f, 0.5f, -2.4f), aptDoor.position + Vector3.up * 0.3f);
        Look("home_wide", aptDoor.position + new Vector3(-9f, 3f, -16f), aptDoor.position + Vector3.up * 4f);
        Look("office_counter", new Vector3(-0.2f, 1.5f, 2.6f), new Vector3(0.6f, 1.0f, 5f));
        Look("office_board", new Vector3(3.6f, 1.6f, 3.0f), new Vector3(3.8f, 1.6f, 5f));
        Look("lot_lines", Eye(lot) + new Vector3(0, 1.5f, -3f), Eye(lot) + new Vector3(-6f, -1.6f, -20f));
        var wallClock = M("Wall Clock (6:30)");
        Look("clock_close", wallClock.position + new Vector3(0, -0.2f, -1.2f), wallClock.position);
        Look("lot_from_car", car.position + new Vector3(-2f, 1.6f, 3f), car.position + new Vector3(8f, 1.2f, 18f));
        var b = building.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
        Look("bld_negZ", b.center + new Vector3(0, -14f, -60f), b.center + Vector3.down * 14f);
        Look("bld_posZ", b.center + new Vector3(0, -14f, 60f), b.center + Vector3.down * 14f);
        Look("bld_posX", b.center + new Vector3(60f, -14f, 0), b.center + Vector3.down * 14f);
        Look("bld_negX", b.center + new Vector3(-60f, -14f, 0), b.center + Vector3.down * 14f);

        int vi = System.Array.IndexOf(args, "-views");
        var only = vi >= 0 ? new HashSet<string>(args[vi + 1].Split(',')) : null;
        var rt = new RenderTexture(426, 240, 24) { filterMode = FilterMode.Point };
        var tex = new Texture2D(426, 240, TextureFormat.RGB24, false);
        float far = cam.farClipPlane;
        foreach (var (name, eye, at) in views)
        {
            if (only != null && !only.Contains(name)) continue;
            cam.farClipPlane = name.StartsWith("bld") ? 300f : far;
            float fog = manager.fogDensity;
            if (name.StartsWith("bld")) manager.fogDensity = 0.004f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            manager.Apply();
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 426, 240), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            manager.fogDensity = fog;
        }
        cam.targetTexture = null;
        RenderTexture.active = null;

        // Why did the door finder fail? Probe the front wall.
        foreach (float h in new[] { 0.4f, 1.2f, 2f })
        {
            var from = new Vector3(b.center.x, h, b.min.z - 6f);
            bool ok = Physics.Raycast(from, Vector3.forward, out var hit, 40f);
            Debug.Log($"[Capture] ray h={h} from {from}: {(ok ? hit.collider.name + " at " + hit.point + " n=" + hit.normal : "miss")}");
        }
        foreach (var c in building.GetComponentsInChildren<Collider>()) Debug.Log($"[Capture] building collider {c.GetType().Name} on {c.name} bounds {c.bounds.center} {c.bounds.size}");
    }
}
