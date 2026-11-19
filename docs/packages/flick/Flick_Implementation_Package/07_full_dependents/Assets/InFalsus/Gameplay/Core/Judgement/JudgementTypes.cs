using InFalsus.Gameplay.Core.Charts;

namespace InFalsus.Gameplay.Core.Judgement
{
    public enum JudgementResult
    {
        None = 0,
        Miss = 1,
        NearEarly = 2,
        NearLate = 3,
        PerfectEarly = 4,
        PerfectLate = 5,
        Shiny = 6,
    }

    public enum JudgementTier
    {
        ExactPlus = 0,
        Exact = 1,
        Near = 2,
        Break = 3,
    }

    public static class Judgement
    {
        public const double ShinyMs = 25.0;
        public const double PerfectMs = 50.0;
        public const double NearMs = 100.0;
        public const double BadMs = 150.0;
        public const double SkyNearMs = 200.0;

        // delta = note time - input time. Positive means early.
        public static JudgementResult Evaluate(double deltaMs, NoteZone zone)
        {
            if (zone == NoteZone.Sky)
            {
                if (deltaMs > SkyNearMs)
                {
                    return JudgementResult.None;
                }
                if (deltaMs > 100.0)
                {
                    return JudgementResult.NearEarly;
                }
                if (deltaMs > 50.0)
                {
                    return JudgementResult.PerfectEarly;
                }
                if (deltaMs >= -50.0)
                {
                    return JudgementResult.Shiny;
                }
                if (deltaMs >= -100.0)
                {
                    return JudgementResult.PerfectLate;
                }
                if (deltaMs >= -200.0)
                {
                    return JudgementResult.NearLate;
                }
                return JudgementResult.Miss;
            }

            if (deltaMs > BadMs)
            {
                return JudgementResult.None;
            }
            if (deltaMs > NearMs)
            {
                return JudgementResult.Miss;
            }
            if (deltaMs > PerfectMs)
            {
                return JudgementResult.NearEarly;
            }
            if (deltaMs > ShinyMs)
            {
                return JudgementResult.PerfectEarly;
            }
            if (deltaMs >= -ShinyMs)
            {
                return JudgementResult.Shiny;
            }
            if (deltaMs >= -PerfectMs)
            {
                return JudgementResult.PerfectLate;
            }
            if (deltaMs >= -NearMs)
            {
                return JudgementResult.NearLate;
            }
            return JudgementResult.Miss;
        }

        public static JudgementTier TierOf(JudgementResult result)
        {
            switch (result)
            {
                case JudgementResult.Shiny:
                    return JudgementTier.ExactPlus;
                case JudgementResult.PerfectEarly:
                case JudgementResult.PerfectLate:
                    return JudgementTier.Exact;
                case JudgementResult.NearEarly:
                case JudgementResult.NearLate:
                    return JudgementTier.Near;
                default:
                    return JudgementTier.Break;
            }
        }

        public static int ScoreOf(JudgementTier tier)
        {
            switch (tier)
            {
                case JudgementTier.ExactPlus:
                    return 10001;
                case JudgementTier.Exact:
                    return 10000;
                case JudgementTier.Near:
                    return 5000;
                default:
                    return 0;
            }
        }

        public static double MissDeadlineMs(NoteZone zone)
        {
            return zone == NoteZone.Sky ? SkyNearMs : NearMs;
        }
    }
}
