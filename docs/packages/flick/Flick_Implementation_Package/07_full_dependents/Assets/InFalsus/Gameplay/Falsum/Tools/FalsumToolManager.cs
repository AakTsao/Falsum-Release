using System;
using System.Collections.Generic;
using System.IO;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Tools
{
    public sealed class FalsumToolManager
    {
        private readonly List<FalsumToolManifest> _tools =
            new List<FalsumToolManifest>();

        public string ToolsRoot { get; private set; }

        public IList<FalsumToolManifest> Tools
        {
            get { return _tools; }
        }

        public void Initialize()
        {
            ToolsRoot = FalsumRuntimePaths.ExternalToolsDirectory;
            Directory.CreateDirectory(ToolsRoot);
            MigrateLegacyTools();
            InstallBundledTools();
            Reload();
        }

        public void Reload()
        {
            _tools.Clear();
            Directory.CreateDirectory(ToolsRoot);
            var directories = Directory.GetDirectories(ToolsRoot);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < directories.Length; i++)
            {
                var manifest = FalsumToolManifest.Load(
                    directories[i]);
                manifest.Enabled =
                    PlayerPrefs.GetInt(
                        EnabledKey(manifest.Id),
                        1) != 0;
                if (manifest.IsReady)
                {
                    try
                    {
                        FalsumToolRuntime.Probe(manifest);
                    }
                    catch (Exception exception)
                    {
                        manifest.LoadError =
                            exception.GetType().Name + ": " +
                            exception.Message;
                    }
                }
                _tools.Add(manifest);
            }
            _tools.Sort(
                delegate(
                    FalsumToolManifest left,
                    FalsumToolManifest right)
                {
                    var byName = string.Compare(
                        left.Name,
                        right.Name,
                        StringComparison.OrdinalIgnoreCase);
                    return byName != 0
                        ? byName
                        : string.Compare(
                            left.Id,
                            right.Id,
                            StringComparison.Ordinal);
                });
        }

        public FalsumToolManifest Find(string id)
        {
            for (var i = 0; i < _tools.Count; i++)
            {
                if (string.Equals(
                        _tools[i].Id,
                        id,
                        StringComparison.Ordinal))
                {
                    return _tools[i];
                }
            }
            return null;
        }

        public void SetEnabled(string id, bool enabled)
        {
            PlayerPrefs.SetInt(
                EnabledKey(id),
                enabled ? 1 : 0);
            PlayerPrefs.Save();
            var manifest = Find(id);
            if (manifest != null)
            {
                manifest.Enabled = enabled;
            }
        }

        public void OpenToolsFolder()
        {
            FalsumPlatform.OpenDirectory(ToolsRoot);
        }

        private void InstallBundledTools()
        {
            var sourceRoot = FalsumRuntimePaths.BundledToolsDirectory;
            if (!Directory.Exists(sourceRoot))
            {
                return;
            }
            var directories = Directory.GetDirectories(sourceRoot);
            for (var i = 0; i < directories.Length; i++)
            {
                var id = Path.GetFileName(directories[i]);
                var destination = Path.Combine(ToolsRoot, id);
                CopyDirectory(
                    directories[i],
                    destination,
                    true);
            }
        }

        private void MigrateLegacyTools()
        {
            var sourceRoot = FalsumRuntimePaths.LegacyToolsDirectory;
            if (!Directory.Exists(sourceRoot) ||
                string.Equals(
                    Path.GetFullPath(sourceRoot),
                    Path.GetFullPath(ToolsRoot),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            var directories = Directory.GetDirectories(sourceRoot);
            for (var i = 0; i < directories.Length; i++)
            {
                CopyDirectory(
                    directories[i],
                    Path.Combine(
                        ToolsRoot,
                        Path.GetFileName(directories[i])),
                    false);
            }
        }

        private static void CopyDirectory(
            string source,
            string destination,
            bool overwrite)
        {
            Directory.CreateDirectory(destination);
            var files = Directory.GetFiles(source);
            for (var i = 0; i < files.Length; i++)
            {
                if (string.Equals(
                        Path.GetExtension(files[i]),
                        ".meta",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var target = Path.Combine(
                    destination,
                    Path.GetFileName(files[i]));
                if (overwrite || !File.Exists(target))
                {
                    File.Copy(files[i], target, overwrite);
                }
            }
            var directories = Directory.GetDirectories(source);
            for (var i = 0; i < directories.Length; i++)
            {
                CopyDirectory(
                    directories[i],
                    Path.Combine(
                        destination,
                        Path.GetFileName(directories[i])),
                    overwrite);
            }
        }

        private static string EnabledKey(string id)
        {
            return "Falsum.Tool." + id + ".Enabled";
        }
    }
}

