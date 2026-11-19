using System;

namespace InFalsus.Gameplay.Presentation.Timing
{
    public sealed class PresentationAnimationClock
    {
        public const double MaxFrameDeltaSeconds = 0.1;

        public double TimeSeconds { get; private set; }
        public bool Running { get; private set; }

        public void Tick(bool running, double unscaledDeltaSeconds)
        {
            Running = running;
            if (!running ||
                double.IsNaN(unscaledDeltaSeconds) ||
                unscaledDeltaSeconds <= 0.0)
            {
                return;
            }

            TimeSeconds += Math.Min(
                unscaledDeltaSeconds,
                MaxFrameDeltaSeconds);
        }

        public void Reset()
        {
            TimeSeconds = 0.0;
            Running = false;
        }
    }
}
