using System;
using System.IO;
using System.Linq;
using MoveIt.Games;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Android TV-box build: branding from <c>Assets/Resources/brand.json</c>, player settings for running as the box's
    /// home screen, and the APK build (to <c>builds/android/</c> next to the repo, on the D: drive).
    /// </summary>
    public static class BoxBuild
    {
        const string BrandPath = "Assets/Resources/brand.json";
        const string IconPath = "Assets/Branding/icon.png";
        const string BannerPath = "Assets/Branding/tv_banner.png";

        static Brand LoadBrand() =>
            File.Exists(BrandPath) ? JsonUtility.FromJson<Brand>(File.ReadAllText(BrandPath)) ?? new Brand() : new Brand();

        public static string ApkPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../../builds/android", $"{LoadBrand().name}.apk"));

        /// <summary>Product name, company, package id, icon, and no Unity splash (the box shows our boot animation).</summary>
        public static void ApplyBranding()
        {
            var brand = LoadBrand();
            PlayerSettings.productName = brand.name;
            PlayerSettings.companyName = brand.company;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, brand.androidPackage);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, brand.androidPackage);
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.backgroundColor = brand.BackgroundColor;
            ImportUncompressed(IconPath);
            ImportUncompressed(BannerPath);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            SetTvBanner(AssetDatabase.LoadAssetAtPath<Texture2D>(BannerPath));
        }

        const string ModelsSource = "../../motion-service-py/models";
        const string ModelsTarget = "Assets/Plugins/Android/MoveItMotion.androidlib/src/main/assets";

        /// <summary>
        /// The pose models live once, in motion-service-py/models (used by the PC service). Copy them into the
        /// Android motion library's assets so they ship inside the APK.
        /// </summary>
        static void CopyPoseModels()
        {
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ModelsSource));
            Directory.CreateDirectory(ModelsTarget);
            foreach (string name in new[] { "pose_landmarker_full.task", "pose_landmarker_lite.task" })
            {
                string from = Path.Combine(source, name), to = Path.Combine(ModelsTarget, name);
                if (!File.Exists(from)) throw new FileNotFoundException($"Pose model missing: {from}");
                if (!File.Exists(to) || new FileInfo(to).Length != new FileInfo(from).Length) File.Copy(from, to, true);
            }
        }

        /// <summary>
        /// This laptop has 16 GB RAM and a nearly full C: drive. Keep Gradle's download cache on D: (also set as the
        /// user's GRADLE_USER_HOME; setting it here makes it work even before Unity is restarted) and cap Gradle's
        /// memory at 2 GB so it can't push Windows into swapping.
        /// </summary>
        static void PrepareBuildMachine()
        {
            const string gradleHome = @"D:\.gradle";
            if (Directory.Exists(@"D:\")) Environment.SetEnvironmentVariable("GRADLE_USER_HOME", gradleHome);
            if (EditorPrefs.GetInt("JVMMaxHeapSize", 4096) > 2048) EditorPrefs.SetInt("JVMMaxHeapSize", 2048);
        }

        /// <summary>Icons look blurry when compressed: import them as uncompressed, no mipmaps.</summary>
        static void ImportUncompressed(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            if (importer.textureCompression == TextureImporterCompression.Uncompressed && !importer.mipmapEnabled) return;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// The 320x180 banner Android TV launchers show. Unity has no public API for it, so set the serialized
        /// player setting directly (what the Inspector does). Without it the box shows Unity's default banner.
        /// </summary>
        static void SetTvBanner(Texture2D banner)
        {
            if (banner == null) return;
            var settings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            var so = new SerializedObject(settings);
            var banners = so.FindProperty("m_AndroidBanners");
            if (banners == null || banners.arraySize == 0) return;
            so.FindProperty("androidEnableBanner").boolValue = true;
            banners.GetArrayElementAtIndex(0).FindPropertyRelative("banner").objectReferenceValue = banner;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("MoveIt/Box/Configure Android Settings")]
        public static void ConfigureAndroid()
        {
            ApplyBranding();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            // "Faster (smaller) builds": less generated C++, so builds need less time and memory, and the APK is smaller.
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Android, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel28;  // Android 9: USB cameras supported
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;     // matches Plugins/Android/AndroidManifest.xml
            PlayerSettings.Android.androidTVCompatibility = true;
            PlayerSettings.Android.forceInternetPermission = true;  // the motion service talks to the game over localhost UDP
            PlayerSettings.Android.startInFullscreen = true;        // no Android status/navigation bars
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            if (PlayerSettings.bundleVersion is "0.1" or "1.0") PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = VersionCode(PlayerSettings.bundleVersion);
            ApplySigning();
            AssetDatabase.SaveAssets();
            Debug.Log("[BoxBuild] Android settings configured for the TV box.");
        }

        /// <summary>
        /// versionCode from the version name: "1.2.3" -> 10203. Boxes only install a higher versionCode (Android
        /// refuses downgrades), so every published build needs a new version name (Project Settings > Player).
        /// </summary>
        public static int VersionCode(string version)
        {
            var parts = version.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor) ||
                !int.TryParse(parts[2], out int patch) || major < 0 || major > 20000 || minor is < 0 or > 99 || patch is < 0 or > 99)
                throw new BuildFailedException($"[BoxBuild] Version \"{version}\" must be MAJOR.MINOR.PATCH with MINOR and PATCH 0-99");
            return major * 10000 + minor * 100 + patch;
        }

        /// <summary>
        /// Release signing from environment variables, so the keystore and its passwords never enter the repo:
        /// TUNDU_KEYSTORE (path), TUNDU_KEYSTORE_PASS, TUNDU_KEY_ALIAS, TUNDU_KEY_PASS. Every update must be signed
        /// with the same key, or boxes refuse it. Without them the APK is debug-signed: fine for MuMu, never publish it
        /// (the release CLI refuses it).
        /// </summary>
        static bool ApplySigning()
        {
            string keystore = Environment.GetEnvironmentVariable("TUNDU_KEYSTORE");
            if (string.IsNullOrEmpty(keystore) || !File.Exists(keystore))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                return false;
            }
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("TUNDU_KEYSTORE_PASS") ?? "";
            PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("TUNDU_KEY_ALIAS") ?? "tundu";
            PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("TUNDU_KEY_PASS") ?? PlayerSettings.Android.keystorePass;
            return true;
        }

        /// <summary>The build to publish: refuses to run without the release keystore.</summary>
        [MenuItem("MoveIt/Box/Build Release APK (signed)")]
        public static void BuildReleaseApk()
        {
            if (!ApplySigning())
            {
                Debug.LogError("[BoxBuild] Release build needs TUNDU_KEYSTORE (+ TUNDU_KEYSTORE_PASS, TUNDU_KEY_ALIAS, TUNDU_KEY_PASS) set before Unity starts. See docs/cloud.md.");
                return;
            }
            BuildApk();
        }

        [MenuItem("MoveIt/Box/Build Android APK")]
        public static void BuildApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                // Switching platform reimports assets and recompiles; build in a second step once that has finished.
                if (EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                    Debug.Log("[BoxBuild] Switched the project to Android. Run MoveIt > Box > Build Android APK again.");
                else
                    Debug.LogError("[BoxBuild] Could not switch to Android. Is the Android module installed in Unity Hub?");
                return;
            }

            ConfigureAndroid();
            PrepareBuildMachine();
            CopyPoseModels();
            string apk = ApkPath;
            Directory.CreateDirectory(Path.GetDirectoryName(apk));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = apk,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BoxBuild] APK built: {apk} ({summary.totalSize / 1048576f:0.0} MB in {summary.totalTime.TotalMinutes:0.0} min), " +
                          $"version {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode}), " +
                          (PlayerSettings.Android.useCustomKeystore ? "release-signed" : "DEBUG-signed (not for publishing)"));
            }
            else
                Debug.LogError($"[BoxBuild] Android build {summary.result} with {summary.totalErrors} error(s). See the Console above.");
        }
    }
}
