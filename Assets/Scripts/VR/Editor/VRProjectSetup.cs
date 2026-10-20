using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager.UI;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

/// <summary>
/// メニュー「BanjiRun/VR/...」で、Meta Quest 単体（OpenXR + XR Interaction Toolkit）向けにプロジェクトを設定する。
/// 何度実行してもよい。サンプルの取り込みはスクリプトのコンパイルが要るので、設定とは別のメニューにしている。
/// </summary>
public static class VRProjectSetup
{
    const string XriPackage = "com.unity.xr.interaction.toolkit";
    static readonly string[] XriSamples = { "Starter Assets", "XR Interaction Simulator" };

    const string AndroidAppId = "com.DefaultCompany.BanjiRun";
    const string QuestRPAssetPath = "Assets/Settings/Mobile_RPAsset.asset"; // Android の既定の画質（Mobile）が使う URP アセット
    const string XRSettingsFolder = "Assets/XR";
    const int TeleportLayerIndex = 31; // XRI のサンプルがテレポート用に予約している Interaction Layer

    [MenuItem("BanjiRun/VR/1. Import XRI Samples")]
    public static void ImportSamples()
    {
        string version = UnityEditor.PackageManager.PackageInfo.FindForPackageName(XriPackage)?.version;
        foreach (var sample in Sample.FindByPackage(XriPackage, version))
        {
            if (!XriSamples.Contains(sample.displayName) || sample.isImported) continue;
            bool ok = sample.Import(Sample.ImportOptions.OverridePreviousImports | Sample.ImportOptions.HideImportWindow);
            Debug.Log($"[VR] XRI サンプル取り込み: {sample.displayName} → {(ok ? sample.importPath : "失敗")}");
        }
    }

    [MenuItem("BanjiRun/VR/2. Setup Quest Project")]
    public static void Setup()
    {
        SetupAndroidPlayer();
        SetupXRManagement();
        SetupOpenXR();
        SetupInteractionLayers();
        SetupSimulator();
        SetupRenderPipeline();
        ApplyOpenXRFixes();
        AssetDatabase.SaveAssets();
        Debug.Log("[VR] Quest 向けの設定が完了");
    }

    /// <summary>コマンドから一度に流す用（-executeMethod VRProjectSetup.SetupAndBuildScene）。</summary>
    public static void SetupAndBuildScene()
    {
        Setup();
        VRSceneBuilder.Build();
    }

    [MenuItem("BanjiRun/VR/4. Build Quest APK")]
    public static void BuildQuestApk()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { VRSceneBuilder.ScenePath }, // Quest には VR 版のシーンだけを入れる（画面版は FlatSceneBuilder.BuildMacApp）
            locationPathName = "Builds/Quest/BanjiRun.apk",
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
        };
        var summary = BuildPipeline.BuildPlayer(options).summary;
        long bytes = File.Exists(options.locationPathName) ? new FileInfo(options.locationPathName).Length : 0;
        Debug.Log($"[VR] APK ビルド: {summary.result} / {options.locationPathName} / {bytes / (1024 * 1024)} MB / {summary.totalTime}");
        if (Application.isBatchMode && summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    static void SetupAndroidPlayer()
    {
        var android = NamedBuildTarget.Android;
        PlayerSettings.SetApplicationIdentifier(android, AndroidAppId);
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft; // Quest は Landscape Left のみ
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

        // 動かない物（Static）をビルド時にまとめて描画の回数を減らす。MeshCollider の下ごしらえもビルド時に済ませ、読み込みを速くする
        PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.Android, true);
        PlayerSettings.bakeCollisionMeshes = true;
    }

    static void SetupXRManagement()
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget))
        {
            if (!AssetDatabase.IsValidFolder(XRSettingsFolder)) AssetDatabase.CreateFolder("Assets", "XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, $"{XRSettingsFolder}/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
        }
        if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android)) perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

        // Android（Quest）だけ OpenXR を有効にする。Mac のエディターでは XR を起動せず、XR Interaction Simulator で試す
        var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        general.InitManagerOnStart = true;
        XRPackageMetadataStore.AssignLoader(general.Manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Android);
        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(perTarget);
    }

    static void SetupOpenXR()
    {
        var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (openxr == null)
        {
            Debug.LogError("[VR] OpenXR の Android 設定が見つかりません。Project Settings > XR Plug-in Management を一度開いてから再実行してください");
            return;
        }
        FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
        openxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
        var features = new[] { typeof(MetaQuestFeature), typeof(OculusTouchControllerProfile), typeof(MetaQuestTouchPlusControllerProfile) };
        foreach (var type in features)
        {
            OpenXRFeature feature = openxr.GetFeature(type);
            if (feature == null) Debug.LogWarning($"[VR] OpenXR の機能が見つかりません: {type.Name}");
            else feature.enabled = true;
        }
        EditorUtility.SetDirty(openxr);
    }

    static void SetupInteractionLayers()
    {
        var settings = GetXriSettings("UnityEngine.XR.Interaction.Toolkit.InteractionLayerSettings");
        if (settings == null) return;
        var so = new SerializedObject(settings);
        var names = so.FindProperty("m_LayerNames");
        if (names == null || names.arraySize <= TeleportLayerIndex) return;
        var layer = names.GetArrayElementAtIndex(TeleportLayerIndex);
        if (!string.IsNullOrEmpty(layer.stringValue) && layer.stringValue != "Teleport")
        {
            Debug.LogWarning($"[VR] Interaction Layer {TeleportLayerIndex} が \"{layer.stringValue}\" で使われているため、Teleport にしませんでした");
            return;
        }
        layer.stringValue = "Teleport";
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetupSimulator()
    {
        // Mac には Quest Link がないので、エディターではヘッドセットなしで試す。
        // 歩き回るのは VRNoHeadsetFallback（WASD + マウス）の方が分かりやすいので、XR Interaction Simulator は自動では出さない。
        // 手の細かい動きを試したいときは Project Settings > XR Plug-in Management > XR Interaction Toolkit で有効にする
        var prefab = FindSampleAsset<GameObject>("XR Interaction Simulator");
        if (prefab == null)
        {
            Debug.LogWarning("[VR] XR Interaction Simulator のプレハブが見つかりません。先に「1. Import XRI Samples」を実行してください");
            return;
        }
        var settings = GetXriSettings("UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRDeviceSimulatorSettings");
        if (settings == null) return;
        var so = new SerializedObject(settings);
        so.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab").boolValue = false;
        so.FindProperty("m_AutomaticallyInstantiateInEditorOnly").boolValue = true;
        so.FindProperty("m_UseClassic").boolValue = false;
        so.FindProperty("m_SimulatorPrefab").objectReferenceValue = prefab;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetupRenderPipeline()
    {
        // VR では解像度を下げるとぼやけて酔いやすく、MSAA が効く。HDR は Quest では重いので切る
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(QuestRPAssetPath);
        if (asset == null) return;
        asset.renderScale = 1f;
        asset.msaaSampleCount = 4;
        asset.supportsHDR = false;
        // 部屋のライトはベイクしたので、毎フレームのライトは後から足す物（懐中電灯など）だけ。1 つの物に効くのは 2 個まで、影は近くだけ
        asset.maxAdditionalLightsCount = 2;
        asset.shadowDistance = 15f;
        EditorUtility.SetDirty(asset);
    }

    /// <summary>OpenXR のプロジェクト検証で、自動で直せる項目を直す（Game Activity など）。</summary>
    static void ApplyOpenXRFixes()
    {
        var issues = new List<OpenXRFeature.ValidationRule>();
        for (int pass = 0; pass < 3; pass++)
        {
            issues.Clear();
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
            var fixable = issues.Where(i => i.fixIt != null && i.fixItAutomatic).ToList();
            if (fixable.Count == 0) break;
            foreach (var issue in fixable)
            {
                Debug.Log($"[VR] OpenXR の検証を自動修正: {issue.message}");
                issue.fixIt();
            }
        }
        foreach (var issue in issues)
            Debug.LogWarning($"[VR] OpenXR の検証で残った項目{(issue.error ? "（エラー）" : "")}: {issue.message}");
    }

    /// <summary>XRI の設定アセットを取る。クラスが internal なので型名で探す。無ければアセットが作られる。</summary>
    static Object GetXriSettings(string typeName)
    {
        var type = typeof(XRInteractionManager).Assembly.GetType(typeName);
        var instance = type?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (instance == null) Debug.LogWarning($"[VR] XRI の設定が見つかりません: {typeName}");
        return instance?.GetValue(null) as Object;
    }

    /// <summary>取り込んだサンプル（Assets/Samples）から、ファイル名が一致するアセットを探す。</summary>
    internal static T FindSampleAsset<T>(string fileName) where T : Object
    {
        string path = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { "Assets/Samples" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == fileName);
        return path == null ? null : AssetDatabase.LoadAssetAtPath<T>(path);
    }
}
