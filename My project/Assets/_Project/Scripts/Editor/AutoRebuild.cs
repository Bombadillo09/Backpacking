using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Runs every art setup step and rebuilds the prototype scene without any dialogs: ground textures,
    /// the nature pack, the imported packs, then Build Prototype Scene.
    /// <list type="bullet">
    /// <item>From the menu: Backpacking > Set Up Art and Rebuild Scene.</item>
    /// <item>On request while the editor is open: create Temp/backpacking-rebuild-request (any contents; a new
    /// value means a new request). The editor notices within a couple of seconds, even in the background,
    /// recompiles changed scripts first, and waits for Play mode to end.</item>
    /// <item>With the editor closed: Unity.exe -batchmode -quit -projectPath "My project"
    /// -executeMethod Backpacking.EditorTools.AutoRebuild.RunBatch</item>
    /// </list>
    /// Each run writes its outcome to Logs/backpacking-rebuild.log.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoRebuild
    {
        const string RequestPath = "Temp/backpacking-rebuild-request";
        const string LogPath = "Logs/backpacking-rebuild.log";
        const string RefreshedKey = "Backpacking.AutoRebuild.RefreshedFor";
        const double PollSeconds = 2.0;

        static double nextPollTime;
        static bool loggedWaitingForPlay;

        static AutoRebuild() => EditorApplication.update += Poll;

        [MenuItem("Backpacking/Set Up Art and Rebuild Scene")]
        static void RunFromMenu() => Run();

        /// <summary>Entry point for -executeMethod. Exits with code 1 on failure.</summary>
        public static void RunBatch()
        {
            if (!Run())
                EditorApplication.Exit(1);
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPollTime)
                return;
            nextPollTime = EditorApplication.timeSinceStartup + PollSeconds;
            if (!File.Exists(RequestPath))
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (!loggedWaitingForPlay)
                    Log("Rebuild requested; waiting for Play mode to end.");
                loggedWaitingForPlay = true;
                return;
            }
            loggedWaitingForPlay = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            // Pick up script changes first. If any compile, the domain reloads and polling starts again;
            // the session flag remembers that this request has already been refreshed for.
            string request = ReadRequest();
            if (SessionState.GetString(RefreshedKey, "") != request)
            {
                SessionState.SetString(RefreshedKey, request);
                AssetDatabase.Refresh();
                return;
            }

            string text = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            if (EditorUtility.scriptCompilationFailed)
            {
                Log("FAILED: scripts have compile errors. Fix them, then request again.");
                return;
            }
            // "run:Namespace.Type.Method" calls a static editor method instead of rebuilding (diagnostics, one-off fixes).
            if (text.StartsWith("run:"))
                RunMethod(text.Substring(4).Trim());
            else
                Run();
        }

        static string ReadRequest()
        {
            try
            {
                return File.ReadAllText(RequestPath) + File.GetLastWriteTimeUtc(RequestPath).Ticks;
            }
            catch (IOException)
            {
                return "";
            }
        }

        /// <summary>Runs every step. Returns false if one threw.</summary>
        static bool Run()
        {
            Log("Started.");
            try
            {
                Log(GroundTextureSetup.Apply(interactive: false));
                Log(NaturePackSetup.Apply(interactive: false));
                Log(ImportedPacksSetup.Apply(interactive: false));
                PrototypeSceneBuilder.Build(interactive: false);
                Log("SUCCEEDED: scene rebuilt.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Log($"FAILED: {exception}");
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void RunMethod(string fullName)
        {
            int dot = fullName.LastIndexOf('.');
            Type type = dot > 0 ? typeof(AutoRebuild).Assembly.GetType(fullName.Substring(0, dot)) : null;
            System.Reflection.MethodInfo method = type?.GetMethod(fullName.Substring(dot + 1),
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (method == null)
            {
                Log($"FAILED: no static method {fullName}.");
                return;
            }
            try
            {
                object result = method.Invoke(null, null);
                Log($"SUCCEEDED: ran {fullName}.{(result != null ? "\n" + result : "")}");
            }
            catch (Exception exception)
            {
                Log($"FAILED: {fullName}: {exception.InnerException ?? exception}");
            }
        }

        public static void Log(string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Debug.Log($"[AutoRebuild] {message}");
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // The log is a convenience; never fail the rebuild over it.
            }
        }
    }
}
