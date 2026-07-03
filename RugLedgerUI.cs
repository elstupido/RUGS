using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Rugs
{
    /// <summary>
    /// The GRAND LEDGER ("the GL") — the empire's ops console, played straight as an 80s fiscal terminal
    /// (monospace, phosphor green, box-drawing rules). Old-school accounting: the books AND the levers on
    /// one screen.
    ///
    ///   • every business you own as a ledger ROW — role (FRONT washes · RIDER earns), estimated dirty
    ///     intake per day, wash capacity per day, factory feed — WITH its controls inline: put a rider on
    ///     a front (+RIDER), collect a rider's take (COLLECT), or cut the rider loose (CUT → back to front);
    ///   • the totals that ARE the endgame — daily intake vs daily wash capacity, with a balance verdict;
    ///   • lifetime earned (dirty) vs lifetime washed (clean) — income and, as the boss put it, washcome;
    ///   • the NIGHT CREW switch: automatic nightly washing through every front (same rules as pressing
    ///     the button yourself — see <see cref="RugLaunder.OnDayChanged"/>), unlocked at 5+ businesses so
    ///     a 30-business fleet isn't 30 clicks.
    ///
    /// Opened from the laundering computer. Rows are monospace padded-column Texts (same font/size), so
    /// the table stays aligned while each row carries its own buttons. Code-only, no Harmony.
    /// </summary>
    internal static class RugLedgerUI
    {
        private static GameObject _root;
        private static Transform _panel;
        private static Text _status;
        private static string _statusText = "";
        private static Font _font;
        private static CursorLockMode _prevLock;
        private static bool _prevCursor;

        internal static bool IsOpen => _root != null;

        private static readonly Color Green = new Color(0.20f, 0.45f, 0.22f);
        private static readonly Color Red   = new Color(0.45f, 0.22f, 0.20f);
        private static readonly Color Slate = new Color(0.30f, 0.25f, 0.18f);

        private static Font UiFont()
        {
            if (_font == null) _font = RugTheme.Mono();
            return _font;
        }

        internal static void Open()
        {
            Close();
            _statusText = "";
            try
            {
                _root = new GameObject("RugLedgerUI");
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30000;
                var scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f;
                _root.AddComponent<GraphicRaycaster>();

                Image backdrop = NewImage(_root.transform, new Color(0f, 0f, 0f, 0.6f));
                Stretch(backdrop.rectTransform);
                var bdBtn = backdrop.gameObject.AddComponent<Button>();
                bdBtn.transition = Selectable.Transition.None;
                bdBtn.onClick.AddListener(Close);

                Image panel = NewImage(_root.transform, new Color(0.05f, 0.07f, 0.05f, 0.98f)); // CRT green-black
                RectTransform prt = panel.rectTransform;
                prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
                prt.sizeDelta = new Vector2(700f, 100f);
                var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(18, 18, 12, 14);
                vlg.spacing = 4;
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.childControlWidth = vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
                var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

                _panel = panel.transform;
                Build(_panel);
                BlockInput(true);
            }
            catch (Exception e)
            {
                Debug.LogError("[RUGS!] grand ledger failed to open: " + e);
                Close();
            }
        }

        private static void Build(Transform panel)
        {
            NewText(panel, RugTheme.Banner("GRAND LEDGER"), 18, FontStyle.Bold, RugTheme.GreenBright)
                .alignment = TextAnchor.MiddleCenter;
            NewText(panel, "RUGSOFT™ FISCAL TERMINAL — books on the left, levers on the right", 11, FontStyle.Italic, RugTheme.GreenDim)
                .alignment = TextAnchor.MiddleCenter;
            NewText(panel, "C:\\RUGS> RUN GL.EXE /MANAGE", 12, FontStyle.Normal, RugTheme.GreenDim)
                .alignment = TextAnchor.MiddleLeft;

            List<BuildingRegistration> owned = RugLaunder.AllOwned();
            float inTotal = 0f, washTotal = 0f, holding = 0f;

            if (owned.Count == 0)
            {
                NewText(panel, "NO BUSINESSES ON FILE.\nRent one, name it, let it trade — then the ledger has something to count.",
                        13, FontStyle.Italic, RugTheme.Green).alignment = TextAnchor.MiddleCenter;
            }
            else
            {
                // Column header + rule (same mono padding as the rows, so everything lines up).
                NewText(panel,
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
                        NewButton(row, "COLLECT", Green, 70f, () => { SetStatus(RugSidecars.Collect(r)); Rebuild(); });
                        NewButton(row, "CUT", Red, 46f, () =>
                        {
                            RugSidecars.Remove(r); // sweeps held cash into the stash
                            SetStatus($"rider cut loose at {Name(r)} — held cash swept, it's a FRONT again.");
                            Rebuild();
                        });
                    }
                    else
                    {
                        NewButton(row, "+RIDER", Green, 66f, () =>
                        {
                            RugSidecars.Attach(r);
                            SetStatus($"rider posted at {Name(r)} — earning starts tomorrow; it's out of the wash pool.");
                            Rebuild();
                        });
                    }
                }

                // Totals rule + row, then the balance verdict — THE endgame number.
                NewText(panel,
                        new string('─', 56) + "\n" +
                        "TOTALS".PadRight(27) + ("$" + inTotal.ToString("N0")).PadLeft(9) + ("$" + washTotal.ToString("N0")).PadLeft(12),
                        13, FontStyle.Bold, RugTheme.Green).alignment = TextAnchor.MiddleLeft;
                NewText(panel, Verdict(inTotal, washTotal), 13, FontStyle.Bold, RugTheme.Amber)
                    .alignment = TextAnchor.MiddleCenter;
            }

            // The lifetime ledger — income and washcome.
            var lt = new StringBuilder();
            lt.Append("LIFETIME   earned ".PadRight(19)).Append(("$" + RugBooks.EarnedLifetime.ToString("N0")).PadLeft(12))
              .Append("   washed ").Append(("$" + RugBooks.DeclaredLifetime.ToString("N0")).PadLeft(12)).Append('\n');
            lt.Append("ON HAND    stash  ".PadRight(19)).Append(("$" + RugBooks.Dirty.ToString("N0")).PadLeft(12))
              .Append("   riders ").Append(("$" + holding.ToString("N0")).PadLeft(12))
              .Append("   heat: ").Append(RugHeat.Band(RugBooks.Heat));
            NewText(panel, lt.ToString(), 13, FontStyle.Normal, RugTheme.Green).alignment = TextAnchor.MiddleLeft;

            // NIGHT CREW (auto-wash) — the automation switch, gated on fleet size.
            if (RugLaunder.AutoWashUnlocked())
            {
                bool on = RugLaunder.AutoWashEnabled;
                Transform row = ControlRow(panel, "NIGHT CREW",
                    on ? "washing the stash through every front, nightly" : "idle — washing is manual");
                NewButton(row, on ? "ON" : "OFF", on ? Green : Red, 56f, () =>
                {
                    RugLaunder.SetAutoWash(!RugLaunder.AutoWashEnabled);
                    SetStatus(RugLaunder.AutoWashEnabled ? "night crew hired — first run tonight." : "night crew stood down.");
                    Rebuild();
                });
            }
            else
            {
                NewText(panel,
                    $"NIGHT CREW: locked — automation takes a real fleet ({RugLaunder.AutoWashMinBusinesses}+ businesses; you run {owned.Count}).",
                    12, FontStyle.Italic, RugTheme.GreenDim).alignment = TextAnchor.MiddleCenter;
            }

            _status = NewText(panel, StatusLine(), 12, FontStyle.Normal, RugTheme.Amber);
            _status.alignment = TextAnchor.MiddleLeft;

            NewButton(panel, "←  Laundry", Slate, 0f, () => { Close(); RugLaunderUI.Open(); });
            NewButton(panel, "Close  (Esc)", Slate, 0f, Close);
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

        private static string StatusLine() => string.IsNullOrEmpty(_statusText) ? "> ready." : "> " + _statusText;

        private static void SetStatus(string s) { _statusText = s; if (_status != null) _status.text = StatusLine(); }

        private static void Rebuild()
        {
            if (_panel == null) return;
            for (int i = _panel.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_panel.GetChild(i).gameObject);
            _status = null;
            Build(_panel);
        }

        internal static void Close()
        {
            if (_root == null) return;
            UnityEngine.Object.Destroy(_root);
            _root = null;
            _panel = null;
            _status = null;
            BlockInput(false);
        }

        private static void BlockInput(bool on)
        {
            try
            {
                var pc = InstanceBehavior<GameManager>.Instance?.playerController;
                if (on)
                {
                    _prevLock = Cursor.lockState;
                    _prevCursor = Cursor.visible;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    pc?.SetNavigationBlocker(NavigationBlocker.SpecialEmployeeDialog);
                }
                else
                {
                    pc?.UnsetNavigationBlocker(NavigationBlocker.SpecialEmployeeDialog);
                    Cursor.lockState = _prevLock;
                    Cursor.visible = _prevCursor;
                }
            }
            catch (Exception e) { Debug.LogError("[RUGS!] ledger input block toggle failed: " + e); }
        }

        // ---- tiny uGUI helpers (mirrors RugLaunderUI) ----

        private static Image NewImage(Transform parent, Color color)
        {
            var go = new GameObject("img", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static Text NewText(Transform parent, string text, int size, FontStyle style, Color color)
        {
            var go = new GameObject("txt", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = UiFont();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.alignment = TextAnchor.MiddleLeft;
            return t;
        }

        // A ledger row: the padded-column mono text on the left, the row's control buttons on the right.
        private static Transform LedgerRow(Transform panel, string cols)
        {
            Image row = NewImage(panel, new Color(1f, 1f, 1f, 0.03f));
            row.gameObject.AddComponent<LayoutElement>().minHeight = 28f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(6, 6, 2, 2);
            hlg.spacing = 5;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            Text t = NewText(row.transform, cols, 13, FontStyle.Normal, RugTheme.Green);
            t.gameObject.AddComponent<LayoutElement>().preferredWidth = 440f;
            return row.transform;
        }

        // A labeled control row (e.g. the night-crew switch).
        private static Transform ControlRow(Transform panel, string name, string sub)
        {
            Image row = NewImage(panel, new Color(1f, 1f, 1f, 0.04f));
            row.gameObject.AddComponent<LayoutElement>().minHeight = 34f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 3, 3);
            hlg.spacing = 5;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            Text n = NewText(row.transform, name, 14, FontStyle.Bold, RugTheme.GreenBright);
            n.gameObject.AddComponent<LayoutElement>().preferredWidth = 130f;
            Text s = NewText(row.transform, sub, 12, FontStyle.Italic, RugTheme.GreenDim);
            s.gameObject.AddComponent<LayoutElement>().preferredWidth = 370f;
            return row.transform;
        }

        private static void NewButton(Transform parent, string label, Color color, float width, Action onClick)
        {
            var go = new GameObject("btn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 24f;
            if (width > 0f) le.preferredWidth = width;

            Text t = NewText(go.transform, label, 12, FontStyle.Bold, Color.white);
            t.alignment = TextAnchor.MiddleCenter;
            Stretch(t.rectTransform);
        }
    }
}
