using System;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// Thin wrapper over the OS clipboard.
    ///
    /// <para>WHY A WRAPPER: nothing in the repository used
    /// <c>GUIUtility.systemCopyBuffer</c> before, and it can throw on
    /// platforms/headless runs where there is no window. A share code is a
    /// convenience path, so a failure must degrade to "please select and copy
    /// manually" instead of taking the editor down.</para>
    /// </summary>
    public static class FalsumTextClipboard
    {
        /// <summary>Replaces the clipboard. Returns false when empty or unavailable.</summary>
        public static bool TrySet(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            try
            {
                GUIUtility.systemCopyBuffer = text;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Clipboard write failed: " +
                    exception.Message);
                return false;
            }
        }

        /// <summary>Reads the clipboard; returns false when empty or unavailable.</summary>
        public static bool TryGet(out string text)
        {
            text = string.Empty;
            try
            {
                var raw = GUIUtility.systemCopyBuffer;
                if (string.IsNullOrEmpty(raw))
                {
                    return false;
                }
                text = raw;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Clipboard read failed: " +
                    exception.Message);
                return false;
            }
        }
    }
}
