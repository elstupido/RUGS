using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// Corner flavor — the absurd one-liner a street character mutters as you walk up to a dealer. No stakes,
    /// pure texture: the Drug-Wars "someone on the subway says..." beat, transplanted to the street corner.
    ///
    /// SOURCE / ATTRIBUTION. The sayings below are the genuine "subway sayings" from <c>dopewars</c> — the
    /// open-source Drug Wars by Ben Webb and contributors (https://dopewars.sourceforge.io/), distributed under
    /// the GNU GPL v2-or-later. RUGS! is GPL-3.0, which is compatible, so they're reused here under that licence
    /// with credit. They are reproduced verbatim; the handful of drug-specific and era/politically-specific lines
    /// in the original were OMITTED (left out, not reworded) to fit a contemporary, rug-world release. (If you
    /// want extra rug-themed corner lines, add them by hand — keep the catalogue human-authored.)
    /// </summary>
    internal static class RugFlavor
    {
        // Who's talking — connective framing around the saying (RUGS!-authored), picked at random.
        private static readonly string[] Openers =
        {
            "A stranger sidles up and mutters,",
            "Some corner character leans in:",
            "A wild-eyed guy stops you to say,",
            "A woman pushing a shopping cart announces,",
            "Passing by, someone says,",
        };

        // Verbatim dopewars subway sayings (the timeless, non-drug, non-dated subset) — see attribution above.
        private static readonly string[] Sayings =
        {
            "Wouldn't it be funny if everyone suddenly quacked at once?",
            "The Pope was once Jewish, you know",
            "I'll bet you have some really interesting dreams",
            "So I think I'm going to Amsterdam this year",
            "Son, you need a yellow haircut",
            "I think it's wonderful what they're doing with incense these days",
            "I wasn't always a woman, you know",
            "Oh, you must be from California",
            "I used to be a hippie, myself",
            "There's nothing like having lots of money",
            "You look like an aardvark!",
            "Haven't I seen you on TV?",
            "I think hemorrhoid commercials are really neat!",
            "We only use 20% of our brains, so why not burn out the other 80%",
            "I'd like to sell you an edible poodle",
            "I am the walrus!",
            "I feel an unaccountable urge to dye my hair blue",
            "Wasn't Jane Fonda wonderful in Barbarella",
            "Would you like a jelly baby?",
        };

        /// <summary>A random corner one-liner (opener + saying), or null if the catalogue is somehow empty.</summary>
        internal static string Line()
        {
            if (Sayings.Length == 0) return null;
            string opener = Openers[UnityEngine.Random.Range(0, Openers.Length)];
            string saying = Sayings[UnityEngine.Random.Range(0, Sayings.Length)];
            return opener + " “" + saying + "”";
        }
    }
}
