using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// What a preferences page may call on the dialog shell.
    ///
    /// Deliberately tiny: a page never receives the shell's visual tree, its
    /// overlay or its scheduler, so a page cannot reach across another page.
    /// Everything here is a dialog-level service a page legitimately needs
    /// (status reporting, rebuilding itself after it changes the draft, the
    /// dialog-wide key-capture state, and the chart package the dialog was
    /// opened for).
    /// </summary>
    internal interface IFalsumPreferencesPageHost
    {
        FalsumKeyCaptureController KeyCapture { get; }

        /// <summary>
        /// The chart package the dialog was opened for, or null when no
        /// package is loaded. Pages that show chart-scoped data (backups,
        /// chart info) read it from here rather than from the shell.
        /// </summary>
        FalsumPackage Package { get; }

        void ShowInfo(string message);

        void ShowError(string message);

        void ClearStatus();

        /// <summary>
        /// Re-shows the given tab. Pages call this after rewriting their own
        /// draft state (restore defaults, difficulty switch) because the
        /// visible controls are rebuilt from the draft.
        /// </summary>
        void RebuildTab(FalsumPreferencesTab tab);

        /// <summary>Replaces the whole draft with defaults, then re-shows this tab.</summary>
        void ResetAllDefaults(FalsumPreferencesTab tab);

        /// <summary>Applies a UI-style preview to the live editor.</summary>
        void PreviewUiStyle(FalsumUiStyle style);

        /// <summary>
        /// Switches the live editor between the desktop and the Android
        /// phone layout. Developer switch only.
        /// </summary>
        void PreviewMobileLayout(bool mobile);

        /// <summary>Applies a phone control-size preview to the live editor.</summary>
        void PreviewMobileScale(float scale);

        /// <summary>
        /// Applies the draft's scene-opacity group to the live editor so
        /// the display page's sliders are visible while they are dragged. The
        /// shell restores the committed group when the dialog is cancelled.
        /// </summary>
        void PreviewVisual();

        void OpenCacheFolder();

        void ClearCache();

        void OpenBackupFolder();

        /// <summary>Asks the editor to restore the given backup.</summary>
        void RestoreBackup(FalsumBackupEntry entry);

        /// <summary>The chart metadata draft being edited, or null.</summary>
        FalsumChartMetadataDraft MetadataDraft { get; }

        /// <summary>
        /// Shared cover texture from the song-info card, used as a stand-in
        /// until the draft has its own converted cover. Owned by the shell.
        /// </summary>
        Texture2D SharedCoverTexture { get; }

        /// <summary>Requests a cover image from the user.</summary>
        void BrowseCover();

        /// <summary>Requests an audio file from the user.</summary>
        void BrowseAudio();

        /// <summary>Opens the background picker sub-modal.</summary>
        void OpenBackgroundPreview();

        /// <summary>Switches the chart to the given difficulty.</summary>
        void SwitchDifficulty(int difficulty);
    }
}
