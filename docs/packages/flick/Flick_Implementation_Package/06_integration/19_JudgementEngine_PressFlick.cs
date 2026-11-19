// ==== 片段：★ 判定入口 PressFlick(direction, nowMs) ====
// 源文件: Assets/InFalsus/Gameplay/Core/Judgement/JudgementEngine.cs
// 行范围: 181-227 （共 47 行；本片段为逐字摘录，未改代码）
// 说明  : 从全表挑「时间最近且方向匹配」的候选；窗口 delta ∈ [-SkyNearMs, BadMs]。不要为性能加游标——见 02-invariants.md A-22。
// ---------------------------------------------------------------------------

        public JudgementResult PressFlick(int direction, double nowMs)
        {
            if (direction != 4 && direction != 16)
            {
                return JudgementResult.None;
            }

            ChartNote candidate = null;
            var best = double.MaxValue;
            for (var i = 0; i < _notes.Count; i++)
            {
                var note = _notes[i];
                if (note.Judged || note.Consumed || note.Kind != NoteKind.Flick)
                {
                    continue;
                }
                if (note.FlickDirection != direction)
                {
                    continue;
                }

                var delta = note.TimeMs - nowMs;
                if (delta > Judgement.BadMs || delta < -Judgement.SkyNearMs)
                {
                    continue;
                }
                var abs = Math.Abs(delta);
                if (abs < best)
                {
                    best = abs;
                    candidate = note;
                }
            }

            if (candidate == null)
            {
                return JudgementResult.None;
            }

            var candidateDelta = candidate.TimeMs - nowMs;
            var result = Judgement.Evaluate(candidateDelta, NoteZone.Sky);
            if (result != JudgementResult.None)
            {
                ApplyResult(candidate, result, nowMs, candidateDelta, false);
            }
            return result;
        }
