using System;
using System.Text;
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// Retro-terminal styling for the mod's OWN uGUI panels — a nod to the text-mode Drug Wars era. Supplies a
    /// monospace OS font (so box-drawing characters line up), a green/amber-on-black palette, and tiny header /
    /// divider builders. Applied to RUGS! panels only; the Plug / IRS phone messages route through Big Ambitions'
    /// native UI and are left untouched (can't be restyled Harmony-free).
    ///
    /// NOTE: "drawille" here means ASCII / Unicode box-drawing text-art (U+2500…), NOT literal Braille dot-art.
    /// Braille glyph coverage in OS monospace fonts is unreliable and bundling a font would hit the AssetBundle
    /// wall this mod avoids; box-drawing is well covered by Consolas / Courier New. If no monospace family is
    /// installed we fall back to the legacy font and skip the box-art (which would misalign in a proportional font).
    /// </summary>
    internal static class RugTheme
    {
        // Terminal palette.
        internal static readonly Color Green       = new Color(0.45f, 0.92f, 0.50f); // phosphor green — body text
        internal static readonly Color GreenDim    = new Color(0.30f, 0.62f, 0.34f); // dim green — subtitles
        internal static readonly Color GreenBright = new Color(0.62f, 1.00f, 0.65f); // bright green — headers
        internal static readonly Color Amber       = new Color(0.95f, 0.78f, 0.35f); // amber — emphasis / warnings

        private static Font _font;
        private static bool _isMono;
        private static bool _resolved;

        /// <summary>A monospace font (Consolas → Courier New → Lucida Console → legacy built-in). Cached, never null.</summary>
        internal static Font Mono() { Resolve(); return _font; }

        /// <summary>True when a real monospace family was found — gates the box-drawing art.</summary>
        internal static bool IsMono { get { Resolve(); return _isMono; } }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _font = TryOsFont("Consolas") ?? TryOsFont("Courier New") ?? TryOsFont("Lucida Console");
            _isMono = _font != null;
            if (_font == null) // last resort — the legacy built-ins (not monospace, but always present)
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                     ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        // Check the OS font list FIRST (reliable), then create — CreateDynamicFontFromOSFont returns a non-null
        // Font even for a missing family, so we can't lean on its result to detect availability.
        private static Font TryOsFont(string family)
        {
            try
            {
                string[] installed = Font.GetOSInstalledFontNames();
                if (installed == null) return null;
                foreach (string n in installed)
                    if (!string.IsNullOrEmpty(n) && n.IndexOf(family, StringComparison.OrdinalIgnoreCase) >= 0)
                        return Font.CreateDynamicFontFromOSFont(family, 16);
            }
            catch { }
            return null;
        }

        /// <summary>A boxed header banner sized to the title (monospace only); the plain title without a mono font.</summary>
        internal static string Banner(string title)
        {
            title = (title ?? "").Trim();
            if (!IsMono || title.Length == 0) return title;
            string t = " " + title + " ";
            string bar = Repeat('═', t.Length);
            return "╔" + bar + "╗\n║" + t + "║\n╚" + bar + "╝";
        }

        /// <summary>A horizontal rule of box-drawing dashes (plain dashes degrade fine without a mono font).</summary>
        internal static string Rule(int chars) => Repeat('─', Mathf.Clamp(chars, 4, 80));

        private static string Repeat(char c, int n)
        {
            var sb = new StringBuilder(n);
            for (int i = 0; i < n; i++) sb.Append(c);
            return sb.ToString();
        }
    }
}
