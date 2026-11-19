using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public static class FalsumPlatform
    {
        public static void OpenDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(fullPath);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var startInfo = new ProcessStartInfo
            {
                FileName = fullPath,
                WorkingDirectory = fullPath,
                UseShellExecute = true,
                Verb = "open",
            };
            Process.Start(startInfo);
#else
            Application.OpenURL(new Uri(fullPath).AbsoluteUri);
#endif
        }

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
