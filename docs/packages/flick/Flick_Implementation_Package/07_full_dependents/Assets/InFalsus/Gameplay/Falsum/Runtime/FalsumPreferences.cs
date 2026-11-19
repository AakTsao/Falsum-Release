using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Falsum.Core;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public enum FalsumAntiAliasingMode
    {
        Msaa2x = 0,
        Msaa4x = 1,
        Msaa8x = 2,
        Smaa = 3,
        Taa = 4,
    }

    [Flags]
    public enum FalsumModifierKeys
    {
        None = 0,
        Ctrl = 1 << 0,
        Alt = 1 << 1,
        Shift = 1 << 2,
    }

    public enum FalsumInputAction
    {
        PlayPause = 0,
        TemporaryAudition = 1,
        Save = 2,
        Undo = 3,
        Redo = 4,
        Copy = 5,
        Cut = 6,
        Paste = 7,
        DeleteSelection = 8,
        MiddleEditBoundary = 9,
        MiddleEditStartEnd = 10,
        MiddleEditPosition = 11,
        Mirror = 12,
        ToggleFullscreen = 13,
        PointPlaceTap = 14,
        PointPlaceHold = 15,
        PointPlaceFlick = 16,
        PointPlaceSkyArea = 17,
        ToggleGrid = 18,
        DecreaseFlowSpeed = 19,
        IncreaseFlowSpeed = 20,
        ToggleFlickColor = 21,
        SegmentSelect = 22,
        MirrorFrontBack = 23,
        ToggleTrackSpeedEffects = 24,
        // Appended, never inserted: the preference file persists the NUMERIC
        // value of each action, so renumbering would silently rebind keys
        // for every existing profile.
        ToggleSkyAreaBank = 25,
    }

    [Serializable]
    public sealed class FalsumKeyBinding
    {
        public FalsumInputAction Action;
        public FalsumModifierKeys Modifiers;
        public KeyCode Key;

        public FalsumKeyBinding Clone()
        {
            return new FalsumKeyBinding
            {
                Action = Action,
                Modifiers = Modifiers,
                Key = Key,
            };
        }
    }

    [Serializable]
    public sealed class FalsumDisplayPreferences
    {
        public int ResolutionWidth = 1600;
        public int ResolutionHeight = 900;
        public int ResolutionRefreshRate = 60;
        public bool BorderlessFullscreen;
        public bool AntiAliasingEnabled = true;
        public FalsumAntiAliasingMode AntiAliasingMode =
            FalsumAntiAliasingMode.Msaa4x;
    }

    [Serializable]
    public sealed class FalsumEditingPreferences
    {
        public bool DirectDragUnselectedNotes = true;
        public bool MiddleEditAffectsAllSelected = true;
        public bool SkyAreaGroupTimeSnap = true;
        public bool DisableOutOfBoundsClamping;
    }

    [Serializable]
    public sealed class FalsumBackupPreferences
    {
        public bool Enabled = true;
        public int IntervalSeconds = 180;
        public int RetentionCount = 20;
    }

    [Serializable]
    public sealed class FalsumAudioPreferences
    {
        public float MusicVolume = 1.0f;
        public float SkyAreaVolume = 1.0f;
        public float TapVolume = 1.0f;
        public float HoldVolume = 1.0f;
        public float FlickVolume = 1.0f;
    }

    /// <summary>
    /// Scene-surface appearance. Kept out of <see cref="FalsumDisplayPreferences"/>
    /// on purpose: the display group drives window mode and anti-aliasing, and
    /// <c>FalsumChartEditor.CommitPreferences</c> treats any change in it as a
    /// resolution change that must go through the ten-second keep/revert
    /// confirmation dialog. Adjusting an opacity must never trigger that.
    ///
    /// Defaults reproduce the current runtime appearance rather than the
    /// material authoring values: SkyArea's fill renders at
    /// <code>_Color.a = 0.5</code>, and the two track alpha channels are 1.0
    /// because <c>GameplayTrackOpacityController.Initialize</c> writes
    /// <c>SetTrackOpacity(1.0)</c> over the materials' authored 0.8 / 0.35.
    /// Keeping the defaults at the running values means resetting to default
    /// never changes the picture, and any slider movement is the user's
    /// explicit choice.
    ///
    /// The side walls still look translucent at 100% because their albedo
    /// texture (<code>notes_track_matid_trackwall_AlbedoTransparency.png</code>)
    /// already carries per-texel alpha — measured mean 0.985, minimum 0.86 —
    /// and the shader additionally fades them toward the track end. The
    /// slider multiplies on top of that texture alpha; it is not the source
    /// of the existing translucency.
    /// </summary>
    [Serializable]
    public sealed class FalsumVisualPreferences
    {
        public const float DefaultSkyAreaOpacity = 0.5f;
        public const float DefaultSkyAreaGlow = 1.0f;
        public const float DefaultSideLaneOpacity = 0.35f;
        public const float DefaultFloorLaneOpacity = 0.8f;

        /// <summary>Absolute alpha of the SkyArea fill (material <code>_Color.a</code>).</summary>
        public float SkyAreaOpacity = DefaultSkyAreaOpacity;

        /// <summary>Multiplier over the SkyArea edge colours' authored alpha.</summary>
        public float SkyAreaGlow = DefaultSkyAreaGlow;

        /// <summary>Alpha scale written to the side walls' <code>_WallAlphaScale</code>.</summary>
        public float SideLaneOpacity = DefaultSideLaneOpacity;

        /// <summary>Alpha scale written to the floor lanes' <code>_AlphaScale</code>.</summary>
        public float FloorLaneOpacity = DefaultFloorLaneOpacity;
    }

    [Serializable]
    public enum FalsumUiStyle
    {
        Classic,
        Studio,
        Canvas,
        InFalsus,
    }

    /// <summary>
    /// Product gate for the style picker. Enabled so the original Classic
    /// interface remains accessible alongside the In Falsus default.
    /// </summary>
    public static class FalsumUiStyleSwitcher
    {
        public static readonly bool Available = true;
    }

    /// <summary>
    /// Temporary product gate for measure line number badges. While it is
    /// false the editor never draws them and the View menu and Advanced page
    /// hide the toggles; flipping it back restores the whole feature.
    /// </summary>
    public static class FalsumMeasureNumberFeature
    {
        public static readonly bool Available = false;
    }

    /// <summary>
    /// Temporary product gate for the waveform spectrum toggle. While it is
    /// false the transport bar hides the button and no spectrum is rendered.
    /// </summary>
    public static class FalsumSpectrumFeature
    {
        public static readonly bool Available = false;
    }

    [Serializable]
    public sealed class FalsumGridPreferences
    {
        public string LineColors = FalsumBeatGrid.DefaultColorSequence;
        public float LineWidthScale = 1.0f;
        public int TimeSubdivision = 4;

        /// <summary>
        /// Ordered set of grid denominators the <c>-</c> / <c>=</c> keys
        /// step through. Text rather than <c>List&lt;int&gt;</c> so a
        /// malformed hand-edited value can be repaired by
        /// <see cref="FalsumPreferences.Normalize"/> instead of silently
        /// deserializing into an empty list.
        /// </summary>
        public string DenominatorSequence =
            FalsumBeatGrid.DefaultDenominatorSequence;
    }

    [Serializable]
    public sealed class FalsumExperimentalPreferences
    {
        public FalsumUiStyle UIStyle = FalsumUiStyle.InFalsus;
        // Older builds forced Classic while the picker was hidden. An
        // explicit selection must survive that one-time default migration.
        public bool UiStyleChosen = false;
        public bool ShowMeasureNumbers = false;

        /// <summary>
        /// Developer switch: render the Android landscape phone layout on
        /// this machine, whatever the platform.
        ///
        /// WHY IT EXISTS: the phone layout can otherwise only be seen on a
        /// device, and this project had no Android build chain at the time
        /// (2026-11-06-falsum-android-feasibility-assessment §1.1 P1/P2).
        /// It is persisted so the choice survives a restart while the
        /// layout is being iterated on, but it is NOT part of the shipped
        /// surface: it lives behind <see cref="FalsumDeveloperSwitches"/>.
        /// </summary>
        public bool ForceMobileLayout = false;

        /// <summary>
        /// Phone control-size multiplier. The user reported the controls as
        /// oversized; this is the knob, expressed as a multiplier rather than
        /// a pixel size so it survives a density change.
        ///
        /// It scales the CONTROLS only. Column widths stay in real dp, so
        /// shrinking the buttons widens the playfield instead of leaving a
        /// gap -- which is the actual complaint.
        /// </summary>
        public float MobileUiScale = FalsumMobileScaleBounds.Default;
    }

    /// <summary>
    /// Gates for developer-only controls. Kept separate from the feature
    /// gates (<see cref="FalsumUiStyleSwitcher"/> and friends) because these
    /// exist for US to look at the build, not for users to configure it.
    /// </summary>
    public static class FalsumDeveloperSwitches
    {
        /// <summary>
        /// Whether the preferences dialog shows the developer section.
        /// True in the editor and in development builds, false in a release
        /// player, so nobody ships a button that makes their editor look
        /// like a phone for no reason.
        /// </summary>
        public static bool Available
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }
    }

    /// <summary>
    /// Transitional forwarding facade introduced in preferences v6 so the
    /// data model could be regrouped (grid styling to <see cref="FalsumGridPreferences"/>,
    /// product-gated fields to <see cref="FalsumExperimentalPreferences"/>,
    /// out-of-bounds clamping to <see cref="FalsumEditingPreferences"/>)
    /// without touching every consumer at once. It holds no data of its own:
    /// every member forwards to the owning <see cref="FalsumPreferences"/>.
    /// Remove in P4 together with the <see cref="FalsumPreferences.Advanced"/>
    /// property.
    /// </summary>
    /// <summary>
    /// Bounds and repairs the phone control scale. Kept next to the type
    /// rather than in the UI so a hand-edited preferences file cannot put
    /// the rails into a state where they are narrower than their own
    /// buttons.
    /// </summary>
    public static class FalsumMobileScaleBounds
    {
        public const float Min = 0.6f;
        public const float Max = 1.4f;
        public const float Default = Min;
        public const float PreviousDefault = 0.85f;

        public static float Clamp(float value)
        {
            if (float.IsNaN(value) || value <= 0.0f)
            {
                return Default;
            }
            if (value < Min)
            {
                return Min;
            }
            if (value > Max)
            {
                return Max;
            }
            return value;
        }
    }

    public sealed class FalsumAdvancedPreferences
    {
        private readonly FalsumPreferences _owner;

        internal FalsumAdvancedPreferences(FalsumPreferences owner)
        {
            _owner = owner;
        }

        public FalsumUiStyle UIStyle
        {
            get
            {
                return _owner.Experimental != null
                    ? _owner.Experimental.UIStyle
                    : FalsumUiStyle.Classic;
            }
            set
            {
                if (_owner.Experimental != null)
                {
                    _owner.Experimental.UIStyle = value;
                    _owner.Experimental.UiStyleChosen = true;
                }
            }
        }

        public bool ShowMeasureNumbers
        {
            get
            {
                return _owner.Experimental != null &&
                       _owner.Experimental.ShowMeasureNumbers;
            }
            set
            {
                if (_owner.Experimental != null)
                {
                    _owner.Experimental.ShowMeasureNumbers = value;
                }
            }
        }

        public string GridLineColors
        {
            get
            {
                return _owner.Grid != null
                    ? _owner.Grid.LineColors
                    : FalsumBeatGrid.DefaultColorSequence;
            }
            set
            {
                if (_owner.Grid != null)
                {
                    _owner.Grid.LineColors = value;
                }
            }
        }

        public float GridLineWidthScale
        {
            get
            {
                return _owner.Grid != null
                    ? _owner.Grid.LineWidthScale
                    : 1.0f;
            }
            set
            {
                if (_owner.Grid != null)
                {
                    _owner.Grid.LineWidthScale = value;
                }
            }
        }

        public bool DisableOutOfBoundsClamping
        {
            get
            {
                return _owner.Editing != null &&
                       _owner.Editing.DisableOutOfBoundsClamping;
            }
            set
            {
                if (_owner.Editing != null)
                {
                    _owner.Editing.DisableOutOfBoundsClamping = value;
                }
            }
        }

        /// <summary>
        /// Removed from the v6 schema (session state, never consumed). Kept
        /// only so pre-v6 call sites still compile; always reports false.
        /// </summary>
        public bool RestoreLastPackage
        {
            get { return false; }
            set { }
        }

        /// <summary>
        /// Removed from the v6 schema. Kept only so pre-v6 call sites still
        /// compile; always reports empty.
        /// </summary>
        public string LastPackagePath
        {
            get { return string.Empty; }
            set { }
        }
    }

    [Serializable]
    public sealed class FalsumPreferences
    {
        /// <summary>
        /// v7 added <see cref="FalsumVisualPreferences"/> (SkyArea opacity /
        /// glow and track-surface opacity).
        ///
        /// v8 added <see cref="FalsumGridPreferences.DenominatorSequence"/>
        /// and, more importantly, taught the migration to read the whole
        /// <c>Grid</c> group: v7 already wrote it, but the legacy DTO had no
        /// member, so every reload reset the group. Both changes are
        /// additive; the version bump exists so the store rewrites the file
        /// with the repaired sequence instead of leaving a stale one behind.
        /// v9 widens the mobile UI scale range and moves its old default to
        /// the new minimum, while preserving custom values.
        /// v10 persists the time subdivision shared by the settings page,
        /// desktop field and mobile grid hold gesture.
        /// </summary>
        public const int CurrentVersion = 10;
        private const int DefaultWindowWidth = 1366;
        private const int DefaultWindowHeight = 768;

        public int Version = CurrentVersion;
        public FalsumDisplayPreferences Display =
            new FalsumDisplayPreferences();
        public List<FalsumKeyBinding> Bindings =
            new List<FalsumKeyBinding>();
        public FalsumEditingPreferences Editing =
            new FalsumEditingPreferences();
        public FalsumBackupPreferences Backup =
            new FalsumBackupPreferences();
        public FalsumAudioPreferences Audio =
            new FalsumAudioPreferences();
        public FalsumVisualPreferences Visual =
            new FalsumVisualPreferences();
        public FalsumGridPreferences Grid =
            new FalsumGridPreferences();
        public FalsumExperimentalPreferences Experimental =
            new FalsumExperimentalPreferences();

        private FalsumAdvancedPreferences _advancedFacade;

        /// <summary>
        /// Transitional forwarding facade (v6 only). Prefer
        /// <see cref="Editing"/> / <see cref="Grid"/> /
        /// <see cref="Experimental"/> directly. The setter is intentionally
        /// a no-op: legacy code that reassigns the whole group must not
        /// discard or replace the regrouped data. Removed in P4.
        /// </summary>
        public FalsumAdvancedPreferences Advanced
        {
            get
            {
                if (_advancedFacade == null)
                {
                    _advancedFacade = new FalsumAdvancedPreferences(this);
                }
                return _advancedFacade;
            }
            set
            {
            }
        }

        public static FalsumPreferences CreateDefault()
        {
            var preferences = new FalsumPreferences();
            var resolution = Screen.currentResolution;
            preferences.Display.ResolutionWidth = DefaultWindowWidth;
            preferences.Display.ResolutionHeight = DefaultWindowHeight;
            if (resolution.refreshRateRatio.value > 0.0)
            {
                preferences.Display.ResolutionRefreshRate =
                    Mathf.RoundToInt((float)resolution.refreshRateRatio.value);
            }
            preferences.Bindings = CreateDefaultBindings();
            preferences.Normalize();
            return preferences;
        }

        public FalsumPreferences Clone()
        {
            return JsonUtility.FromJson<FalsumPreferences>(
                JsonUtility.ToJson(this));
        }

        public void Normalize()
        {
            var previousVersion = Version;
            Version = CurrentVersion;
            if (Display == null)
            {
                Display = new FalsumDisplayPreferences();
            }
            if (Editing == null)
            {
                Editing = new FalsumEditingPreferences();
            }
            if (Experimental == null)
            {
                Experimental = new FalsumExperimentalPreferences();
            }
            // Repair the phone control scale in one place. A hand-edited
            // file must not be able to put the rails into a state where they
            // are narrower than their own buttons.
            Experimental.MobileUiScale = FalsumMobileScaleBounds.Clamp(
                Experimental.MobileUiScale);
            if (previousVersion < 9 &&
                Mathf.Abs(
                    Experimental.MobileUiScale -
                    FalsumMobileScaleBounds.PreviousDefault) < 0.0001f)
            {
                Experimental.MobileUiScale =
                    FalsumMobileScaleBounds.Default;
            }
            if (previousVersion < 3)
            {
                Editing.SkyAreaGroupTimeSnap = true;
            }
            if (Backup == null)
            {
                Backup = new FalsumBackupPreferences();
            }
            if (Audio == null)
            {
                Audio = new FalsumAudioPreferences();
            }
            if (Visual == null)
            {
                Visual = new FalsumVisualPreferences();
            }
            if (Grid == null)
            {
                Grid = new FalsumGridPreferences();
            }
            // Older profiles have no time subdivision; zero restores the
            // previous editor default rather than creating an invalid grid.
            if (Grid.TimeSubdivision < 1) Grid.TimeSubdivision = 4;
            if (Experimental == null)
            {
                Experimental = new FalsumExperimentalPreferences();
            }

            List<Color> gridColors;
            string gridColorError;
            if (!FalsumBeatGrid.TryParseColorSequence(
                    Grid.LineColors,
                    out gridColors,
                    out gridColorError))
            {
                gridColors = null;
            }
            Grid.LineColors =
                FalsumBeatGrid.FormatColorSequence(gridColors);
            Grid.LineWidthScale = Mathf.Clamp(
                Grid.LineWidthScale,
                0.25f,
                3.0f);
            List<int> gridDenominators;
            string gridDenominatorError;
            if (FalsumBeatGrid.TryParseDenominatorSequence(
                    Grid.DenominatorSequence,
                    out gridDenominators,
                    out gridDenominatorError))
            {
                Grid.DenominatorSequence =
                    FalsumBeatGrid.FormatDenominatorSequence(
                        gridDenominators);
            }
            else
            {
                // Same repair shape as LineColors: an invalid authored
                // sequence falls back to the default instead of leaving the
                // user with a sequence the -/= keys cannot walk.
                Grid.DenominatorSequence =
                    FalsumBeatGrid.DefaultDenominatorSequence;
            }
            if (previousVersion < 5)
            {
                // Measure line numbers are gated off for now; a missing field
                // deserializes to false, which matches the hidden default.
                Experimental.ShowMeasureNumbers = false;
            }
            ApplyProductGates();

            var desktop = Screen.currentResolution;
            if (Display.ResolutionWidth < 1280 ||
                Display.ResolutionHeight < 720)
            {
                Display.ResolutionWidth = desktop.width > 0
                    ? desktop.width
                    : 1600;
                Display.ResolutionHeight = desktop.height > 0
                    ? desktop.height
                    : 900;
            }
            if (Display.ResolutionRefreshRate <= 0)
            {
                Display.ResolutionRefreshRate = desktop.width > 0
                    ? Mathf.RoundToInt(
                        (float)desktop.refreshRateRatio.value)
                    : 60;
            }
            if (Display.AntiAliasingMode <
                    FalsumAntiAliasingMode.Msaa2x ||
                Display.AntiAliasingMode >
                    FalsumAntiAliasingMode.Taa)
            {
                Display.AntiAliasingMode =
                    FalsumAntiAliasingMode.Msaa4x;
            }

            Backup.IntervalSeconds = Mathf.Clamp(
                Backup.IntervalSeconds,
                60,
                600);
            Backup.RetentionCount = Mathf.Clamp(
                Backup.RetentionCount,
                1,
                100);
            Audio.MusicVolume = Mathf.Clamp01(Audio.MusicVolume);
            Audio.SkyAreaVolume = Mathf.Clamp01(Audio.SkyAreaVolume);
            Audio.TapVolume = Mathf.Clamp01(Audio.TapVolume);
            Audio.HoldVolume = Mathf.Clamp01(Audio.HoldVolume);
            Audio.FlickVolume = Mathf.Clamp01(Audio.FlickVolume);
            NormalizeVisual();

            NormalizeBindings(previousVersion);
        }

        /// <summary>
        /// Clamps the scene-surface opacity group into 0..1 and repairs
        /// non-finite values. Values are deliberately not "repaired upward":
        /// 0 is a legitimate setting (hide the surface entirely), so only NaN
        /// and infinities fall back to the group's default.
        /// </summary>
        private void NormalizeVisual()
        {
            Visual.SkyAreaOpacity = ClampOpacityOr(
                Visual.SkyAreaOpacity,
                FalsumVisualPreferences.DefaultSkyAreaOpacity);
            Visual.SkyAreaGlow = ClampOpacityOr(
                Visual.SkyAreaGlow,
                FalsumVisualPreferences.DefaultSkyAreaGlow);
            Visual.SideLaneOpacity = ClampOpacityOr(
                Visual.SideLaneOpacity,
                FalsumVisualPreferences.DefaultSideLaneOpacity);
            Visual.FloorLaneOpacity = ClampOpacityOr(
                Visual.FloorLaneOpacity,
                FalsumVisualPreferences.DefaultFloorLaneOpacity);
        }

        private static float ClampOpacityOr(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }
            return Mathf.Clamp(value, 0.0f, 1.0f);
        }

        /// <summary>
        /// Applies the temporary product gates. These are deliberately
        /// separate from Normalize's null/clamp work: while a feature is
        /// hidden no profile may enable it, and the UI style enum must stay
        /// valid so restoring the gate cannot surface a bad value.
        /// </summary>
        private void ApplyProductGates()
        {
            if (!FalsumUiStyleSwitcher.Available ||
                !Enum.IsDefined(
                    typeof(FalsumUiStyle),
                    Experimental.UIStyle))
            {
                // While the switcher is hidden every profile renders classic,
                // including profiles saved by an earlier build.
                Experimental.UIStyle = FalsumUiStyle.Classic;
                Experimental.UiStyleChosen = true;
            }
            else if (!Experimental.UiStyleChosen &&
                     Experimental.UIStyle == FalsumUiStyle.Classic)
            {
                Experimental.UIStyle = FalsumUiStyle.InFalsus;
            }
            if (!FalsumMeasureNumberFeature.Available)
            {
                // While the feature is hidden no profile can turn it on.
                Experimental.ShowMeasureNumbers = false;
            }
        }

        public FalsumKeyBinding FindBinding(FalsumInputAction action)
        {
            if (Bindings == null)
            {
                return null;
            }
            for (var i = 0; i < Bindings.Count; i++)
            {
                if (Bindings[i] != null &&
                    Bindings[i].Action == action)
                {
                    return Bindings[i];
                }
            }
            return null;
        }

        public void SetBinding(
            FalsumInputAction action,
            FalsumModifierKeys modifiers,
            KeyCode key)
        {
            var binding = FindBinding(action);
            if (binding == null)
            {
                binding = new FalsumKeyBinding
                {
                    Action = action,
                };
                Bindings.Add(binding);
            }
            binding.Modifiers = modifiers;
            binding.Key = key;
        }

        public bool HasBindingConflict(
            FalsumInputAction action,
            FalsumModifierKeys modifiers,
            KeyCode key,
            out FalsumInputAction conflict)
        {
            conflict = action;
            if (Bindings == null)
            {
                return false;
            }
            for (var i = 0; i < Bindings.Count; i++)
            {
                var binding = Bindings[i];
                if (binding == null ||
                    binding.Action == action ||
                    binding.Key == KeyCode.None)
                {
                    continue;
                }
                if (binding.Key == key &&
                    binding.Modifiers == modifiers)
                {
                    conflict = binding.Action;
                    return true;
                }
            }
            return false;
        }

        public bool HasBindingConflicts()
        {
            for (var i = 0; i < Bindings.Count; i++)
            {
                var current = Bindings[i];
                if (current == null ||
                    current.Key == KeyCode.None)
                {
                    continue;
                }
                for (var j = i + 1; j < Bindings.Count; j++)
                {
                    var other = Bindings[j];
                    if (other == null ||
                        other.Key == KeyCode.None)
                    {
                        continue;
                    }
                    if (current.Key == other.Key &&
                        current.Modifiers == other.Modifiers)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void NormalizeBindings(int previousVersion)
        {
            if (Bindings == null)
            {
                Bindings = new List<FalsumKeyBinding>();
            }
            for (var i = Bindings.Count - 1; i >= 0; i--)
            {
                if (Bindings[i] == null)
                {
                    Bindings.RemoveAt(i);
                }
            }
            if (previousVersion < 2)
            {
                MigrateDefaultBinding(
                    FalsumInputAction.MiddleEditBoundary,
                    KeyCode.F8,
                    KeyCode.F1);
                MigrateDefaultBinding(
                    FalsumInputAction.MiddleEditStartEnd,
                    KeyCode.F9,
                    KeyCode.F2);
                MigrateDefaultBinding(
                    FalsumInputAction.MiddleEditPosition,
                    KeyCode.F10,
                    KeyCode.F3);
            }

            var defaults = CreateDefaultBindings();
            var seen = new HashSet<FalsumInputAction>();
            for (var i = 0; i < Bindings.Count; i++)
            {
                var binding = Bindings[i];
                if (!seen.Add(binding.Action))
                {
                    binding.Key = KeyCode.None;
                    binding.Modifiers = FalsumModifierKeys.None;
                }
            }
            for (var i = 0; i < defaults.Count; i++)
            {
                var fallback = defaults[i];
                var binding = FindBinding(fallback.Action);
                if (binding == null)
                {
                    Bindings.Add(fallback.Clone());
                }
            }
        }

        private void MigrateDefaultBinding(
            FalsumInputAction action,
            KeyCode oldKey,
            KeyCode newKey)
        {
            var binding = FindBinding(action);
            if (binding != null &&
                binding.Modifiers == FalsumModifierKeys.None &&
                binding.Key == oldKey)
            {
                binding.Key = newKey;
            }
        }

        private static List<FalsumKeyBinding> CreateDefaultBindings()
        {
            return new List<FalsumKeyBinding>
            {
                Binding(FalsumInputAction.PlayPause, KeyCode.Q),
                Binding(FalsumInputAction.TemporaryAudition, KeyCode.Space),
                Binding(FalsumInputAction.Save, KeyCode.S, FalsumModifierKeys.Ctrl),
                Binding(FalsumInputAction.Undo, KeyCode.Z, FalsumModifierKeys.Ctrl),
                Binding(FalsumInputAction.Redo, KeyCode.Y, FalsumModifierKeys.Ctrl),
                Binding(FalsumInputAction.Copy, KeyCode.C, FalsumModifierKeys.Ctrl),
                Binding(FalsumInputAction.Cut, KeyCode.X, FalsumModifierKeys.Ctrl),
                Binding(FalsumInputAction.Paste, KeyCode.V, FalsumModifierKeys.Ctrl),
                Binding(FalsumInputAction.DeleteSelection, KeyCode.Delete),
                Binding(FalsumInputAction.MiddleEditBoundary, KeyCode.F1),
                Binding(FalsumInputAction.MiddleEditStartEnd, KeyCode.F2),
                Binding(FalsumInputAction.MiddleEditPosition, KeyCode.F3),
                Binding(FalsumInputAction.Mirror, KeyCode.M, FalsumModifierKeys.Ctrl),
                Binding(FalsumInputAction.MirrorFrontBack, KeyCode.F, FalsumModifierKeys.Ctrl),
                Binding(FalsumInputAction.ToggleFullscreen, KeyCode.F11),
                Binding(FalsumInputAction.PointPlaceTap, KeyCode.A),
                Binding(FalsumInputAction.PointPlaceHold, KeyCode.S),
                Binding(FalsumInputAction.PointPlaceFlick, KeyCode.D),
                Binding(FalsumInputAction.PointPlaceSkyArea, KeyCode.F),
                Binding(FalsumInputAction.ToggleGrid, KeyCode.X),
                // DecreaseFlowSpeed / IncreaseFlowSpeed keep their enum values
                // (bindings persist numerically) but are deliberately unbound:
                // flow speed is Ctrl+wheel now, so E/R would be dead keys.
                Binding(FalsumInputAction.ToggleFlickColor, KeyCode.M),
                Binding(FalsumInputAction.SegmentSelect, KeyCode.R, FalsumModifierKeys.Ctrl),
                Binding(FalsumInputAction.ToggleTrackSpeedEffects, KeyCode.T),
                Binding(
                    FalsumInputAction.ToggleSkyAreaBank,
                    KeyCode.B,
                    FalsumModifierKeys.Ctrl),
            };
        }

        private static FalsumKeyBinding Binding(
            FalsumInputAction action,
            KeyCode key,
            FalsumModifierKeys modifiers = FalsumModifierKeys.None)
        {
            return new FalsumKeyBinding
            {
                Action = action,
                Key = key,
                Modifiers = modifiers,
            };
        }
    }
}
