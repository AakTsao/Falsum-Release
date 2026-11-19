using System;
using InFalsus.Gameplay.Core.Charts;
using UnityEngine;

namespace InFalsus.Gameplay.Presentation.Notes
{
    /// <summary>Owns one renderer's overrides while preserving its original property block.</summary>
    internal sealed class NoteRendererPropertyState
    {
        [Flags]
        private enum Properties
        {
            None = 0, Color = 1, BaseColor = 2, MainColor = 4,
            Opacity = 8, Held = 16, Progress = 32, ModulateProgress = 64, BarRepeat = 128,
        }
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int MainColorId = Shader.PropertyToID("_MainColor");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int HeldId = Shader.PropertyToID("_Held");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int ModulateProgressId = Shader.PropertyToID("_ModulateProgress");
        private static readonly int BarRepeatId = Shader.PropertyToID("_BarRepeat");

        private readonly Renderer _renderer;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private Material _material;
        private Shader _shader;
        private Properties _properties;
        private bool _hasState;
        private NoteKind _kind;
        private float _fade, _progress, _repeat;
        private bool _holding;
        private Color _color;

        public NoteRendererPropertyState(Renderer renderer)
        {
            _renderer = renderer;
        }

        public bool Apply(NoteKind kind, float fade, float progress, bool holding, Color tint,
            int direction, float repeat, bool force)
        {
            if (_renderer == null) return false;
            var material = _renderer.sharedMaterial;
            var shader = material != null ? material.shader : null;
            if (material != _material || shader != _shader || !_hasState)
            {
                _material = material;
                _shader = shader;
                _renderer.GetPropertyBlock(_block);
                _properties = Properties.None;
                if (material != null)
                {
                    Cache(material, ColorId, Properties.Color);
                    Cache(material, BaseColorId, Properties.BaseColor);
                    Cache(material, MainColorId, Properties.MainColor);
                    Cache(material, OpacityId, Properties.Opacity);
                    Cache(material, HeldId, Properties.Held);
                    Cache(material, ProgressId, Properties.Progress);
                    Cache(material, ModulateProgressId, Properties.ModulateProgress);
                    Cache(material, BarRepeatId, Properties.BarRepeat);
                }
                force = true;
            }
            else if (force)
            {
                // A pooled renderer may carry another owner's unrelated
                // overrides; retain them when reapplying the note's state.
                _renderer.GetPropertyBlock(_block);
            }
            var color = kind == NoteKind.Flick
                ? (direction == 4 ? new Color(0.2f, 0.95f, 0.55f, fade) : new Color(1f, 0.82f, 0.18f, fade))
                : new Color(tint.r, tint.g, tint.b, kind == NoteKind.Tap ? Mathf.Clamp01(fade) : fade);
            var relevant = kind == NoteKind.Hold
                ? Properties.Held | Properties.Progress | Properties.ModulateProgress | Properties.Opacity | Properties.Color
                : kind == NoteKind.Flick ? Properties.Opacity | Properties.Color | (repeat > 0 ? Properties.BarRepeat : Properties.None)
                : Properties.Color | Properties.BaseColor | Properties.MainColor;
            relevant &= _properties;
            force |= _kind != kind;
            var dirty = false;
            if ((relevant & Properties.Color) != 0 && (force || _color != color))
            { _block.SetColor(ColorId, color); dirty = true; }
            if ((relevant & Properties.BaseColor) != 0 && (force || _color != color))
            { _block.SetColor(BaseColorId, color); dirty = true; }
            if ((relevant & Properties.MainColor) != 0 && (force || _color != color))
            { _block.SetColor(MainColorId, color); dirty = true; }
            if ((relevant & Properties.Opacity) != 0 && (force || _fade != fade))
            { _block.SetFloat(OpacityId, fade); dirty = true; }
            if ((relevant & Properties.Held) != 0 && (force || _holding != holding))
            { _block.SetFloat(HeldId, holding ? 1f : 0f); dirty = true; }
            if ((relevant & Properties.Progress) != 0 && (force || _progress != progress))
            { _block.SetFloat(ProgressId, progress); dirty = true; }
            if ((relevant & Properties.ModulateProgress) != 0 && force)
            { _block.SetFloat(ModulateProgressId, 0f); dirty = true; }
            if ((relevant & Properties.BarRepeat) != 0 && (force || _repeat != repeat))
            { _block.SetFloat(BarRepeatId, repeat); dirty = true; }
            _hasState = true;
            _kind = kind;
            _fade = fade;
            _progress = progress;
            _repeat = repeat;
            _holding = holding;
            _color = color;
            if (dirty) _renderer.SetPropertyBlock(_block);
            return dirty;
        }

        private void Cache(Material material, int id, Properties property)
        {
            if (material.HasProperty(id)) _properties |= property;
        }
    }
}
