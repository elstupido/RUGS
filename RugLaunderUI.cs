using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Rugs
{
    /// <summary>
    /// The laundering window — a standalone runtime-uGUI panel (its own Canvas, same approach as
    /// <see cref="RugDealUI"/>). Lists the legit businesses the player owns and lets them pick which one
    /// to wash dirty cash through. A wash fabricates a believable sale on that business's real books
    /// (<see cref="RugLaunder.Wash"/>); it clears clean + taxed at the next in-game midnight. Within a
    /// business's plausible daily capacity it's heat-free; pushing past that cooks the books and draws
    /// the IRS.
    ///
    /// Opened by the player's laundering appliance (bought + placed at home). Code-only, no Harmony.
    /// </summary>
    internal static class RugLaunderUI
    {
        private static GameObject _root;
        private static Text _status;
        private static Text _dirty;
        private static string _statusText = "";
        private static Font _font;
        private static CursorLockMode _prevLock;
        private static bool _prevCursor;

        internal static bool IsOpen => _root != null;

        private static readonly Color Gold = new Color(0.52f, 0.42f, 0.14f);

        private static Font UiFont()
        {
            if (_font == null) _font = RugTheme.Mono(); // monospace terminal font (RugTheme handles the fallback)
            return _font;
        }

        internal static void Open()
        {
            Close();
            _statusText = "";
            try
            {
                _root = new GameObject("RugLaunderUI");
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30000; // above the game's UI
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

                Image panel = NewImage(_root.transform, new Color(0.08f, 0.07f, 0.06f, 0.98f));
                // Scrollable, height-capped: a 30-business fleet must never push the buttons off-screen.
                Transform content = RugUi.MakeScrollable(panel, 640f, new RectOffset(16, 16, 12, 14), 5f);
                Build(content);
                RugUi.Fit(content);
                BlockInput(true);
            }
            catch (Exception e)
            {
                Debug.LogError("[RUGS!] laundering panel failed to open: " + e);
                Close();
            }
        }

        private static void Build(Transform panel)
        {
            NewText(panel, RugTheme.Banner("LAUNDERING"), 18, FontStyle.Bold, RugTheme.GreenBright)
                .alignment = TextAnchor.MiddleCenter;
            NewText(panel, "turn dirty cash clean — fast for a cut, or cheap through your own businesses", 12, FontStyle.Italic, new Color(0.7f, 0.7f, 0.7f))
                .alignment = TextAnchor.MiddleCenter;

            _dirty = NewText(panel, DirtyText(), 14, FontStyle.Bold, new Color(0.85f, 0.62f, 0.55f));
            _dirty.alignment = TextAnchor.MiddleCenter;

            // Quick wash — instant, any amount, a flat cut. The always-available release valve.
            if (RugBooks.Dirty > 0f)
            {
                Section(panel, $"QUICK WASH  ·  instant  ·  −{RugLaunder.DealerVig * 100f:0}% cut");
                Transform qr = Row(panel, "wash dirty cash now", "any amount");
                NewButton(qr, "$10k", Gold, 50f, () => DoQuickWash(10000f));
                NewButton(qr, "Max",  Gold, 50f, () => DoQuickWash(float.MaxValue));
            }

            List<BuildingRegistration> fronts = RugLaunder.Fronts();
            if (fronts.Count == 0)
            {
                NewText(panel, "You don't own a business to wash through.\nRent one, give it a name, and let it build up some real sales first.",
                        13, FontStyle.Italic, new Color(0.8f, 0.8f, 0.8f)).alignment = TextAnchor.MiddleCenter;
            }
            else
            {
                Section(panel, "YOUR BUSINESSES");
                foreach (BuildingRegistration reg in fronts)
                {
                    BuildingRegistration r = reg; // capture
                    float cap = RugLaunder.CapRemaining(r);
                    string name = string.IsNullOrWhiteSpace(r.BusinessName) ? "(unnamed)" : r.BusinessName;
                    if (name.Length > 22) name = name.Substring(0, 21) + "…"; // keep it inside its column
                    // The sub column is ~150px and overflow renders UNDER the buttons (seen live in neriku's
                    // screenshot), so every variant stays short. The quick-wash hint lives in the QUICK WASH
                    // section at the top of this same panel — no need to repeat it per row.
                    float boost = RugFactoryBoost.For(r);
                    string sub = boost >= 0.005f
                        ? (cap >= 1f ? $"wash ${cap:N0}/day · fed +{boost * 100f:0}%" : $"tapped out · fed +{boost * 100f:0}%")
                        : (cap >= 1f ? $"can wash ${cap:N0} today" : "tapped out today");
                    Transform row = Row(panel, name, sub);
                    NewButton(row, "$1k",  Gold, 44f, () => DoWash(r, 1000f));
                    NewButton(row, "$10k", Gold, 50f, () => DoWash(r, 10000f));
                    NewButton(row, "Safe", Gold, 50f, () => DoWash(r, RugLaunder.CapRemaining(r)));
                }
            }

            // DEV scroll stress: synthetic rows (F10 cycles the count; always 0 in release builds).
            for (int i = 0; i < RugsConfig.UiStressRows; i++)
            {
                Transform srow = Row(panel, $"STRESS BIZ #{i + 1:00}", "can wash $12,345 today");
                NewButton(srow, "$1k",  Gold, 44f, () => { });
                NewButton(srow, "$10k", Gold, 50f, () => { });
                NewButton(srow, "Safe", Gold, 50f, () => { });
            }

            NewText(panel, "clears clean overnight  ·  booked as that business's income  ·  taxed",
                    11, FontStyle.Italic, new Color(0.6f, 0.6f, 0.6f)).alignment = TextAnchor.MiddleCenter;

            _status = NewText(panel, _statusText, 13, FontStyle.Italic, new Color(0.9f, 0.82f, 0.5f));
            _status.alignment = TextAnchor.MiddleCenter;

            // The GL is THE ops console — businesses, roles, riders (hire/collect/cut), totals, night crew.
            // (The old separate "Manage Dealers" panel was a strict subset of it and was retired in 1.5.2.)
            NewButton(panel, "The GL  (Grand Ledger)  →", new Color(0.18f, 0.30f, 0.34f), 0f, () => { Close(); RugLedgerUI.Open(); });
            NewButton(panel, "Close  (Esc)", new Color(0.30f, 0.25f, 0.18f), 0f, Close);
        }

        private static void DoWash(BuildingRegistration reg, float amount)
        {
            if (amount < 1f) { SetStatus("That business is tapped out for safe washing today."); return; }
            SetStatus(RugLaunder.Wash(reg, amount));
            Refresh();
        }

        private static void DoQuickWash(float amount) { SetStatus(RugLaunder.WashViaDealer(amount)); Refresh(); }


        private static string DirtyText() => $"Dirty cash on hand: ${RugBooks.Dirty:N0}";

        // The per-business capacity labels are built once; the live CapRemaining is re-read at click time
        // (so washes stay correct), and the labels refresh on the next open. Here we keep the dirty-cash
        // readout and status live after each wash.
        private static void Refresh()
        {
            if (_dirty != null) _dirty.text = DirtyText();
        }

        private static void Section(Transform panel, string label)
        {
            NewText(panel, label, 12, FontStyle.Bold, new Color(0.75f, 0.7f, 0.5f)).alignment = TextAnchor.MiddleCenter;
        }

        private static Transform Row(Transform panel, string name, string sub)
        {
            Image row = NewImage(panel, new Color(1f, 1f, 1f, 0.04f));
            row.gameObject.AddComponent<LayoutElement>().minHeight = 36f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 3, 3);
            hlg.spacing = 5;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            AddText(row.transform, name, 15, FontStyle.Bold, Color.white, 260f);
            AddText(row.transform, sub, 12, FontStyle.Italic, new Color(0.7f, 0.85f, 0.7f), 150f);
            return row.transform;
        }

        private static void SetStatus(string s) { _statusText = s; if (_status != null) _status.text = s; }

        internal static void Close()
        {
            if (_root == null) return;
            UnityEngine.Object.Destroy(_root);
            _root = null;
            _status = null;
            _dirty = null;
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
            catch (Exception e) { Debug.LogError("[RUGS!] launder input block toggle failed: " + e); }
        }

        // ---- tiny uGUI helpers (mirrors RugDealUI) ----

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

        private static void AddText(Transform parent, string text, int size, FontStyle style, Color color, float width)
        {
            Text t = NewText(parent, text, size, style, color);
            t.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
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
            le.minHeight = 30f;
            if (width > 0f) le.preferredWidth = width;

            Text t = NewText(go.transform, label, 13, FontStyle.Bold, Color.white);
            t.alignment = TextAnchor.MiddleCenter;
            Stretch(t.rectTransform);
        }
    }
}
