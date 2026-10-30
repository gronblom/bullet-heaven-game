using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace BulletHeaven.Unity.Editor
{
    /// <summary>
    /// Keeps the Unity project fed from the rest of the repository:
    /// - builds ../core (the shared gameplay library) with the .NET SDK and copies the DLLs into
    ///   Assets/Plugins/Core,
    /// - copies the generated models and textures from ../assets into Assets/Resources,
    /// - makes sure URP is the active render pipeline.
    /// The core DLLs are committed (Unity only runs editor scripts once every script compiles, and the
    /// game scripts need them), so this runs by itself only when the DLLs or the models are missing;
    /// when ../core has newer sources it says so. Otherwise use the menu (Bullet Heaven > Sync core +
    /// models), or the command line:
    ///   Unity.exe -batchmode -quit -projectPath unity -executeMethod BulletHeaven.Unity.Editor.Setup.Run -logFile setup.log
    /// </summary>
    [InitializeOnLoad]
    public static class Setup
    {
        private const string PluginFolder = "Assets/Plugins/Core";

        // Unity's own runtime already provides this one; a second copy is a compile error.
        private static readonly string[] SkipDlls = { "System.Runtime.CompilerServices.Unsafe.dll" };

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        static Setup()
        {
            if (Application.isBatchMode)
                return; // Run is called explicitly there
            EditorApplication.delayCall += () =>
            {
                EnsureRenderPipeline();
                EnsureLegacyInput();
                if (!File.Exists(CoreDll))
                    Sync();
                else if (!Directory.Exists(Path.Combine(Application.dataPath, "Resources", "Icons")) || ModelsOutdated())
                    CopyAssets(); // the DLLs are committed; just bring the models in
                else if (CoreIsStale())
                    Debug.LogWarning("Bullet Heaven: ../core has changed since Assets/Plugins/Core was built. Use Bullet Heaven > Sync core + models.");
            };
        }

        /// <summary>Command-line entry point: sync everything and exit non-zero on failure.</summary>
        public static void Run()
        {
            try
            {
                EnsureRenderPipeline();
                EnsureLegacyInput();
                if (!Sync())
                    EditorApplication.Exit(1);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Bullet Heaven/Sync core + models")]
        public static bool Sync()
        {
            bool ok = BuildCore();
            CopyAssets();
            Debug.Log(ok ? "Bullet Heaven: core and models synced." : "Bullet Heaven: core build FAILED, see the log above.");
            return ok;
        }

        /// <summary>Copies the generated models and textures from ../assets into Assets/Resources.</summary>
        private static void CopyAssets()
        {
            CopyFolder(Path.Combine(RepoRoot, "assets", "models"), Path.Combine(Application.dataPath, "Resources", "Models"), "*.glb");
            CopyFolder(Path.Combine(RepoRoot, "assets", "textures"), Path.Combine(Application.dataPath, "Resources", "Textures"), "*.jpg");
            CopyFolder(Path.Combine(RepoRoot, "assets", "icons"), Path.Combine(Application.dataPath, "Resources", "Icons"), "*.png");
            AssetDatabase.Refresh();
            Debug.Log("Bullet Heaven: models, textures and icons copied into Assets/Resources.");
        }

        /// <summary>True when ../assets has models or icons newer than the copies in Resources (after a git pull).</summary>
        private static bool ModelsOutdated()
        {
            foreach (var (folder, pattern, target) in new[] { ("models", "*.glb", "Models"), ("icons", "*.png", "Icons"), ("textures", "*.jpg", "Textures") })
            {
                string from = Path.Combine(RepoRoot, "assets", folder);
                if (!Directory.Exists(from))
                    continue;
                foreach (var file in Directory.GetFiles(from, pattern))
                {
                    string copy = Path.Combine(Application.dataPath, "Resources", target, Path.GetFileName(file));
                    if (!File.Exists(copy) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(copy))
                        return true;
                }
            }
            return false;
        }

        private static string CoreDll => Path.Combine(Application.dataPath, "Plugins", "Core", "BulletHeaven.Core.dll");

        private static bool CoreIsStale()
        {
            if (!File.Exists(CoreDll))
                return true;
            // A minute's grace: after a git checkout sources and DLL get near-identical times.
            var built = File.GetLastWriteTimeUtc(CoreDll).AddMinutes(1);
            foreach (var source in Directory.GetFiles(Path.Combine(RepoRoot, "core"), "*.cs", SearchOption.AllDirectories))
            {
                if (!source.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && File.GetLastWriteTimeUtc(source) > built)
                    return true;
            }
            return false;
        }

        /// <summary>dotnet publish ../core into a temp folder, then copy the DLLs Unity needs.</summary>
        private static bool BuildCore()
        {
            string output = Path.Combine(Path.GetTempPath(), "BulletHeaven.Core.publish");
            string project = Path.Combine(RepoRoot, "core", "BulletHeaven.Core.csproj");
            var info = new ProcessStartInfo("dotnet", $"publish \"{project}\" -c Release -o \"{output}\" -nologo")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            try
            {
                using var process = Process.Start(info);
                string log = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    Debug.LogError("Building ../core failed (is the .NET 8 SDK installed and `dotnet` on PATH?):\n" + log);
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Couldn't run `dotnet` to build ../core. Install the .NET 8 SDK and restart Unity.\n" + e.Message);
                return false;
            }

            string target = Path.Combine(Application.dataPath, "Plugins", "Core");
            Directory.CreateDirectory(target);
            foreach (var dll in Directory.GetFiles(output, "*.dll"))
            {
                if (Array.IndexOf(SkipDlls, Path.GetFileName(dll)) < 0)
                    File.Copy(dll, Path.Combine(target, Path.GetFileName(dll)), true);
            }
            return true;
        }

        private static void CopyFolder(string from, string to, string pattern)
        {
            if (!Directory.Exists(from))
            {
                Debug.LogWarning($"Bullet Heaven: {from} not found; the game will use primitive placeholders.");
                return;
            }
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from, pattern))
            {
                string destination = Path.Combine(to, Path.GetFileName(file));
                if (!File.Exists(destination) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(destination))
                    File.Copy(file, destination, true);
            }
        }

        /// <summary>
        /// The game reads UnityEngine.Input (the classic input manager). Projects created from the newer
        /// templates only enable the Input System package, so switch "Active Input Handling" to Both.
        /// </summary>
        private static void EnsureLegacyInput()
        {
            var settings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            if (settings == null)
                return;
            var serialized = new SerializedObject(settings);
            var handler = serialized.FindProperty("activeInputHandler");
            if (handler == null || handler.intValue != 1)
                return; // 0 = old only, 2 = both: fine
            handler.intValue = 2;
            serialized.ApplyModifiedProperties();
            Debug.LogWarning("Bullet Heaven: set Player Settings > Active Input Handling to Both. Restart Unity for it to take effect.");
        }

        /// <summary>Creates and assigns a URP asset if the project has no render pipeline yet.</summary>
        private static void EnsureRenderPipeline()
        {
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset)
                return;

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Settings"));
            var asset = UniversalRenderPipelineAsset.Create();
            asset.shadowDistance = 140f;
            asset.supportsHDR = true;
            asset.msaaSampleCount = 4;
            asset.supportsCameraDepthTexture = true;
            AssetDatabase.CreateAsset(asset, "Assets/Settings/URP.asset");
            // The renderer data is created alongside; keep it as its own asset so it survives reloads.
            var rendererData = asset.rendererDataList.Length > 0 ? asset.rendererDataList[0] : null;
            if (rendererData != null && !AssetDatabase.Contains(rendererData))
                AssetDatabase.AddObjectToAsset(rendererData, asset);
            AssetDatabase.SaveAssets();

            GraphicsSettings.defaultRenderPipeline = asset;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            // Write ProjectSettings now, so the switch survives even if the editor goes down afterwards.
            AssetDatabase.SaveAssets();
            Debug.Log("Bullet Heaven: created Assets/Settings/URP.asset and made URP the render pipeline.");
        }
    }

    /// <summary>Imports the *_normal textures as normal maps.</summary>
    public sealed class TextureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/Textures/"))
                return;
            var importer = (TextureImporter)assetImporter;
            if (assetPath.EndsWith("_normal.jpg", StringComparison.OrdinalIgnoreCase))
                importer.textureType = TextureImporterType.NormalMap;
            else if (assetPath.EndsWith("_roughness.jpg", StringComparison.OrdinalIgnoreCase))
                importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 8;
        }
    }
}
