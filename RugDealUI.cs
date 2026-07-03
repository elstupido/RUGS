using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Rugs
{
    /// <summary>
    /// A custom runtime-built "street deal" panel (uGUI). Avoids the game's phone-styled
    /// dialog entirely. Shows the dealer's rugs at live prices with Buy buttons.
    /// Selling is added next; this build proves the panel renders and is clickable.
    /// </summary>
    internal static class RugDealUI
    {
        private static GameObject _root;
        private static Transform _panel; // the content panel (so an arrival event can hand off to the deal)
        private static RugEvents.Arrival _arrival; // pending arrival event awaiting the player's acknowledge
        private static RugDealerController _dealer;
        private static Text _status;
        private static Text _carry;
        private static Text _cash;
        private static Text _book;
        private static string _statusText = "";
        private static Font _font;
        private static CursorLockMode _prevLock;
        private static bool _prevCursor;

        internal static bool IsOpen => _root != null;

        private static Font UiFont()
        {
            if (_font == null) _font = RugTheme.Mono(); // monospace terminal font (RugTheme handles the fallback)
            return _font;
        }

        internal static void Open(RugDealerController dealer)
        {
            Close();
            _dealer = dealer;
            _statusText = "";
            // Heal the "phantom hands" case: if the player walks up holding our now-EMPTY box (left over
            // from selling out under an earlier build), drop it so it stops blocking object pickups.
            RugInventory.DropEmptyHeldBox();
            try
            {
                _root = new GameObject("RugDealUI");
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30000; // above the game's UI
                var scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f; // match HEIGHT so the panel always fits vertically
                _root.AddComponent<GraphicRaycaster>();

                // Dim backdrop; clicking it (anywhere outside the panel) closes the deal — unless a street
                // moment is pending (RequestClose): you can't dodge the muscle or the hospital by clicking away.
                Image backdrop = NewImage(_root.transform, new Color(0f, 0f, 0f, 0.6f));
                Stretch(backdrop.rectTransform);
                var bdBtn = backdrop.gameObject.AddComponent<Button>();
                bdBtn.transition = Selectable.Transition.None;
                bdBtn.onClick.AddListener(RequestClose);

                Image panel = NewImage(_root.transform, new Color(0.08f, 0.07f, 0.06f, 0.98f));
                RectTransform prt = panel.rectTransform;
                prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
                prt.sizeDelta = new Vector2(540f, 100f);
                var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(16, 16, 12, 14);
                vlg.spacing = 5;
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.childControlWidth = vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
                var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

                _panel = panel.transform;
                // The first dealer the player ever opens fronts a free starter stash (T0.1); after that, the
                // Drug-Wars "arrival" beat may trigger an event. Either way, announce it first, then let the
                // player continue to the buy/sell panel.
                RugEvents.Arrival arrival = RugFreebie.TryFirstFree(_dealer) ?? RugEvents.RollDealerArrival(_dealer.Neighborhood);
                if (arrival != null) BuildArrival(arrival);
                else Build(_panel);
                BlockInput(true);
            }
            catch (Exception e)
            {
                Debug.LogError("[RUGS!] deal panel failed to open: " + e);
                Close();
            }
        }

        // The arrival-event screen: what happened on the way in, then a button to acknowledge it.
        private static void BuildArrival(RugEvents.Arrival a)
        {
            if (_panel == null) return;
            _arrival = a;
            NewText(_panel, RugTheme.Banner((_dealer.Name + "'s Corner").ToUpperInvariant()), 18, FontStyle.Bold, RugTheme.GreenBright)
                .alignment = TextAnchor.MiddleCenter;
            NewText(_panel, "— on your way in —", 12, FontStyle.Italic, new Color(0.7f, 0.7f, 0.7f))
                .alignment = TextAnchor.MiddleCenter;
            Text body = NewText(_panel, a.message, 16, FontStyle.Bold, new Color(0.9f, 0.82f, 0.5f));
            body.alignment = TextAnchor.MiddleCenter;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;

            if (a.choices != null && a.choices.Count > 0)
            {
                // Interactive arrival — one button per branch. Picking one runs its effect, then shows the outcome.
                foreach (RugEvents.Choice choice in a.choices)
                {
                    RugEvents.Choice c = choice; // capture for the closure
                    NewButton(_panel, c.label, Blue, 0f, () => PickChoice(c));
                }
            }
            else
            {
                NewButton(_panel, a.ends ? "OK" : "Continue  →", Green, 0f, ContinueToDeal);
            }
        }

        // Player picked a branch: run its effect, take the OUTCOME screen it returns, and render that (its own
        // Continue/OK then routes through ContinueToDeal). A null outcome just flows straight on to the deal.
        private static void PickChoice(RugEvents.Choice c)
        {
            RugEvents.Arrival outcome = null;
            try { outcome = c?.resolve?.Invoke(); }
            catch (Exception e) { Debug.LogError("[RUGS!] choice resolve failed: " + e); }
            if (_panel == null) return;
            for (int i = _panel.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_panel.GetChild(i).gameObject);
            if (outcome == null) { ContinueToDeal(); return; }
            BuildArrival(outcome);
        }

        // Acknowledge the arrival event: run any deferred effect (e.g. the hospital teleport), then either
        // close (events that whisk you away) or flip to the normal buy/sell deal.
        private static void ContinueToDeal()
        {
            RugEvents.Arrival a = _arrival;
            _arrival = null;
            try { a?.onContinue?.Invoke(); } catch (Exception e) { Debug.LogError("[RUGS!] arrival continue failed: " + e); }
            if (a != null && a.ends) { Close(); return; }      // e.g. hospital — whisked away, no deal
            if (_panel == null) return;
            for (int i = _panel.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_panel.GetChild(i).gameObject);
            _statusText = "";
            Build(_panel);
        }

        private static readonly Color Green = new Color(0.20f, 0.45f, 0.22f);
        private static readonly Color Blue = new Color(0.25f, 0.35f, 0.55f);

        private static void Build(Transform panel)
        {
            NewText(panel, RugTheme.Banner((_dealer.Name + "'s Corner").ToUpperInvariant()), 18, FontStyle.Bold, RugTheme.GreenBright)
                .alignment = TextAnchor.MiddleCenter;

            if (!string.IsNullOrEmpty(_dealer.Neighborhood))
                NewText(panel, Localizor.LocalizorManager.GetLocalization(_dealer.Neighborhood) + (_dealer.IsAnchor ? " · your corner" : " · street prices"),
                        12, FontStyle.Italic, new Color(0.7f, 0.7f, 0.7f)).alignment = TextAnchor.MiddleCenter;

            _cash = NewText(panel, CashText(), 14, FontStyle.Bold, new Color(0.6f, 0.9f, 0.6f));
            _cash.alignment = TextAnchor.MiddleCenter;

            _carry = NewText(panel, CarryText(), 13, FontStyle.Italic, new Color(0.75f, 0.85f, 0.95f));
            _carry.alignment = TextAnchor.MiddleCenter;

            _book = NewText(panel, BookText(), 11, FontStyle.Italic, new Color(0.85f, 0.62f, 0.55f));
            _book.alignment = TextAnchor.MiddleCenter;

            if (_dealer.Sells.Length > 0)
            {
                Section(panel, "BUY FROM HIM");
                foreach (RugDef rug in _dealer.Sells)
                {
                    RugDef r = rug;
                    Transform row = Row(panel, r.Display, $"${RugTrading.BuyPrice(_dealer, r):N0}");
                    NewButton(row, "x1",   Green, 42f, () => DoBuy(r, 1));
                    NewButton(row, "x10",  Green, 46f, () => DoBuy(r, 10));
                    NewButton(row, "x100", Green, 52f, () => DoBuy(r, 100));
                    NewButton(row, "Max",  Green, 50f, () => DoBuy(r, RugTrading.MaxAffordable(_dealer, r)));
                }
            }

            if (_dealer.Buys.Length > 0)
            {
                Section(panel, "SELL TO HIM");
                foreach (RugDef rug in _dealer.Buys)
                {
                    RugDef r = rug;
                    Transform row = Row(panel, r.Display, $"${RugTrading.SellPrice(_dealer, r):N0}");
                    NewButton(row, "x10",  Blue, 46f, () => DoSell(r, 10));
                    NewButton(row, "x100", Blue, 52f, () => DoSell(r, 100));
                    NewButton(row, "Max",  Blue, 50f, () => DoSell(r, RugInventory.TotalOf(r)));
                }
            }

            // (Laundering lives in its own menu now — open it from the computer in your apartment.)

            _status = NewText(panel, _statusText, 13, FontStyle.Italic, new Color(0.9f, 0.82f, 0.5f));
            _status.alignment = TextAnchor.MiddleCenter;

            NewButton(panel, "Leave  (Esc)", new Color(0.40f, 0.20f, 0.20f), 0f, Close);
        }

        private static void DoBuy(RugDef r, int qty) { SetStatus(RugTrading.Buy(_dealer, r, qty)); Refresh(); }
        private static void DoSell(RugDef r, int qty) { SetStatus(RugTrading.Sell(_dealer, r, qty)); Refresh(); }

        private static string CarryText()
        {
            List<(RugDef rug, int amount)> carried = RugInventory.Carried();
            if (carried.Count == 0) return "Carrying: nothing";
            var parts = new List<string>();
            foreach ((RugDef rug, int amount) in carried) parts.Add($"{rug.Display} x{amount:N0}");
            return "Carrying: " + string.Join(", ", parts);
        }

        private static string CashText() => $"Cash: ${SaveGameManager.Current.Money:N0}";

        private static string BookText() => $"Dirty cash: ${RugBooks.Dirty:N0}   ·   Heat: {RugHeat.Band(RugBooks.Heat)}";

        private static void Refresh()
        {
            if (_carry != null) _carry.text = CarryText();
            if (_cash != null) _cash.text = CashText();
            if (_book != null) _book.text = BookText();
        }

        private static void Section(Transform panel, string label)
        {
            NewText(panel, label, 12, FontStyle.Bold, new Color(0.75f, 0.7f, 0.5f)).alignment = TextAnchor.MiddleCenter;
        }

        private static Transform Row(Transform panel, string name, string price)
        {
            Image row = NewImage(panel, new Color(1f, 1f, 1f, 0.04f));
            row.gameObject.AddComponent<LayoutElement>().minHeight = 36f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 3, 3);
            hlg.spacing = 5;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            AddText(row.transform, name,  15, FontStyle.Bold, Color.white, 96f);
            AddText(row.transform, price, 13, FontStyle.Normal, new Color(0.6f, 0.9f, 0.6f), 60f);
            return row.transform;
        }

        private static void SetStatus(string s) { _statusText = s; if (_status != null) _status.text = s; }

        /// <summary>
        /// Player-initiated close (Esc / backdrop). Refused while an arrival event is pending — its effects
        /// resolve through its own buttons, so closing early would dodge the consequence (the hospital
        /// teleport, the muscle's cut). Once the arrival is acknowledged, closes as normal.
        /// </summary>
        internal static void RequestClose() { if (_arrival == null) Close(); }

        /// <summary>
        /// Called every tick with the current in-game day. If midnight passed while the deal screen is open,
        /// rebuild it: prices re-roll at day change (RugMarket.SyncDay), and clicking a button quotes LIVE —
        /// stale rows would silently charge a different price than they show. An arrival screen is left
        /// alone (it re-renders into fresh prices via ContinueToDeal anyway).
        /// </summary>
        internal static void OnDayTick(int day)
        {
            if (_root == null || _panel == null || day == _builtDay) return;
            bool stale = _builtDay >= 0 && _arrival == null;
            _builtDay = day;
            if (!stale) return;
            for (int i = _panel.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_panel.GetChild(i).gameObject);
            _statusText = "midnight — the corners re-rolled their prices";
            Build(_panel);
        }

        private static int _builtDay = -1;

        internal static void Close()
        {
            if (_root == null) return;
            UnityEngine.Object.Destroy(_root);
            _root = null;
            _panel = null;
            _arrival = null;
            _builtDay = -1;
            _dealer = null;
            _status = null;
            _carry = null;
            _cash = null;
            _book = null;
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
            catch (Exception e) { Debug.LogError("[RUGS!] input block toggle failed: " + e); }
        }

        // ---- tiny uGUI helpers ----

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
