// Scene assembly following "Part C - Creating your first VR App" (Leigh & Sijan, LAVA UH, Oct 2026):
// delete Main Camera, add the Camera Rig and Teleport (Nav Mesh) Building Blocks, add Collider Surfaces
// to the walkable floors and assign them to the Teleport Interactables, and switch controller movement
// from Slide to Teleport. Then bakes lighting and builds the Quest APK.
//
// CLI: Unity -batchmode -projectPath <proj> -buildTarget Android -executeMethod ClientBathroom.EditorTools.BathroomXRSetup.BuildSceneFromCommandLine
//      Unity -batchmode -quit -projectPath <proj> -buildTarget Android -executeMethod ClientBathroom.EditorTools.BathroomXRSetup.BuildApk
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ClientBathroom.EditorTools
{
    public static class BathroomXRSetup
    {
        public const string ScenePath = "Assets/Scenes/ClientBathroom.unity";
        const string CameraRigBlock = "e47682b9-c270-40b1-b16d-90b627a5ce1b";
        const string TeleportBlock = "85906e61-05be-4ea9-bfa6-139215b858df";
        static readonly Vector3 SpawnInches = new(82f, 0f, 18f);   // just inside the door, facing north

        static bool _exitWhenDone;

        [MenuItem("Tools/Client Bathroom/Build Scene (room + XR rig + lighting)")]
        public static void BuildSceneMenu() => _ = BuildScene();

        public static void BuildSceneFromCommandLine()
        {
            _exitWhenDone = true;
            _ = BuildScene();
        }

        static async Task BuildScene()
        {
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

                // Part C step: delete the existing Main Camera
                var cam = GameObject.Find("Main Camera");
                if (cam != null) UnityEngine.Object.DestroyImmediate(cam);

                // Afternoon sun through the west window, baked
                var sun = UnityEngine.Object.FindAnyObjectByType<Light>();
                if (sun != null)
                {
                    sun.name = "Sun";
                    sun.lightmapBakeType = LightmapBakeType.Baked;
                    sun.transform.rotation = Quaternion.Euler(32f, 75f, 0f);
                    sun.intensity = 1.4f; sun.color = new Color(1f, 0.93f, 0.82f);
                    sun.shadows = LightShadows.Soft;
                }

                BathroomBuilder.Build();
                EditorSceneManager.SaveScene(scene, ScenePath);

                // Part C step: drag in the Camera Rig building block, then the Teleport block (Nav Mesh variant)
                await InstallBlock(CameraRigBlock, null);
                await InstallBlock(TeleportBlock, "NavMesh");

                ConfigureTeleportAndLocomotion();
                PlaceRig();
                ConfigureRendering();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);

                BakeLighting();
                EditorSceneManager.SaveScene(scene, ScenePath);
                AddSceneToBuild();
                AssetDatabase.SaveAssets();
                Debug.Log("[ClientBathroom] Scene built: " + ScenePath);
                if (_exitWhenDone) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[ClientBathroom] Scene build failed: " + e);
                if (_exitWhenDone) EditorApplication.Exit(1);
            }
        }

        // ---------------------------------------------------------------- Building Blocks

        static async Task InstallBlock(string blockId, string teleportVariant)
        {
            var utils = FindType("Meta.XR.BuildingBlocks.Editor.Utils");
            var block = utils.GetMethod("GetBlockData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null).Invoke(null, new object[] { blockId });
            if (block == null) throw new Exception("Building block not found: " + blockId);
            var blockName = block.GetType().GetProperty("BlockName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(block);

            // Answer the variant dialog the way the tutorial does (e.g. Teleport -> "Nav Mesh", then Confirm)
            var interfaceType = FindType("Meta.XR.BuildingBlocks.Editor.InterfaceBlockData");
            if (interfaceType.IsInstanceOfType(block))
            {
                var selection = interfaceType.GetField("Selection", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                var selType = selection.GetType();
                selType.GetMethod("SetupForSelection").Invoke(selection, new[] { block });
                if (teleportVariant != null)
                {
                    foreach (var handle in (IEnumerable)selection)
                    {
                        var hType = handle.GetType();
                        var member = (MemberInfo)hType.GetProperty("MemberInfo").GetValue(handle);
                        if (member.Name != "variant" || member is not FieldInfo field || !field.FieldType.IsEnum) continue;
                        if (!Enum.GetNames(field.FieldType).Contains(teleportVariant)) continue;
                        hType.GetProperty("RawValue").SetValue(handle, Enum.Parse(field.FieldType, teleportVariant));
                    }
                }
                selType.GetMethod("UpdateVariants").Invoke(selection, null);
                selType.GetProperty("Completed").SetValue(selection, true);
            }

            var install = block.GetType().GetMethod("InstallWithDependencies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(GameObject) }, null);
            var task = (Task)install.Invoke(block, new object[] { null });
            await task;
            Debug.Log($"[ClientBathroom] Installed building block: {blockName}");
        }

        static void ConfigureTeleportAndLocomotion()
        {
            var interactableType = FindType("Oculus.Interaction.Locomotion.TeleportInteractable");
            var colliderSurfaceType = FindType("Oculus.Interaction.Surfaces.ColliderSurface");
            var interactables = UnityEngine.Object.FindObjectsByType(interactableType, FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
                .Cast<Component>().OrderBy(c => c.name).ToArray();
            if (interactables.Length == 0) throw new Exception("No Teleport Interactables were created.");

            // Collider Surface on each walkable floor (the tutorial's two planes): main floor and pebble shower floor
            var floors = new[] { GameObject.Find("Bathroom/Floor/FloorWalkable"), GameObject.Find("Bathroom/Floor/ShowerFloorPebble") };
            for (var i = 0; i < interactables.Length; i++)
            {
                var floor = floors[Mathf.Min(i, floors.Length - 1)];
                var surface = floor.GetComponent(colliderSurfaceType) ?? floor.AddComponent(colliderSurfaceType);
                var sso = new SerializedObject(surface);
                sso.FindProperty("_collider").objectReferenceValue = floor.GetComponent<Collider>();
                sso.ApplyModifiedPropertiesWithoutUndo();

                var so = new SerializedObject(interactables[i]);
                so.FindProperty("_allowTeleport").boolValue = true;
                so.FindProperty("_surface").objectReferenceValue = surface;
                so.ApplyModifiedPropertiesWithoutUndo();
                interactables[i].gameObject.name = $"[BuildingBlock] Teleport Interactable ({floor.name})";
                Debug.Log($"[ClientBathroom] {interactables[i].gameObject.name}: Allow Teleport on, Surface = {floor.name}");
            }

            // Locomotor > LocomotionSettings: Controller Movement Slide -> Teleport
            var movingType = FindType("Oculus.Interaction.Locomotion.MovingSetting");
            foreach (var moving in UnityEngine.Object.FindObjectsByType(movingType, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(moving);
                var p = so.FindProperty("_controllerMovement._value");
                p.enumValueIndex = Array.IndexOf(p.enumNames, "Teleport");
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[ClientBathroom] Controller Movement set to Teleport");
            }
        }

        static void PlaceRig()
        {
            var rigType = FindType("OVRCameraRig");
            var rig = (Component)UnityEngine.Object.FindAnyObjectByType(rigType);
            var root = rig.transform.root;
            root.position = SpawnInches * MeshKit.In;
            root.rotation = Quaternion.identity;
        }

        // ---------------------------------------------------------------- Rendering, lighting, build

        static void ConfigureRendering()
        {
            var assets = Enumerable.Range(0, QualitySettings.count).Select(QualitySettings.GetRenderPipelineAssetAt)
                .Append(GraphicsSettings.defaultRenderPipeline).Where(a => a != null).Distinct();
            foreach (var rp in assets)
            {
                var so = new SerializedObject(rp);
                var box = so.FindProperty("m_ReflectionProbeBoxProjection");
                if (box != null) box.boolValue = true;
                var atlas = so.FindProperty("m_ReflectionProbeAtlas");   // atlas left mirrors showing the skybox
                if (atlas != null) atlas.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.6f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        }

        static void BakeLighting()
        {
            const string path = "Assets/Bathroom/ClientBathroomLighting.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (ls == null) { ls = new LightingSettings { name = "ClientBathroomLighting" }; AssetDatabase.CreateAsset(ls, path); }
            ls.bakedGI = true; ls.realtimeGI = false;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.lightmapResolution = 50; ls.lightmapPadding = 4; ls.lightmapMaxSize = 2048;
            ls.directSampleCount = 64; ls.indirectSampleCount = 512; ls.environmentSampleCount = 256;
            ls.maxBounces = 3; ls.ao = true; ls.aoMaxDistance = 0.4f;
            ls.directionalityMode = LightmapsMode.NonDirectional;
            ls.lightmapCompression = LightmapCompression.NormalQuality;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            Lightmapping.lightingSettings = ls;
            var t = DateTime.Now;
            var ok = Lightmapping.Bake();
            Debug.Log($"[ClientBathroom] Lightmap bake {(ok ? "succeeded" : "FAILED")} in {(DateTime.Now - t).TotalSeconds:F0}s");

            // Feed the baked room cubemap to the box-projected mirrors
            var probe = UnityEngine.Object.FindAnyObjectByType<ReflectionProbe>();
            var mirror = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bathroom/Materials/MirrorBox.mat");
            if (probe != null && probe.bakedTexture != null && mirror != null)
            {
                mirror.SetTexture("_Cube", probe.bakedTexture);
                EditorUtility.SetDirty(mirror);
            }
        }

        static void AddSceneToBuild()
        {
            var scenes = EditorBuildSettings.scenes.Select(s => new EditorBuildSettingsScene(s.path, false))
                .Where(s => s.path != ScenePath && System.IO.File.Exists(s.path)).ToList();   // drop renamed/deleted scenes
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        [MenuItem("Tools/Client Bathroom/Build Quest APK")]
        public static void BuildApk()
        {
            PlayerSettings.companyName = "Brooke Clifton";
            PlayerSettings.productName = "Client Bathroom";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.brookeclifton.clientbathroom");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = "Builds/ClientBathroom.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            Debug.Log($"[ClientBathroom] APK build: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, " +
                      $"{report.summary.totalErrors} errors");
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }

        static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null)
            ?? throw new Exception("Type not found: " + fullName);
    }
}
