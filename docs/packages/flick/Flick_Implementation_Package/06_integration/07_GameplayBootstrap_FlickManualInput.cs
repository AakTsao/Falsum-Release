// ==== 片段：手动输入：两个方向键分别映射 16 / 4 ====
// 源文件: Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs
// 行范围: 948-962 （共 15 行；本片段为逐字摘录，未改代码）
// 说明  : 16 = 黄/左，4 = 绿/右。切勿写别的值——解析层强校验会拒载整个谱面。
// ---------------------------------------------------------------------------

			{
				_engine.ReleaseLane(lane, nowMs);
			}
		}
		if (Input.GetKeyDown((KeyCode)276))
		{
			_engine.PressFlick(16, nowMs);
		}
		if (Input.GetKeyDown((KeyCode)275))
		{
			_engine.PressFlick(4, nowMs);
		}
		if (Input.GetKeyDown((KeyCode)273))
		{
			_engine.PressSky(nowMs);
