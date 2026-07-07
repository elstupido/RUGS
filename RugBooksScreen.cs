using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Rugs
{
    /// <summary>
    /// THE BOOKS — CROOKBOOKS™ screen [1] (formerly the standalone "Grand Ledger" panel). Old-school
    /// accounting: the books AND the levers on one screen.
    ///
    ///   • every business as a ledger row — role (FRONT washes · RIDER earns), estimated dirty intake/day,
    ///     wash capacity/day, factory feed — with its controls inline (+RIDER / COLLECT / CUT);
    ///   • [COLLECT ALL] — one click sweeps every rider's held take (same math/heat as collecting each);
    ///   • the totals that ARE the endgame — daily intake vs wash capacity, with the balance verdict;
    ///   • lifetime earned (dirty) vs washed (clean) — income and washcome;
    ///   • the NIGHT CREW switch (5+ businesses) and its receipts.
    ///
    /// Renders into the terminal's content area; the terminal owns the window, glance strip, status line
    /// and navigation (<see cref="RugTerminal"/>). Rows are monospace padded-column Texts so the table
    /// stays aligned while each row carries its own buttons.
    /// </summary>
    internal static class RugBooksScreen
    {
        private static readonly Color Green = new Color(0.20f, 0.45f, 0.22f);
        private static readonly Color Red   = new Color(0.45f, 0.22f, 0.20f);

        internal static void Build(Transform panel)
        {
            List<BuildingRegistration> owned = RugLaunder.AllOwned();
            float inTotal = 0f, washTotal = 0f, holding = 0f;

            if (owned.Count == 0)
            {
                RugUi.NewText(panel, "NO BUSINESSES ON FILE.\nRent one, name it, let it trade — then the books have something to count.",
                        13, FontStyle.Italic, RugTheme.Green).alignment = TextAnchor.MiddleCenter;
            }
            else
            {
                // Column header + rule (same mono padding as the rows, so everything lines up).
                RugUi.NewText(panel,
                        "BUSINESS           ROLE     IN $/DAY  WASH $/DAY    FED\n" + new string('─', 56),
                        13, FontStyle.Normal, RugTheme.GreenDim).alignment = TextAnchor.MiddleLeft;

                foreach (BuildingRegistration reg in owned)
                {
                    BuildingRegistration r = reg; // capture for the closures
                    bool rider = RugSidecars.HasSidecar(RugLaunder.Key(r));
                    float boost = RugFactoryBoost.For(r);
                    float rev = RugLaunder.BelievableRevenue(r);

                    float inDay = 0f, washDay = 0f;
                    if (rider)
                    {
                        inDay = rev * RugSidecars.Factor * (1f + boost); // tomorrow's estimated take
                        holding += RugSidecars.Held(r);
                    }
                    else washDay = RugLaunder.PlausibleCap(r) * (1f + boost); // safe capacity per day
                    inTotal += inDay;
                    washTotal += washDay;

                    Transform row = LedgerRow(panel, Cols(r, rider, inDay, washDay, boost));
                    if (rider)
                    {
                        RugUi.NewButton(row, "COLLECT", Green, 70f, () => { RugTerminal.SetStatus(RugSidecars.Collect(r)); RugTerminal.Refresh(); });
                        RugUi.NewButton(row, "CUT", Red, 46f, () =>
                        {
                            RugSidecars.Remove(r); // sweeps held cash into the stash
                            RugTerminal.SetStatus($"rider cut loose at {Name(r)} — held cash swept, it's a FRONT again.");
                            RugTerminal.Refresh();
                        });
                    }
                    else
                    {
                        RugUi.NewButton(row, "+RIDER", Green, 66f, () =>
                        {
                            RugSidecars.Attach(r);
                            RugTerminal.SetStatus($"rider posted at {Name(r)} — earning starts tomorrow; it's out of the wash pool.");
                            RugTerminal.Refresh();
                        });
                    }
                }

                // DEV scroll stress: synthetic rows (F10 cycles the count; the const gate strips this from release).
                for (int i = 0; RugsConfig.Dev && i < RugsConfig.UiStressRows; i++)
                {
                    Transform srow = LedgerRow(panel,
                        $"STRESS BIZ #{i + 1:00}".PadRight(19) + "FRONT".PadRight(8) + "—".PadLeft(9) + "$12,345".PadLeft(12) + "—".PadLeft(7));
                    RugUi.NewButton(srow, "+RIDER", Green, 66f, () => { });
                }

                // Totals rule + row, then the balance verdict — THE endgame number.
                RugUi.NewText(panel,
                        new string('─', 56) + "\n" +
                        "TOTALS".PadRight(27) + ("$" + inTotal.ToString("N0")).PadLeft(9) + ("$" + washTotal.ToString("N0")).PadLeft(12),
                        13, FontStyle.Bold, RugTheme.Green).alignment = TextAnchor.MiddleLeft;
                RugUi.NewText(panel, Verdict(inTotal, washTotal), 13, FontStyle.Bold, RugTheme.Amber)
                    .alignment = TextAnchor.MiddleCenter;
            }

            // The lifetime ledger — income and washcome.
            var lt = new StringBuilder();
            lt.Append("LIFETIME   earned ".PadRight(19)).Append(("$" + RugBooks.EarnedLifetime.ToString("N0")).PadLeft(12))
              .Append("   washed ").Append(("$" + RugBooks.DeclaredLifetime.ToString("N0")).PadLeft(12)).Append('\n');
            lt.Append("ON HAND    stash  ".PadRight(19)).Append(("$" + RugBooks.Dirty.ToString("N0")).PadLeft(12))
              .Append("   riders ").Append(("$" + holding.ToString("N0")).PadLeft(12));
            RugUi.NewText(panel, lt.ToString(), 13, FontStyle.Normal, RugTheme.Green).alignment = TextAnchor.MiddleLeft;

            // [COLLECT ALL] — the bulk twin of the per-row COLLECT (the 30-click fix, rider edition).
            if (holding >= 1f)
            {
                Transform row = ControlRow(panel, "RIDERS", $"holding ${holding:N0} across the fleet");
                RugUi.NewButton(row, "COLLECT ALL", Green, 100f, () => { RugTerminal.SetStatus(RugSidecars.CollectAll()); RugTerminal.Refresh(); });
            }

            // NIGHT CREW (auto-wash) — the automation switch, gated on fleet size, with its receipt.
            if (RugLaunder.AutoWashUnlocked())
            {
                bool on = RugLaunder.AutoWashEnabled;
                Transform row = ControlRow(panel, "NIGHT CREW",
                    on ? "washing the stash through every front, nightly" : "idle — washing is manual");
                RugUi.NewButton(row, on ? "ON" : "OFF", on ? Green : Red, 56f, () =>
                {
                    RugLaunder.SetAutoWash(!RugLaunder.AutoWashEnabled);
                    RugTerminal.SetStatus(RugLaunder.AutoWashEnabled ? "night crew hired — first run tonight." : "night crew stood down.");
                    RugTerminal.Refresh();
                });
                string rep = RugLaunder.NightCrewReport();
                if (rep.Length > 0)
                    RugUi.NewText(panel, rep, 12, FontStyle.Italic, RugTheme.GreenDim).alignment = TextAnchor.MiddleCenter;
            }
            else
            {
                RugUi.NewText(panel,
                    $"NIGHT CREW: locked — automation takes a real fleet ({RugLaunder.AutoWashMinBusinesses}+ businesses; you run {owned.Count}).",
                    12, FontStyle.Italic, RugTheme.GreenDim).alignment = TextAnchor.MiddleCenter;
            }
        }

        // One padded-column line for a business row (monospace keeps every row's columns aligned).
        private static string Cols(BuildingRegistration reg, bool rider, float inDay, float washDay, float boost)
        {
            string name = Name(reg);
            if (name.Length > 18) name = name.Substring(0, 17) + "…";
            return name.PadRight(19)
                 + (rider ? "RIDER" : "FRONT").PadRight(8)
                 + (inDay >= 1f ? "$" + inDay.ToString("N0") : "—").PadLeft(9)
                 + (washDay >= 1f ? "$" + washDay.ToString("N0") : "—").PadLeft(12)
                 + (boost >= 0.005f ? "+" + (boost * 100f).ToString("0") + "%" : "—").PadLeft(7);
        }

        private static string Name(BuildingRegistration reg)
            => string.IsNullOrWhiteSpace(reg.BusinessName) ? "(unnamed)" : reg.BusinessName;

        // The balance read — THE endgame number: daily dirty intake vs daily wash capacity.
        private static string Verdict(float inTotal, float washTotal)
        {
            if (inTotal < 1f && washTotal < 1f) return "nothing moving yet — put some product on the street.";
            float gap = inTotal - washTotal;
            if (Mathf.Abs(gap) <= 0.10f * Mathf.Max(inTotal, washTotal))
                return "BALANCED BOOKS. beautiful. now scale it.";
            return gap > 0f
                ? $"intake outruns wash by ${gap:N0}/day — the books need more fronts."
                : $"wash room to spare: ${-gap:N0}/day — you can run more product.";
        }

        // A ledger row: the padded-column mono text on the left, the row's control buttons on the right.
        private static Transform LedgerRow(Transform panel, string cols)
        {
            Image row = RugUi.NewImage(panel, new Color(1f, 1f, 1f, 0.03f));
            row.gameObject.AddComponent<LayoutElement>().minHeight = 28f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(6, 6, 2, 2);
            hlg.spacing = 5;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            Text t = RugUi.NewText(row.transform, cols, 13, FontStyle.Normal, RugTheme.Green);
            t.gameObject.AddComponent<LayoutElement>().preferredWidth = 440f;
            return row.transform;
        }

        // A labeled control row (COLLECT ALL, the night-crew switch).
        private static Transform ControlRow(Transform panel, string name, string sub)
        {
            Image row = RugUi.NewImage(panel, new Color(1f, 1f, 1f, 0.04f));
            row.gameObject.AddComponent<LayoutElement>().minHeight = 34f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 3, 3);
            hlg.spacing = 5;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            Text n = RugUi.NewText(row.transform, name, 14, FontStyle.Bold, RugTheme.GreenBright);
            n.gameObject.AddComponent<LayoutElement>().preferredWidth = 130f;
            Text s = RugUi.NewText(row.transform, sub, 12, FontStyle.Italic, RugTheme.GreenDim);
            s.gameObject.AddComponent<LayoutElement>().preferredWidth = 330f;
            return row.transform;
        }
    }
}
