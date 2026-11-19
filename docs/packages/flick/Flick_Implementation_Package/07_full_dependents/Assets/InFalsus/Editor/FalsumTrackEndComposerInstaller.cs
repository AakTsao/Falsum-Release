using System;
using InFalsus.Gameplay.Presentation.Backgrounds;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace InFalsus.EditorTools
{
    [InitializeOnLoad]
    public static class FalsumTrackEndComposerInstaller
    {
        private const string RendererPath =
            "Assets/Settings/PC_Renderer.asset";
        private const string FeaturePath =
            "Assets/Settings/FalsumTrackEndComposerFeature.asset";

        static FalsumTrackEndComposerInstaller()
        {
            if (Application.isBatchMode ||
                AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }
            EditorApplication.delayCall += () =>
            {
                EnsureInstalled(false);
            };
        }

        [MenuItem("InFalsus/Gameplay/Repair TrackEnd Composer")]
        public static void RepairFromMenu()
        {
            EnsureInstalled(true);
        }

        public static bool EnsureInstalled(bool logResult)
        {
            var rendererData =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                    RendererPath);
            if (rendererData == null)
            {
                Debug.LogError(
                    "[Falsum] TrackEnd composer renderer data is missing: " +
                    RendererPath);
                return false;
            }

            var feature =
                AssetDatabase.LoadAssetAtPath<
                    FalsumTrackEndComposerFeature>(FeaturePath);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<
                    FalsumTrackEndComposerFeature>();
                feature.name = "FalsumTrackEndComposerFeature";
                AssetDatabase.CreateAsset(feature, FeaturePath);
            }

            var changed = false;
            for (var index =
                     rendererData.rendererFeatures.Count - 1;
                 index >= 0;
                 index--)
            {
                var existing =
                    rendererData.rendererFeatures[index];
                if (existing == null)
                {
                    rendererData.rendererFeatures.RemoveAt(index);
                    changed = true;
                    continue;
                }
                if (existing == feature)
                {
                    continue;
                }
                var typeName = existing.GetType().Name;
                if (typeName.StartsWith(
                        "FalsumTrackEnd",
                        StringComparison.Ordinal))
                {
                    rendererData.rendererFeatures.RemoveAt(index);
                    changed = true;
                }
            }

            if (!rendererData.rendererFeatures.Contains(feature))
            {
                rendererData.rendererFeatures.Add(feature);
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(rendererData);
                AssetDatabase.SaveAssets();
            }
            if (logResult)
            {
                Debug.Log(
                    "[Falsum] TrackEnd composer is installed.");
            }
            return true;
        }

        public static bool IsInstalled(out string error)
        {
            error = string.Empty;
            var rendererData =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                    RendererPath);
            if (rendererData == null)
            {
                error = "PC_Renderer.asset is missing.";
                return false;
            }

            var count = 0;
            for (var index = 0;
                 index < rendererData.rendererFeatures.Count;
                 index++)
            {
                if (rendererData.rendererFeatures[index]
                    is FalsumTrackEndComposerFeature)
                {
                    count++;
                }
            }
            if (count != 1)
            {
                error =
                    "Expected one TrackEnd Composer feature, found " +
                    count + ".";
                return false;
            }
            return true;
        }
    }
}
