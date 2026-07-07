using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Rugs
{
    /// <summary>
    /// THE WASH — CROOKBOOKS™ screen [2] (formerly the standalone "Laundering" panel, now in the terminal's
    /// CRT skin). Every way to turn dirty cash clean:
    ///
    ///   • QUICK WASH — instant, any amount, a flat vig (the always-available release valve);
    ///   • [WASH ALL · SAFE] — one click runs the same biggest-room-first safe sweep the night crew runs,
    ///     any fleet size, any time (<see cref="RugLaunder.WashAllSafe"/>);
    ///   • per-front rows — fine control, $1k / $10k / Safe through a specific business.
    ///
    /// Renders into the terminal's content area; the terminal owns the window, the glance strip (stash ·
    /// heat · wash room — heat finally visible where washing happens), status and navigation.
    /// </summary>
    internal static class RugWashScreen
    {
        private static readonly Color Gold = new Color(0.52f, 0.42f, 0.14f);

        internal static void Build(Transform panel)
        {
            // Quick wash — instant, any amount, a flat cut. The always-available release valve.
            if (RugBooks.Dirty > 0f)
            {
                Section(panel, $"QUICK WASH  ·  instant  ·  −{RugLaunder.DealerVig * 100f:0}% cut");
                Transform qr = Row(panel, "wash dirty cash now", "any amount");
                RugUi.NewButton(qr, "$10k", Gold, 50f, () => DoQuickWash(10000f));
                RugUi.NewButton(qr, "Max",  Gold, 50f, () => DoQuickWash(float.MaxValue));

                // The bulk action: the night crew's sweep, run by hand — safe caps, biggest room first.
                Section(panel, "WASH EVERYTHING  ·  safe caps  ·  no cut");
                Transform wr = Row(panel, "spread the stash across your fronts", "clears overnight");
                RugUi.NewButton(wr, "WASH ALL", Gold, 84f, () => { RugTerminal.SetStatus(RugLaunder.WashAllSafe()); RugTerminal.Refresh(); });
            }

            List<BuildingRegistration> fronts = RugLaunder.Fronts();
            if (fronts.Count == 0)
            {
                RugUi.NewText(panel, "You don't own a business to wash through.\nRent one, give it a name, and let it build up some real sales first.",
                        13, FontStyle.Italic, RugTheme.Green).alignment = TextAnchor.MiddleCenter;
            }
            else
            {
                Section(panel, "YOUR FRONTS");
                // When EVERY front is tapped, say why up front — a wall of "tapped out" otherwise reads as broken.
                if (RugLaunder.TotalSafeCapacity() < 1f)
                    RugUi.NewText(panel,
                        RugLaunder.AutoWashEnabled
                            ? "the night crew already ran the stash through your fronts — nothing left to wash by hand today."
                            : "every front is washed out for today — safe room comes back at the next midnight.",
                        12, FontStyle.Italic, RugTheme.Amber).alignment = TextAnchor.MiddleCenter;
                foreach (BuildingRegistration reg in fronts)
                {
                    BuildingRegistration r = reg; // capture
                    float cap = RugLaunder.CapRemaining(r);
                    string name = string.IsNullOrWhiteSpace(r.BusinessName) ? "(unnamed)" : r.BusinessName;
                    if (name.Length > 22) name = name.Substring(0, 21) + "…"; // keep it inside its column
                    Transform row = Row(panel, name, FrontSub(r, cap, RugFactoryBoost.For(r)));
                    RugUi.NewButton(row, "$1k",  Gold, 44f, () => DoWash(r, 1000f));
                    RugUi.NewButton(row, "$10k", Gold, 50f, () => DoWash(r, 10000f));
                    RugUi.NewButton(row, "Safe", Gold, 50f, () => DoWash(r, RugLaunder.CapRemaining(r)));
                }

                // DEV scroll stress: synthetic rows (F10 cycles the count; the const gate strips this from release).
                for (int i = 0; RugsConfig.Dev && i < RugsConfig.UiStressRows; i++)
                {
                    Transform srow = Row(panel, $"STRESS BIZ #{i + 1:00}", "can wash $12,345 today");
                    RugUi.NewButton(srow, "$1k",  Gold, 44f, () => { });
                    RugUi.NewButton(srow, "$10k", Gold, 50f, () => { });
                    RugUi.NewButton(srow, "Safe", Gold, 50f, () => { });
                }
            }

            RugUi.NewText(panel, "clears clean overnight  ·  booked as that business's income  ·  taxed",
                    11, FontStyle.Italic, RugTheme.GreenDim).alignment = TextAnchor.MiddleCenter;
        }

        private static void DoWash(BuildingRegistration reg, float amount)
        {
            if (amount < 1f) { RugTerminal.SetStatus("That business is tapped out for safe washing today."); return; }
            RugTerminal.SetStatus(RugLaunder.Wash(reg, amount));
            RugTerminal.Refresh();
        }

        private static void DoQuickWash(float amount)
        {
            RugTerminal.SetStatus(RugLaunder.WashViaDealer(amount));
            RugTerminal.Refresh();
        }

        // The row's status — and when a front CAN'T wash, WHY, so "tapped out" never reads as a dead/broken button.
        // (sub column ~170px; overflow renders under the buttons, so every variant stays short.)
        private static string FrontSub(BuildingRegistration r, float cap, float boost)
        {
            if (cap >= 1f)
                return boost >= 0.005f ? $"wash ${cap:N0}/day · fed +{boost * 100f:0}%" : $"can wash ${cap:N0} today";
            float today = RugLaunder.WashedToday(r);
            if (today >= 1f) return $"washed ${today:N0} today";  // room's spent — show what moved through
            return "no sales history yet";
        }

        private static void Section(Transform panel, string label)
        {
            RugUi.NewText(panel, label, 12, FontStyle.Bold, RugTheme.GreenDim).alignment = TextAnchor.MiddleCenter;
        }

        private static Transform Row(Transform panel, string name, string sub)
        {
            Image row = RugUi.NewImage(panel, new Color(1f, 1f, 1f, 0.04f));
            row.gameObject.AddComponent<LayoutElement>().minHeight = 36f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 3, 3);
            hlg.spacing = 5;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            Text n = RugUi.NewText(row.transform, name, 14, FontStyle.Bold, Color.white);
            n.gameObject.AddComponent<LayoutElement>().preferredWidth = 260f;
            Text s = RugUi.NewText(row.transform, sub, 12, FontStyle.Italic, RugTheme.Green);
            s.gameObject.AddComponent<LayoutElement>().preferredWidth = 170f;
            return row.transform;
        }
    }
}
