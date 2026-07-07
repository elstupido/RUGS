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

        // DEV-only: synthetic rows appended to the list panels (laundry / dealers / GL) so the scroll fix can
        // be stress-tested without owning 30 businesses. F10 cycles 0 → 30 → 60 in Dev builds; in release the
        // toggle is stripped with the other hotkeys, so this stays 0 and the row loops are dead code.
        internal static int UiStressRows;
    }
}
