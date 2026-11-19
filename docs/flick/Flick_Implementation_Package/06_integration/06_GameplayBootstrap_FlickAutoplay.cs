// ==== 片段：Autoplay 触发 PressFlick ====
// 源文件: Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs
// 行范围: 924-934 （共 11 行；本片段为逐字摘录，未改代码）
// 说明  : 自动演奏路径：在 note.TimeMs 时刻调用 _engine.PressFlick(note.FlickDirection, note.TimeMs)。
// ---------------------------------------------------------------------------

						_engine.ReleaseLane(note.StartLane, note.TimeMs);
					}
				}
			}
			else if (note.Kind == NoteKind.Flick && nowMs >= note.TimeMs)
			{
				_engine.PressFlick(note.FlickDirection, note.TimeMs);
			}
			else if (note.Kind == NoteKind.SkyArea && nowMs >= note.TimeMs)
			{
				_engine.PressSky(note.TimeMs);
