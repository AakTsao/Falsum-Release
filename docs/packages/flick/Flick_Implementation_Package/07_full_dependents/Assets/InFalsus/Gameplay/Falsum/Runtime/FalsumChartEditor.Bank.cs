using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Core;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// Editor-side half of the Skyarea Bank: saving a selection, placing a
    /// preset, importing/exporting share codes, and owning the on-disk library.
    ///
    /// <para>PLACEMENT REUSES THE EXISTING PASTE PIPELINE. It temporarily
    /// swaps the user's clipboard for the preset's segments, runs the normal
    /// <c>PasteClipboard()</c> flow (ghost preview, cancel, negative-time
    /// guard, bounds clamping, one undo record) and then restores the
    /// clipboard. Building a second preview state machine would duplicate
    /// twenty-odd <c>EditorMode</c> branches and drift, which is exactly the
    /// failure mode the anchor layer was rebuilt to avoid.</para>
    ///
    /// <para>CLIPBOARD RESTORATION IS IDEMPOTENT and is called from every
    /// exit path (commit, cancel, preview teardown, difficulty switch, editor
    /// shutdown). Missing one leaves the user's Ctrl+V pasting SkyAreas
    /// afterwards.</para>
    /// </summary>
    public sealed partial class FalsumChartEditor
    {
        private FalsumSkyAreaBankDocument _bankDocument;
        private bool _bankInitialized;
        private bool _bankPanelOpen;
        private bool _bankPlacementPending;
        private FalsumSkyAreaPreset _bankPendingPreset;
        private readonly List<ClipboardNote> _bankClipboardBackup =
            new List<ClipboardNote>();

        /// <summary>Currently open bank panel, if any (may be null before wiring).</summary>
        private bool BankPanelOpen
        {
            get { return _bankPanelOpen; }
        }

        private void InitializeSkyAreaBank()
        {
            if (_bankInitialized)
            {
                return;
            }
            _bankInitialized = true;
            FalsumSkyAreaBankStore.WarningSink = delegate(string message)
            {
                Debug.LogWarning(message);
            };
            // The bank lives outside the editor, so it must never be able to
            // stop the editor from starting: Load() degrades to an empty
            // library and quarantines the broken file.
            _bankDocument = FalsumSkyAreaBankStore.Load();
            if (_bankDocument == null)
            {
                _bankDocument = new FalsumSkyAreaBankDocument();
            }
            // Start CLOSED, every session. The sidebar is an overlay on the
            // left of the chart, so restoring the last session's open state
            // meant the editor always launched with a panel covering the
            // notes the user came to edit. The persisted flag is still written
            // when the user toggles (see SetBankPanelOpen) so the field keeps
            // round-tripping through bank.json, but it is deliberately NOT
            // restored into the live UI here.
            _bankPanelOpen = false;
            UpdateBankSelectionSummary();
        }

        /// <summary>Number of selected SkyArea notes (the bank's save condition).</summary>
        private int SelectedSkyAreaCount()
        {
            var count = 0;
            for (var i = 0; i < _selected.Count; i++)
            {
                if (_selected[i] != null &&
                    _selected[i].Kind == NoteKind.SkyArea)
                {
                    count++;
                }
            }
            return count;
        }

        private void UpdateBankSelectionSummary()
        {
            if (_view == null)
            {
                return;
            }
            _view.SetBankSelectionSummary(
                SelectedSkyAreaCount(),
                _selected.Count);
        }

        /// <summary>
        /// Saves the currently selected SkyArea notes as a preset. Never
        /// touches the chart, so it produces no undo record.
        /// </summary>
        internal bool SaveSelectionToBank(string name, IList<string> tags)
        {
            if (!RequireLoadedPackage() || _bankDocument == null)
            {
                return false;
            }
            var skyNotes = new List<ChartNote>();
            for (var i = 0; i < _selected.Count; i++)
            {
                if (_selected[i] != null &&
                    _selected[i].Kind == NoteKind.SkyArea)
                {
                    skyNotes.Add(_selected[i]);
                }
            }
            if (skyNotes.Count == 0)
            {
                ShowHint(L.Get("falsum.bank.hint.needSkyArea"), 1.8f);
                return false;
            }

            var preset = FalsumSkyAreaBankModel.FromSelection(
                skyNotes,
                name);
            if (preset == null)
            {
                ShowHint(L.Get("falsum.bank.error.invalidSegments"), 1.8f);
                return false;
            }
            var sanitized = FalsumSkyAreaBankModel.SanitizeTags(tags);
            for (var i = 0; i < sanitized.Count; i++)
            {
                preset.Tags.Add(sanitized[i]);
            }
            if (string.IsNullOrEmpty(preset.Name))
            {
                preset.Name = L.Get(
                    "falsum.bank.defaultName",
                    ("index", _bankDocument.Presets.Count + 1));
            }

            string errorKey;
            if (!FalsumSkyAreaBankStore.AddOrReplace(
                    _bankDocument,
                    preset,
                    out errorKey))
            {
                ShowHint(L.Get(errorKey), 1.8f);
                return false;
            }
            if (!FalsumSkyAreaBankStore.Save(_bankDocument))
            {
                ShowHint(L.Get("falsum.bank.error.saveFailed"), 2.4f);
                return false;
            }
            _view?.SetBankPresets(_bankDocument);
            ShowHint(
                L.Get(
                    "falsum.bank.hint.saved",
                    ("name", preset.Name),
                    ("segments", preset.Segments.Count)),
                1.8f);
            return true;
        }

        /// <summary>
        /// Places a preset through the ordinary paste preview. The user's own
        /// clipboard is parked in <see cref="_bankClipboardBackup"/> and put
        /// back on either exit path.
        /// </summary>
        internal bool BeginBankPlacement(FalsumSkyAreaPreset preset)
        {
            if (!RequireLoadedPackage() ||
                preset == null ||
                _bankDocument == null)
            {
                return false;
            }
            if (_session.HasPendingEdit || IsAnchorDragActive ||
                IsMiddleEditActive)
            {
                // Never start a modal preview on top of a half-finished edit:
                // the pending transaction would swallow the paste commit.
                ShowHint(L.Get("falsum.hint.finishCurrentEdit"), 1.8f);
                return false;
            }
            if (_mode == EditorMode.PastePreview)
            {
                CancelPastePreview();
            }

            var notes = FalsumSkyAreaBankModel.ToChartNotes(
                preset,
                true);
            if (notes.Count == 0)
            {
                ShowHint(L.Get("falsum.bank.error.invalidSegments"), 1.8f);
                return false;
            }
            if (ContainsNegativeMusicTime(notes))
            {
                ShowNegativeMusicTimeWarning();
                return false;
            }

            CaptureBankClipboard();
            _clipboard.Clear();
            var earliest = double.MaxValue;
            for (var i = 0; i < notes.Count; i++)
            {
                earliest = Math.Min(earliest, notes[i].TimeMs);
            }
            for (var i = 0; i < notes.Count; i++)
            {
                _clipboard.Add(new ClipboardNote
                {
                    Note = notes[i],
                    TimeOffsetMs = notes[i].TimeMs - earliest,
                });
            }
            _bankPlacementPending = true;
            _bankPendingPreset = preset;
            PasteClipboard();
            ShowHint(
                L.Get(
                    "falsum.bank.hint.placing",
                    ("name", preset.Name)),
                2.5f);
            return true;
        }

        private void CaptureBankClipboard()
        {
            _bankClipboardBackup.Clear();
            for (var i = 0; i < _clipboard.Count; i++)
            {
                var entry = _clipboard[i];
                if (entry == null || entry.Note == null)
                {
                    continue;
                }
                _bankClipboardBackup.Add(new ClipboardNote
                {
                    Note = CloneNote(entry.Note),
                    TimeOffsetMs = entry.TimeOffsetMs,
                });
            }
        }

        /// <summary>
        /// Puts the user's clipboard back after a bank placement. Idempotent:
        /// safe to call from every teardown path.
        /// </summary>
        private void RestoreBankClipboard()
        {
            if (!_bankPlacementPending)
            {
                return;
            }
            _bankPlacementPending = false;
            _bankPendingPreset = null;
            _clipboard.Clear();
            for (var i = 0; i < _bankClipboardBackup.Count; i++)
            {
                var entry = _bankClipboardBackup[i];
                _clipboard.Add(new ClipboardNote
                {
                    Note = CloneNote(entry.Note),
                    TimeOffsetMs = entry.TimeOffsetMs,
                });
            }
            _bankClipboardBackup.Clear();
        }

        /// <summary>Marks the placed preset as "recently used" (flushed lazily).</summary>
        private void TouchPlacedPreset()
        {
            if (_bankPendingPreset == null || _bankDocument == null)
            {
                return;
            }
            _bankPendingPreset.LastUsedAtUtc =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            FalsumSkyAreaBankStore.MarkDirty(_bankDocument);
            _view?.SetBankPresets(_bankDocument);
        }

        internal bool DeleteBankPreset(FalsumSkyAreaPreset preset)
        {
            if (_bankDocument == null || preset == null)
            {
                return false;
            }
            if (!FalsumSkyAreaBankStore.Remove(_bankDocument, preset))
            {
                return false;
            }
            FalsumSkyAreaBankStore.Save(_bankDocument);
            _view?.SetBankPresets(_bankDocument);
            ShowHint(L.Get("falsum.bank.hint.deleted"), 1.5f);
            return true;
        }

        internal bool RenameBankPreset(
            FalsumSkyAreaPreset preset,
            string name)
        {
            if (_bankDocument == null || preset == null)
            {
                return false;
            }
            var sanitized = FalsumSkyAreaBankModel.SanitizeText(
                name,
                FalsumSkyAreaBankModel.MaxNameLength);
            if (sanitized.Length == 0)
            {
                ShowHint(L.Get("falsum.bank.error.invalidSegments"), 1.5f);
                return false;
            }
            preset.Name = sanitized;
            preset.UpdatedAtUtc =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            FalsumSkyAreaBankStore.Save(_bankDocument);
            _view?.SetBankPresets(_bankDocument);
            return true;
        }

        internal bool SetBankPresetScales(
            FalsumSkyAreaPreset preset,
            double widthScale,
            double lengthScale)
        {
            if (preset == null || _bankDocument == null)
            {
                return false;
            }
            if (!FalsumSkyAreaBankModel.IsScaleInRange(widthScale) ||
                !FalsumSkyAreaBankModel.IsScaleInRange(lengthScale))
            {
                ShowHint(
                    L.Get(
                        "falsum.bank.error.invalidScale",
                        ("min", FalsumSkyAreaBankModel.MinScale),
                        ("max", FalsumSkyAreaBankModel.MaxScale)),
                    1.8f);
                return false;
            }
            preset.WidthScale = widthScale;
            preset.LengthScale = lengthScale;
            preset.UpdatedAtUtc =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            FalsumSkyAreaBankStore.Save(_bankDocument);
            _view?.SetBankPresets(_bankDocument);
            return true;
        }

        /// <summary>Encodes a preset into a pasteable code; returns empty on failure.</summary>
        internal string BuildShareCode(FalsumSkyAreaPreset preset)
        {
            if (preset == null)
            {
                return string.Empty;
            }
            string code;
            string errorKey;
            if (!FalsumSkyAreaShareCode.TryEncode(preset, out code, out errorKey))
            {
                ShowHint(L.Get(errorKey), 2.4f);
                return string.Empty;
            }
            return code;
        }

        /// <summary>Imports a share code; the caller confirms before it is stored.</summary>
        internal FalsumSkyAreaPreset DecodeShareCode(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }
            FalsumSkyAreaPreset preset;
            string errorKey;
            if (!FalsumSkyAreaShareCode.TryDecode(code, out preset, out errorKey))
            {
                ShowHint(L.Get(errorKey), 2.4f);
                return null;
            }
            return preset;
        }

        internal bool StoreImportedPreset(FalsumSkyAreaPreset preset)
        {
            if (_bankDocument == null || preset == null)
            {
                return false;
            }
            string errorKey;
            if (!FalsumSkyAreaBankStore.AddOrReplace(
                    _bankDocument,
                    preset,
                    out errorKey))
            {
                ShowHint(
                    L.Get(
                        errorKey,
                        ("limit", FalsumSkyAreaBankModel.MaxPresetsPerBank)),
                    2.4f);
                return false;
            }
            FalsumSkyAreaBankStore.Save(_bankDocument);
            _view?.SetBankPresets(_bankDocument);
            ShowHint(
                L.Get(
                    "falsum.bank.hint.imported",
                    ("name", preset.Name),
                    ("segments", preset.Segments.Count)),
                1.8f);
            return true;
        }

        internal bool CopyShareCodeToClipboard(FalsumSkyAreaPreset preset)
        {
            var code = BuildShareCode(preset);
            if (code.Length == 0)
            {
                return false;
            }
            // Show it first: the code is also a read-only text field, so a
            // clipboard failure still lets the user select and copy by hand.
            _view?.ShowBankShareCode(preset, code);
            if (!FalsumTextClipboard.TrySet(code))
            {
                ShowHint(L.Get("falsum.bank.error.copyFailed"), 1.8f);
                return false;
            }
            ShowHint(
                L.Get(
                    "falsum.bank.hint.codeCopied",
                    ("length", code.Length)),
                1.8f);
            return true;
        }

        internal string ReadShareCodeFromClipboard()
        {
            string code;
            return FalsumTextClipboard.TryGet(out code)
                ? code
                : string.Empty;
        }

        /// <summary>Exports a preset to a .fsky file holding the share code.</summary>
        internal void ExportPresetToFile(FalsumSkyAreaPreset preset)
        {
            var code = BuildShareCode(preset);
            if (code.Length == 0)
            {
                return;
            }
            var path = FalsumFileDialog.SaveSkyAreaBank(
                preset.Name);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                File.WriteAllText(
                    FalsumFileDialog.EnsureSkyAreaBankExtension(path),
                    code,
                    new UTF8Encoding(false));
                ShowHint(L.Get("falsum.bank.hint.fileExported"), 1.8f);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] SkyArea Bank export failed: " +
                    exception.Message);
                ShowHint(L.Get("falsum.bank.error.fileWrite"), 1.8f);
            }
        }

        /// <summary>
        /// Opens the folder that holds <c>bank.json</c> so the user can drop
        /// <c>.fsky</c> share files in or copy them out. This replaced the old
        /// "import from file" button: importing one file was a dead end when
        /// the folder itself is the unit of exchange, and revealing it also
        /// answers "where did my presets go?".
        ///
        /// <para>The path is <see cref="FalsumSkyAreaBankStore.DirectoryPath"/>,
        /// the same property Load/Save use -- never a second hard-coded path,
        /// so a change to where the bank lives moves the button with it.</para>
        ///
        /// <para>Opening goes through <see cref="FalsumPlatform.OpenDirectory"/>,
        /// which owns the editor/standalone split (shell-open on Windows,
        /// <c>Application.OpenURL</c> elsewhere) and creates the directory when
        /// the user has never saved a preset. That call can throw (a locked or
        /// unwritable persistentDataPath), and an exception escaping a button
        /// callback tears down the rest of the UI event dispatch, hence the
        /// guard -- same shape as <c>OpenBackupFolder</c>.</para>
        /// </summary>
        internal void OpenBankFolder()
        {
            try
            {
                FalsumPlatform.OpenDirectory(
                    FalsumSkyAreaBankStore.DirectoryPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] SkyArea Bank folder could not be opened: " +
                    exception.Message);
                ShowHint(
                    L.Get(
                        "falsum.bank.error.openFolderFailed",
                        ("error", exception.Message)),
                    2.4f);
            }
        }

        internal void ToggleBankPanel()
        {
            SetBankPanelOpen(!_bankPanelOpen);
        }

        internal void SetBankPanelOpen(bool open)
        {
            if (_view == null)
            {
                return;
            }
            _bankPanelOpen = open;
            if (open)
            {
                InitializeSkyAreaBank();
                _view.SetBankPresets(_bankDocument);
                UpdateBankSelectionSummary();
            }
            _view.SetBankPanelOpen(open);
            if (_bankDocument != null)
            {
                _bankDocument.PanelOpened = open;
                FalsumSkyAreaBankStore.MarkDirty(_bankDocument);
            }
        }

        /// <summary>Flushes lazy bank bookkeeping; called on shutdown/switch.</summary>
        internal void FlushSkyAreaBank()
        {
            RestoreBankClipboard();
            FalsumSkyAreaBankStore.Flush();
        }
    }
}
