using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace InFalsus.Gameplay.Falsum.UI
{
    public static class FalsumUiStyles
    {
        private static StyleSheet _studio;
        private static StyleSheet _canvas;
        private static StyleSheet _inFalsus;

        public static void Apply(
            VisualElement root,
            FalsumUiStyle style)
        {
            if (root == null)
            {
                return;
            }
            RemoveStyle(root, _studio);
            RemoveStyle(root, _canvas);
            RemoveStyle(root, _inFalsus);
            root.EnableInClassList("is-infalsus", style == FalsumUiStyle.InFalsus);
            switch (style)
            {
                case FalsumUiStyle.Studio:
                    AddStyle(
                        root,
                        ref _studio,
                        "Falsum/UI/FalsumStudio");
                    break;
                case FalsumUiStyle.Canvas:
                    AddStyle(
                        root,
                        ref _canvas,
                        "Falsum/UI/FalsumCanvas");
                    break;
                case FalsumUiStyle.InFalsus:
                    AddStyle(
                        root,
                        ref _inFalsus,
                        "Falsum/UI/FalsumInFalsus");
                    break;
            }
        }

        private static void AddStyle(
            VisualElement root,
            ref StyleSheet sheet,
            string path)
        {
            if (sheet == null)
            {
                sheet = Resources.Load<StyleSheet>(path);
            }
            if (sheet == null)
            {
                Debug.LogError("[Falsum] UI style missing: " + path);
                return;
            }
            if (!root.styleSheets.Contains(sheet))
            {
                root.styleSheets.Add(sheet);
            }
        }

        private static void RemoveStyle(
            VisualElement root,
            StyleSheet sheet)
        {
            if (sheet != null)
            {
                root.styleSheets.Remove(sheet);
            }
        }
    }
}
