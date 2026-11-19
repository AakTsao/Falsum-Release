using System;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public enum FalsumViewMode
    {
        Perspective3D,
    }

    [Flags]
    public enum FalsumChartLayer
    {
        None = 0,
        Ground = 1 << 0,
        Sky = 1 << 1,
        Both = Ground | Sky,
    }
}
