// Automates "Part A - Setting up your Unity and Quest for Development"
// (Leigh & Sijan, LAVA UH, Oct 2026) so the same setup can be reapplied to new projects.
//
// Menu: Tools > Quest Setup > Run Part A Setup
// CLI:  Unity -batchmode -quit -projectPath <proj> -buildTarget Android -executeMethod QuestProjectSetup.RunPartA
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;

public static class QuestProjectSetup
{
    const BuildTargetGroup Android = BuildTargetGroup.Android;
    const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
    const string MetaXRFeatureSetId = "com.meta.openxr.featureset.metaxr";

    [MenuItem("Tools/Quest Setup/Run Part A Setup")]
    public static void RunPartA()
    {
        // Step: File > Build Profiles > Android > Switch Platform
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(Android, BuildTarget.Android);
        Debug.Log("[QuestSetup] Active build target: " + EditorUserBuildSettings.activeBuildTarget);

        // Step: Project Settings > XR Plug-in Management > Android tab > check OpenXR
        EnableOpenXRForAndroid();

        // Step: "Enable the Meta XR feature set" prompt > Enable
        EnableMetaXRFeatureSet();

        // Step: Interaction SDK OpenXR Hand Skeleton Upgrade > Use OpenXR Hand
        UseOpenXRHand();

        // Step: Window > Meta > Tools > Project Setup Tool > Fix All + Apply All
        FixAllProjectSetupTasks();

        // Step: File > Save Project
        AssetDatabase.SaveAssets();
        Debug.Log("[QuestSetup] Part A setup complete.");
    }

    static void EnableOpenXRForAndroid()
    {
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
            out XRGeneralSettingsPerBuildTarget perTarget);
        if (perTarget == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }

        if (!perTarget.HasSettingsForBuildTarget(Android))
            perTarget.CreateDefaultSettingsForBuildTarget(Android);
        if (!perTarget.HasManagerSettingsForBuildTarget(Android))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(Android);

        var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(Android);
        general.InitManagerOnStart = true;
        var manager = perTarget.ManagerSettingsForBuildTarget(Android);
        if (!manager.activeLoaders.Any(l => l != null && l.GetType().FullName == OpenXRLoader))
            XRPackageMetadataStore.AssignLoader(manager, OpenXRLoader, Android);

        EditorUtility.SetDirty(perTarget);
        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(manager);
        Debug.Log("[QuestSetup] OpenXR loader on Android: " +
                  string.Join(", ", manager.activeLoaders.Select(l => l.GetType().Name)));
    }

    static void EnableMetaXRFeatureSet()
    {
        OpenXRFeatureSetManager.InitializeFeatureSets();
        var set = OpenXRFeatureSetManager.GetFeatureSetWithId(Android, MetaXRFeatureSetId);
        if (set == null)
        {
            Debug.LogError("[QuestSetup] Meta XR feature set not found. Is com.meta.xr.sdk.core installed?");
            return;
        }
        set.isEnabled = true;
        OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(Android);
        Debug.Log("[QuestSetup] Meta XR feature set enabled for Android.");
    }

    static void UseOpenXRHand()
    {
        var upgrade = FindType("Oculus.Interaction.Editor.OpenXRUpgrade");
        upgrade?.GetMethod("UpgradeToOpenXR", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        Debug.Log("[QuestSetup] Interaction SDK set to OpenXR hand skeleton.");
    }

    static void FixAllProjectSetupTasks()
    {
        var setup = FindType("OVRProjectSetup");
        if (setup == null)
        {
            Debug.LogError("[QuestSetup] OVRProjectSetup not found.");
            return;
        }
        var logEnum = setup.GetNestedType("LogMessages", BindingFlags.Public | BindingFlags.NonPublic);
        var fix = setup.GetMethod("FixTasks", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var update = setup.GetMethod("UpdateTasks", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var changed = Enum.Parse(logEnum, "Changed");

        // Fixes can unlock further tasks, so repeat like pressing Fix All / Apply All until it settles.
        for (var pass = 0; pass < 3; pass++)
        {
            update.Invoke(null, new object[] { Android, null, changed, true, null, true });
            fix.Invoke(null, new object[] { Android, null, changed, true, null, false });
        }
        update.Invoke(null, new object[] { Android, null, changed, true, null, true });
        Debug.Log("[QuestSetup] Project Setup Tool: Fix All / Apply All done for Android.");
    }

    static Type FindType(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(fullName, false))
            .FirstOrDefault(t => t != null);
}
