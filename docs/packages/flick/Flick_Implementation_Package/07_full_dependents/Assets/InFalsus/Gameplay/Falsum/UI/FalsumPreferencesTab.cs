namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// Identifies a preferences page.
    ///
    /// Grouped by what the user is trying to do rather than by which C# field
    /// the setting happens to live in: the old "Advanced" bucket held grid
    /// rendering, out-of-bounds clamping, cache housekeeping and the global
    /// reset all at once. Grid and clamping now sit with the editing settings
    /// they affect; cache housekeeping and the global reset stay in Advanced,
    /// and product information has its own page so neither tab has to be
    /// reached through the other.
    /// </summary>
    internal enum FalsumPreferencesTab
    {
        /// <summary>Resolution, window mode, anti-aliasing.</summary>
        Display,
        /// <summary>Music and judgement-sound loudness.</summary>
        Volume,
        /// <summary>Key bindings.</summary>
        Keys,
        /// <summary>Editing behaviour plus grid and bounds handling.</summary>
        Editing,
        /// <summary>Automatic backup settings and per-chart history.</summary>
        Backup,
        /// <summary>Cache housekeeping and the global reset.</summary>
        Advanced,
        /// <summary>Product info: version, credits, disclaimer.</summary>
        About,
    }
}
