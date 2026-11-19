// ==== 片段：★ 解析 flick(t, center, grid, width, dir) ====
// 源文件: Assets/InFalsus/Gameplay/Core/Chart/ChartParser.cs
// 行范围: 332-377 （共 46 行；本片段为逐字摘录，未改代码）
// 说明  : p1/grid = 中点 StartPos；p2/grid = 宽度 StartWidth；grid 必须 > 0；dir 只能 4/16。
// ---------------------------------------------------------------------------

                    case "flick":
                    {
                        RequireExact(
                            args,
                            5,
                            directive,
                            lineNumber,
                            raw,
                            result);
                        var grid = Number(args[2]);
                        var center = Number(args[1]) / grid;
                        var widthCells = Number(args[3]);
                        if (grid <= 0.0)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "flick grid must be positive.");
                            break;
                        }
                        var gridCells = CellCount(args[2]);
                        if (widthCells < 0.0)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "flick width must not be negative.");
                            break;
                        }
                        var width = widthCells / grid;
                        var direction = Integer(args[4]);
                        if (direction != 4 && direction != 16)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "flick direction must be 4 or 16.");
                            break;
                        }
                        chart.Notes.Add(new ChartNote
                        {
                            // p1/grid is the midpoint; p2/grid is the width.
                            Kind = NoteKind.Flick,
                            TimeMs = ChartTime(args[0], delay),
                            StartPos = center,
                            StartWidth = width,
                            StartCellCount = gridCells,
                            EndCellCount = gridCells,
                            FlickDirection = direction,
                        });
                        break;
                    }
