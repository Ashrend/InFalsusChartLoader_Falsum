namespace InFalsusChartLoader_falsum
{
    /// <summary>A stand-in for the mod's entry point, which the rule tests do not link.</summary>
    /// <remarks>
    /// The address cache builds its key out of the mod's version, and the real
    /// <c>ModCore</c> brings the whole game in with it. Only the version constant is needed
    /// here, and it never matters in this process: without the game module there is nothing to
    /// key on, so the cache stays inert and never reads or writes a file.
    /// </remarks>
    internal static class ModCore
    {
        /// <summary>The mod's version, as the real one has it.</summary>
        internal const string Version = "0.0.0-tests";
    }
}
