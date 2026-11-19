// ==== 片段：★ 写谱：flick 行生成（含量化方向 AwayFromZero） ====
// 源文件: Assets/InFalsus/Gameplay/Falsum/Core/ChartWriter.cs
// 行范围: 208-237 （共 30 行；本片段为逐字摘录，未改代码）
// 说明  : 先 FalsumFraction.NormalizePair 归一化，再 round 到 cell 网格。量化方向固定 AwayFromZero（见 02-invariants.md A-9）。
// ---------------------------------------------------------------------------

            if (note.Kind == NoteKind.Flick)
            {
                var cells = note.StartCellCount > 0
                    ? note.StartCellCount
                    : 24;
                double normalizedPosition;
                double normalizedWidth;
                cells = FalsumFraction.NormalizePair(
                    note.StartPos,
                    note.StartWidth,
                    cells,
                    out normalizedPosition,
                    out normalizedWidth);
                var center = Math.Round(
                    normalizedPosition * cells,
                    MidpointRounding.AwayFromZero);
                var width = Math.Max(
                    0.0,
                    Math.Round(
                        normalizedWidth * cells,
                        MidpointRounding.AwayFromZero));
                lines.Add(
                    "flick(" +
                    Number(MusicTime(note.TimeMs, delayMs)) + "," +
                    Number(center) + "," +
                    cells.ToString(Invariant) + "," +
                    Number(width) + "," +
                    note.FlickDirection.ToString(Invariant) + ")");
                return;
            }
