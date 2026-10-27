// Renders review stills of the bathroom from eye height (and a plan view) to Builds/Snapshots.
// CLI: Unity -batchmode -quit -projectPath <proj> -executeMethod MomsBathroom.EditorTools.BathroomSnapshots.Render
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MomsBathroom.EditorTools
{
    public static class BathroomSnapshots
    {
        const float Eye = 64f;

        [MenuItem("Tools/Moms Bathroom/Render Review Snapshots")]
        public static void Render()
        {
            EditorSceneManager.OpenScene(BathroomXRSetup.ScenePath);
            Directory.CreateDirectory("Builds/Snapshots");
            Shot("01_door_looking_north", new Vector3(82, Eye, 10), new Vector3(40, 40, 123), 90);
            Shot("02_east_vanity", new Vector3(45, Eye, 40), new Vector3(126, 45, 40), 90);
            Shot("03_west_tub_deck", new Vector3(95, Eye, 50), new Vector3(0, 40, 40), 90);
            Shot("04_shower_and_north", new Vector3(95, Eye, 60), new Vector3(20, 45, 123), 90);
            Shot("05_south_door_wall", new Vector3(70, Eye, 118), new Vector3(70, 45, 0), 95);
            Shot("06_toilet_corner", new Vector3(60, Eye, 70), new Vector3(126, 30, 112), 85);
            Plan("07_plan");

            // Each design option, from the views where they differ
            var options = Object.FindAnyObjectByType<TileOptionSwitcher>();
            var keys = new[] { "1", "2", "2a", "2b", "3" };
            for (var i = 0; options != null && i < options.options.Length; i++)
            {
                options.Apply(i, false);
                Shot($"opt{keys[i]}_east", new Vector3(40, Eye, 55), new Vector3(126, 45, 70), 100);
                Shot($"opt{keys[i]}_vanity", new Vector3(70, 50, 36), new Vector3(126, 30, 36), 85);
                Shot($"opt{keys[i]}_south", new Vector3(70, Eye, 118), new Vector3(70, 45, 0), 95);
                Shot($"opt{keys[i]}_toiletwall", new Vector3(80, 52, 88), new Vector3(105, 42, 123), 85);
            }
            if (options != null) options.Apply(options.startOption, false);
        }

        // Quick check of the Option 2 niche wall
        public static void RenderToiletWall()
        {
            EditorSceneManager.OpenScene(BathroomXRSetup.ScenePath);
            Directory.CreateDirectory("Builds/Snapshots");
            var options = Object.FindAnyObjectByType<TileOptionSwitcher>();
            options.Apply(1, false);
            Shot("opt2_toiletwall", new Vector3(80, 52, 88), new Vector3(100, 42, 123), 85);
            Shot("opt2_toiletwall_close", new Vector3(98, 46, 106), new Vector3(100.4f, 36, 123), 75);
            Shot("detail_tub_end", new Vector3(60, 60, 40), new Vector3(36, 40, 0), 85);
            Shot("detail_hotelier", new Vector3(60, 58, 34), new Vector3(24, 58, 0), 75);
            Shot("detail_west_window", new Vector3(80, 64, 42), new Vector3(0, 70, 42), 75);
            Shot("detail_north_window", new Vector3(66, 60, 90), new Vector3(66, 66, 123), 75);
            Shot("detail_south_niche", new Vector3(105, 62, 40), new Vector3(112, 62, 0), 75);
            Shot("detail_shower", new Vector3(70, 60, 75), new Vector3(24, 55, 123), 80);
            Shot("detail_sink_faucets", new Vector3(85, 55, 36), new Vector3(126, 42, 36), 80);
            Shot("detail_curb", new Vector3(80, 45, 70), new Vector3(46, 0, 105), 80);
        }

        static Camera MakeCam(int w, int h, out RenderTexture rt)
        {
            var go = new GameObject("SnapshotCam");
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 50f;
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            return cam;
        }

        static void Shot(string name, Vector3 eyeIn, Vector3 targetIn, float fov)
        {
            var cam = MakeCam(1600, 1000, out var rt);
            cam.fieldOfView = fov / 1.6f;
            cam.transform.position = eyeIn * MeshKit.In;
            cam.transform.LookAt(targetIn * MeshKit.In);
            Save(cam, rt, name);
        }

        static void Plan(string name)
        {
            var ceiling = GameObject.Find("Bathroom/CeilingAndLighting/Ceiling");
            if (ceiling != null) ceiling.SetActive(false);
            var cam = MakeCam(1400, 1400, out var rt);
            cam.orthographic = true; cam.orthographicSize = 66f * MeshKit.In;
            cam.transform.position = new Vector3(63f, 140f, 61.5f) * MeshKit.In;
            // Match the client's floor plan: west wall at the top of the image
            cam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.left);
            Save(cam, rt, name);
            if (ceiling != null) ceiling.SetActive(true);
        }

        static void Save(Camera cam, RenderTexture rt, string name)
        {
            cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes($"Builds/Snapshots/{name}.png", tex.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(tex);
            rt.Release();
        }
    }
}
