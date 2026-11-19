// ==== 片段：FlickCenterPos / FlickWidth / FlickLeftPos / FlickRightPos / FlickHeadPos ====
// 源文件: Assets/InFalsus/Gameplay/Core/Chart/ChartModels.cs
// 行范围: 49-82 （共 34 行；本片段为逐字摘录，未改代码）
// 说明  : FlickNote 把中点存在 StartPos、宽度存在 StartWidth。FlickHeadPos 按方向取远端：16 → 左端，否则右端。
// ---------------------------------------------------------------------------

        public int FlickDirection;

        // Flick stores its midpoint in StartPos and its width in StartWidth.
        // SkyArea stores endpoint centers and widths in Start/End pairs.
        public double FlickCenterPos
        {
            get { return StartPos; }
        }

        public double FlickWidth
        {
            get { return StartWidth; }
        }

        public double FlickLeftPos
        {
            get { return FlickCenterPos - FlickWidth * 0.5; }
        }

        public double FlickRightPos
        {
            get { return FlickCenterPos + FlickWidth * 0.5; }
        }

        public double FlickHeadPos
        {
            get
            {
                return FlickDirection == 16
                    ? FlickLeftPos
                    : FlickRightPos;
            }
        }

