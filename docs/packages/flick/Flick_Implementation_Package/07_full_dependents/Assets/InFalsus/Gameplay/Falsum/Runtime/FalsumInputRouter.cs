using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed class FalsumInputRouter
    {
        private readonly Dictionary<FalsumInputAction, FalsumKeyBinding>
            _bindings =
                new Dictionary<FalsumInputAction, FalsumKeyBinding>();

        public void Apply(FalsumPreferences preferences)
        {
            _bindings.Clear();
            if (preferences == null ||
                preferences.Bindings == null)
            {
                return;
            }
            for (var i = 0; i < preferences.Bindings.Count; i++)
            {
                var binding = preferences.Bindings[i];
                if (binding != null &&
                    binding.Key != KeyCode.None)
                {
                    _bindings[binding.Action] = binding.Clone();
                }
            }
        }

        public bool WasPressed(FalsumInputAction action)
        {
            FalsumKeyBinding binding;
            return _bindings.TryGetValue(action, out binding) &&
                   Input.GetKeyDown(binding.Key) &&
                   ModifiersMatch(binding.Modifiers);
        }

        public bool IsHeld(FalsumInputAction action)
        {
            FalsumKeyBinding binding;
            return _bindings.TryGetValue(action, out binding) &&
                   Input.GetKey(binding.Key) &&
                   ModifiersMatch(binding.Modifiers);
        }

        public bool WasReleased(FalsumInputAction action)
        {
            FalsumKeyBinding binding;
            return _bindings.TryGetValue(action, out binding) &&
                   Input.GetKeyUp(binding.Key);
        }

        public static bool ModifiersMatch(
            FalsumModifierKeys required)
        {
            var ctrl = FalsumModifierState.Control;
            var alt =
                Input.GetKey(KeyCode.LeftAlt) ||
                Input.GetKey(KeyCode.RightAlt);
            var shift =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);
            return ctrl ==
                   ((required & FalsumModifierKeys.Ctrl) != 0) &&
                   alt ==
                   ((required & FalsumModifierKeys.Alt) != 0) &&
                   shift ==
                   ((required & FalsumModifierKeys.Shift) != 0);
        }

        public static bool TryCapture(
            KeyDownEvent evt,
            out FalsumKeyBinding binding)
        {
            binding = new FalsumKeyBinding
            {
                Key = evt.keyCode,
                Modifiers = CaptureModifiers(evt),
            };
            if (binding.Key == KeyCode.None &&
                evt.character != '\0')
            {
                var character = char.ToLowerInvariant(
                    evt.character);
                if (character >= 'a' && character <= 'z')
                {
                    binding.Key = (KeyCode)Enum.Parse(
                        typeof(KeyCode),
                        character.ToString().ToUpperInvariant());
                }
                else if (character >= '0' && character <= '9')
                {
                    binding.Key = KeyCode.Alpha0 +
                                  (character - '0');
                }
            }
            return binding.Key != KeyCode.None &&
                   binding.Key != KeyCode.LeftControl &&
                   binding.Key != KeyCode.RightControl &&
                   binding.Key != KeyCode.LeftAlt &&
                   binding.Key != KeyCode.RightAlt &&
                   binding.Key != KeyCode.LeftShift &&
                   binding.Key != KeyCode.RightShift;
        }

        public static string Format(FalsumKeyBinding binding)
        {
            if (binding == null || binding.Key == KeyCode.None)
            {
                return L.Get("falsum.shortcut.unbound");
            }
            var parts = new List<string>();
            if ((binding.Modifiers & FalsumModifierKeys.Ctrl) != 0)
            {
                parts.Add(L.Get("falsum.shortcut.ctrl"));
            }
            if ((binding.Modifiers & FalsumModifierKeys.Alt) != 0)
            {
                parts.Add(L.Get("falsum.shortcut.alt"));
            }
            if ((binding.Modifiers & FalsumModifierKeys.Shift) != 0)
            {
                parts.Add(L.Get("falsum.shortcut.shift"));
            }
            parts.Add(FormatKey(binding.Key));
            return string.Join(
                L.Get("falsum.shortcut.separator"),
                parts.ToArray());
        }

        public static string FormatKey(KeyCode key)
        {
            if (key == KeyCode.Return)
            {
                return L.Get("falsum.key.enter");
            }
            if (key == KeyCode.Escape)
            {
                return L.Get("falsum.key.escape");
            }
            if (key == KeyCode.Backspace)
            {
                return L.Get("falsum.key.backspace");
            }
            return key.ToString().ToUpperInvariant();
        }

        private static FalsumModifierKeys CaptureModifiers(
            KeyDownEvent evt)
        {
            var modifiers = FalsumModifierKeys.None;
            if (evt.ctrlKey)
            {
                modifiers |= FalsumModifierKeys.Ctrl;
            }
            if (evt.altKey)
            {
                modifiers |= FalsumModifierKeys.Alt;
            }
            if (evt.shiftKey)
            {
                modifiers |= FalsumModifierKeys.Shift;
            }
            return modifiers;
        }

        /// <summary>Human-readable label for an input action, localized.</summary>
        public static string ActionLabel(FalsumInputAction action)
        {
            switch (action)
            {
            case FalsumInputAction.PlayPause:
                return L.Get("falsum.preferences.keys.action.playPause");
            case FalsumInputAction.TemporaryAudition:
                return L.Get("falsum.preferences.keys.action.temporaryAudition");
            case FalsumInputAction.Save:
                return L.Get("falsum.preferences.keys.action.save");
            case FalsumInputAction.Undo:
                return L.Get("falsum.preferences.keys.action.undo");
            case FalsumInputAction.Redo:
                return L.Get("falsum.preferences.keys.action.redo");
            case FalsumInputAction.Copy:
                return L.Get("falsum.action.copy");
            case FalsumInputAction.Cut:
                return L.Get("falsum.action.cut");
            case FalsumInputAction.Paste:
                return L.Get("falsum.action.paste");
            case FalsumInputAction.DeleteSelection:
                return L.Get("falsum.preferences.keys.action.deleteSelection");
            case FalsumInputAction.SegmentSelect:
                return L.Get("falsum.radial.segmentSelect");
            case FalsumInputAction.MiddleEditBoundary:
                return L.Get("falsum.preferences.keys.action.boundaryEdit");
            case FalsumInputAction.MiddleEditStartEnd:
                return L.Get("falsum.preferences.keys.action.startEndEdit");
            case FalsumInputAction.MiddleEditPosition:
                return L.Get("falsum.preferences.keys.action.positionEdit");
            case FalsumInputAction.Mirror:
                return L.Get("falsum.preferences.keys.action.mirror");
            case FalsumInputAction.MirrorFrontBack:
                return L.Get("falsum.preferences.keys.action.mirrorFrontBack");
            case FalsumInputAction.ToggleFlickColor:
                return L.Get("falsum.preferences.keys.action.toggleFlickColor");
            case FalsumInputAction.ToggleFullscreen:
                return L.Get("falsum.preferences.keys.action.toggleFullscreen");
            case FalsumInputAction.PointPlaceTap:
                return L.Get("falsum.preferences.keys.action.pointPlaceTap");
            case FalsumInputAction.PointPlaceHold:
                return L.Get("falsum.preferences.keys.action.pointPlaceHold");
            case FalsumInputAction.PointPlaceFlick:
                return L.Get("falsum.preferences.keys.action.pointPlaceFlick");
            case FalsumInputAction.PointPlaceSkyArea:
                return L.Get("falsum.preferences.keys.action.pointPlaceSkyArea");
            case FalsumInputAction.ToggleGrid:
                return L.Get("falsum.preferences.keys.action.toggleGrid");
            case FalsumInputAction.DecreaseFlowSpeed:
                return L.Get("falsum.preferences.keys.action.decreaseFlow");
            case FalsumInputAction.IncreaseFlowSpeed:
                return L.Get("falsum.preferences.keys.action.increaseFlow");
            case FalsumInputAction.ToggleTrackSpeedEffects:
                return L.Get(
                    "falsum.preferences.keys.action.toggleTrackSpeedEffects");
            case FalsumInputAction.ToggleSkyAreaBank:
                // Without this case the `default` arm would leak the English
                // enum name into the preferences list.
                return L.Get(
                    "falsum.preferences.keys.action.toggleSkyAreaBank");
            default:
                return action.ToString();
            }
        }

        /// <summary>True when two bindings describe the same key combination.</summary>
        public static bool BindingsEqual(
            FalsumKeyBinding left,
            FalsumKeyBinding right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }
            return left.Key == right.Key &&
                   left.Modifiers == right.Modifiers;
        }
    }
}
