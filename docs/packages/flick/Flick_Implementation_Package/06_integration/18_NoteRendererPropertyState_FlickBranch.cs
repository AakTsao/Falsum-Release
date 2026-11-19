// ==== 片段：★ 方向 → 颜色的唯一真源 ====
// 源文件: Assets/InFalsus/Gameplay/Presentation/Notes/NoteRendererPropertyState.cs
// 行范围: 72-107 （共 36 行；本片段为逐字摘录，未改代码）
// 说明  : 4 → (0.2,0.95,0.55) 绿；16 → (1,0.82,0.18) 黄。flick 只写 _Color/_Opacity/_BarRepeat 三个属性，其余位（_Held/_Progress）不碰。
// ---------------------------------------------------------------------------

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
