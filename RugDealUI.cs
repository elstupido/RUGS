using System;
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
        private static RugDealerController _dealer;
        private static Text _status;
        private static Text _carry;
        private static Text _cash;
        private static string _statusText = "";
        private static Font _font;
        private static CursorLockMode _prevLock;
        private static bool _prevCursor;

        internal static bool IsOpen => _root != null;

        private static Font UiFont()
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }

        internal static void Open(RugDealerController dealer)
        {
            Close();
            _dealer = dealer;
            _statusText = "";
            try
            {
                _root = new GameObject("RugDealUI");
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30000; // above the game's UI
                var scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                _root.AddComponent<GraphicRaycaster>();

                Image backdrop = NewImage(_root.transform, new Color(0f, 0f, 0f, 0.6f));
                Stretch(backdrop.rectTransform);

                Image panel = NewImage(_root.transform, new Color(0.08f, 0.07f, 0.06f, 0.98f));
                RectTransform prt = panel.rectTransform;
                prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
                prt.sizeDelta = new Vector2(700f, 100f);
                var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(24, 24, 20, 24);
                vlg.spacing = 12;
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.childControlWidth = vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
                var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

                Build(panel.transform);
                BlockInput(true);
                Debug.Log("[RUGS!] deal panel opened for " + dealer.Name);
            }
            catch (Exception e)
            {
                Debug.LogError("[RUGS!] deal panel failed to open: " + e);
                Close();
            }
        }

        private static readonly Color Green = new Color(0.20f, 0.45f, 0.22f);
        private static readonly Color Blue = new Color(0.25f, 0.35f, 0.55f);

        private static void Build(Transform panel)
        {
            NewText(panel, (_dealer.Name + "'s Corner").ToUpperInvariant(), 30, FontStyle.Bold, new Color(0.95f, 0.85f, 0.35f))
                .alignment = TextAnchor.MiddleCenter;

            _cash = NewText(panel, CashText(), 16, FontStyle.Bold, new Color(0.6f, 0.9f, 0.6f));
            _cash.alignment = TextAnchor.MiddleCenter;

            _carry = NewText(panel, CarryText(), 16, FontStyle.Italic, new Color(0.75f, 0.85f, 0.95f));
            _carry.alignment = TextAnchor.MiddleCenter;

            if (_dealer.Sells.Length > 0)
            {
                Section(panel, "HE'S SELLING");
                foreach (RugDef rug in _dealer.Sells)
                {
                    RugDef r = rug;
                    Transform row = Row(panel, r.Display, $"${RugTrading.BuyPrice(_dealer, r):N0}/ea");
                    NewButton(row, "x1",   Green, 56f, () => DoBuy(r, 1));
                    NewButton(row, "x10",  Green, 56f, () => DoBuy(r, 10));
                    NewButton(row, "x100", Green, 64f, () => DoBuy(r, 100));
                    NewButton(row, "Max",  Green, 64f, () => DoBuy(r, RugTrading.MaxAffordable(_dealer, r)));
                }
            }

            if (_dealer.Buys.Length > 0)
            {
                Section(panel, "HE'S BUYING");
                foreach (RugDef rug in _dealer.Buys)
                {
                    RugDef r = rug;
                    Transform row = Row(panel, r.Display, $"${RugTrading.SellPrice(_dealer, r):N0}/ea");
                    NewButton(row, "Sell All", Blue, 130f, () => DoSell(r));
                }
            }

            _status = NewText(panel, _statusText, 16, FontStyle.Italic, new Color(0.9f, 0.82f, 0.5f));
            _status.alignment = TextAnchor.MiddleCenter;

            NewButton(panel, "Leave", new Color(0.40f, 0.20f, 0.20f), 0f, Close);
        }

        private static void DoBuy(RugDef r, int qty) { SetStatus(RugTrading.Buy(_dealer, r, qty)); Refresh(); }
        private static void DoSell(RugDef r) { SetStatus(RugTrading.SellAll(_dealer, r)); Refresh(); }

        private static string CarryText()
        {
            RugDef held = RugTrading.HeldRug();
            return held == null ? "Carrying: nothing" : $"Carrying: {held.Display} x{RugTrading.HeldAmount():N0}";
        }

        private static string CashText() => $"Cash: ${SaveGameManager.Current.Money:N0}";

        private static void Refresh()
        {
            if (_carry != null) _carry.text = CarryText();
            if (_cash != null) _cash.text = CashText();
        }

        private static void Section(Transform panel, string label)
        {
            NewText(panel, label, 15, FontStyle.Bold, new Color(0.75f, 0.7f, 0.5f)).alignment = TextAnchor.MiddleCenter;
        }

        private static Transform Row(Transform panel, string name, string price)
        {
            Image row = NewImage(panel, new Color(1f, 1f, 1f, 0.04f));
            row.gameObject.AddComponent<LayoutElement>().minHeight = 54f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(12, 12, 6, 6);
            hlg.spacing = 8;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            AddText(row.transform, name, 22, FontStyle.Bold, Color.white, 150f);
            AddText(row.transform, price, 18, FontStyle.Normal, new Color(0.6f, 0.9f, 0.6f), 95f);
            return row.transform;
        }

        private static void SetStatus(string s) { _statusText = s; if (_status != null) _status.text = s; }

        internal static void Close()
        {
            if (_root == null) return;
            UnityEngine.Object.Destroy(_root);
            _root = null;
            _dealer = null;
            _status = null;
            _carry = null;
            _cash = null;
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
            le.minHeight = 44f;
            if (width > 0f) le.preferredWidth = width;

            Text t = NewText(go.transform, label, 18, FontStyle.Bold, Color.white);
            t.alignment = TextAnchor.MiddleCenter;
            Stretch(t.rectTransform);
        }
    }
}
