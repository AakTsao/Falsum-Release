// ==== 片段：★ 模型层强校验 ====
// 源文件: Assets/InFalsus/Gameplay/Core/Chart/ChartModels.cs
// 行范围: 147-175 （共 29 行；本片段为逐字摘录，未改代码）
// 说明  : cellCount>0、span 在 0..1（带 1/cells 容差）、dir ∈ {4,16}。注意 flick 用 RequireFlickSpan 而不是 RequireUnitSpan，容差不同。
// ---------------------------------------------------------------------------

                case NoteKind.Flick:
                    RequireFinite(note.StartPos, context + ".startPos");
                    RequireFinite(
                        note.StartWidth,
                        context + ".startWidth");
                    if (note.StartCellCount <= 0 ||
                        note.EndCellCount <= 0)
                    {
                        throw new FormatException(
                            context +
                            ".startCellCount and endCellCount " +
                            "must be positive.");
                    }
                    if (!allowOutOfBounds)
                    {
                        RequireFlickSpan(
                            note.StartPos,
                            note.StartWidth,
                            note.StartCellCount,
                            context + " flick span");
                    }
                    if (note.FlickDirection != 4 &&
                        note.FlickDirection != 16)
                    {
                        throw new FormatException(
                            context +
                            ".flickDirection must be 4 or 16.");
                    }
                    break;
