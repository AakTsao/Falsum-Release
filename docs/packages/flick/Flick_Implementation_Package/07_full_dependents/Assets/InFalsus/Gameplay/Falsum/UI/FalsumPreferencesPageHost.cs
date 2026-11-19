using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// Adapts the preferences dialog shell to <see cref="IFalsumPreferencesPageHost"/>.
    ///
    /// Kept as a separate object rather than having the view implement the
    /// interface directly: the view exposes a lot of public surface for the
    /// editor and the debug API, and pages should only see this narrow slice.
    /// </summary>
    internal sealed class FalsumPreferencesPageHost :
        IFalsumPreferencesPageHost
    {
        private readonly FalsumPreferencesView _view;

        public FalsumPreferencesPageHost(FalsumPreferencesView view)
        {
            _view = view;
        }

        public FalsumKeyCaptureController KeyCapture
        {
            get { return _view.KeyCaptureForPages; }
        }

        public FalsumPackage Package
        {
            get { return _view.PackageForPages; }
        }

        public void ShowInfo(string message)
        {
            _view.ShowInfo(message);
        }

        public void ShowError(string message)
        {
            _view.ShowError(message);
        }

        public void ClearStatus()
        {
            _view.ClearStatus();
        }

        public void RebuildTab(FalsumPreferencesTab tab)
        {
            _view.RebuildTabForPages(tab);
        }

        public void ResetAllDefaults(FalsumPreferencesTab tab)
        {
            _view.ResetAllDefaultsForPages(tab);
        }

        public void PreviewUiStyle(FalsumUiStyle style)
        {
            _view.PreviewUiStyleForPages(style);
        }

        public void PreviewMobileLayout(bool mobile)
        {
            _view.PreviewMobileLayoutForPages(mobile);
        }

        public void PreviewMobileScale(float scale)
        {
            _view.PreviewMobileScaleForPages(scale);
        }

        public void PreviewVisual()
        {
            _view.PreviewVisualForPages();
        }

        public void OpenCacheFolder()
        {
            _view.RaiseOpenCacheFolderForPages();
        }

        public void ClearCache()
        {
            _view.RaiseClearCacheForPages();
        }

        public void OpenBackupFolder()
        {
            _view.RaiseOpenBackupFolderForPages();
        }

        public void RestoreBackup(FalsumBackupEntry entry)
        {
            _view.RaiseRestoreBackupForPages(entry);
        }

        public FalsumChartMetadataDraft MetadataDraft
        {
            get { return _view.MetadataDraftForPages; }
        }

        public Texture2D SharedCoverTexture
        {
            get { return _view.SharedCoverTextureForPages; }
        }

        public void BrowseCover()
        {
            _view.RaiseBrowseCoverForPages();
        }

        public void BrowseAudio()
        {
            _view.RaiseBrowseAudioForPages();
        }

        public void OpenBackgroundPreview()
        {
            _view.OpenBackgroundPreviewForPages();
        }

        public void SwitchDifficulty(int difficulty)
        {
            _view.RaiseSwitchDifficultyForPages(difficulty);
        }
    }
}
