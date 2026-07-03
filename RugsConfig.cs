namespace Rugs
{
    /// <summary>
    /// Build-time switches. Flip <see cref="Dev"/> to <c>false</c> for the Steam release: it
    /// disables the authoring hotkeys (F6/F7/F8/F9), the local dealer-spot override, and the
    /// chatty diagnostic logs — leaving only the shipped, baked-in roster and player-facing logs.
    /// </summary>
    internal static class RugsConfig
    {
        internal const bool Dev = false;
    }
}
