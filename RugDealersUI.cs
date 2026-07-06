using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Rugs
{
    /// <summary>
    /// "Your Dealers" — the T2 management panel (standalone runtime-uGUI, mirrors <see cref="RugLaunderUI"/>).
    /// Lists the businesses the player owns and lets them bolt a DEALER SIDECAR onto one: an earner that mints
    /// dirty cash off the front's real activity (see <see cref="RugSidecars"/>), then COLLECT it into the stash.
    /// A sidecar'd business is locked out of laundering. Reached from the home computer via the laundry panel's
    /// "Manage Dealers" door. Code-only, no Harmony.
    /// </summary>
    internal static class RugDealersUI
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

        internal static void Open()
        {
            Close();
            _statusText = "";
            try
            {
                _root = new GameObject("RugDealersUI");
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

                Image panel = NewImage(_root.transform, new Color(0.08f, 0.07f, 0.06f, 0.98f));
                // Scrollable, height-capped: a 30-business fleet must never push the buttons off-screen.
                _panel = RugUi.MakeScrollable(panel, 640f, new RectOffset(16, 16, 12, 14), 5f);
                Build(_panel);
                RugUi.Fit(_panel);
                BlockInput(true);
            }
            catch (Exception e)
            {
                Debug.LogError("[RUGS!] dealers panel failed to open: " + e);
                Close();
            }
        }

        private static void Build(Transform panel)
        {
            NewText(panel, RugTheme.Banner("YOUR DEALERS"), 18, FontStyle.Bold, RugTheme.GreenBright)
                .alignment = TextAnchor.MiddleCenter;
            NewText(panel, "put a dealer in the back of a business you own — it earns dirty cash off the cover", 12, FontStyle.Italic, new Color(0.7f, 0.7f, 0.7f))
                .alignment = TextAnchor.MiddleCenter;

            List<BuildingRegistration> owned = RugLaunder.AllOwned();
            if (owned.Count == 0)
            {
                NewText(panel, "You don't own a business yet.\nRent one, give it a name, and let it build up some real sales first.",
                        13, FontStyle.Italic, new Color(0.8f, 0.8f, 0.8f)).alignment = TextAnchor.MiddleCenter;
            }
            else
            {
                Section(panel, "YOUR BUSINESSES");
                foreach (BuildingRegistration reg in owned)
                {
                    BuildingRegistration r = reg; // capture for the closures
                    string name = string.IsNullOrWhiteSpace(r.BusinessName) ? "(unnamed)" : r.BusinessName;
                    if (name.Length > 22) name = name.Substring(0, 21) + "…";
                    // Boosted rows use a COMPACT form: the sub column is ~170px and overflow renders under the
                    // buttons, so the factory tag replaces filler words instead of extending the line.
                    float boost = RugFactoryBoost.For(r);
                    string fed = boost >= 0.005f ? $" · fed +{boost * 100f:0}%" : ""; // supplied by your factory

                    if (RugSidecars.HasSidecar(RugLaunder.Key(r)))
                    {
                        float held = RugSidecars.Held(r);
                        string sub = fed.Length > 0
                            ? (held >= 1f ? $"holds ${held:N0}{fed}" : $"nothing yet{fed}")
                            : (held >= 1f ? $"dealer working · holding ${held:N0}" : "dealer working · nothing yet");
                        Transform row = Row(panel, name, sub);
                        NewButton(row, "Collect", Green, 64f, () => DoCollect(r));
                        NewButton(row, "Fire",    Red,   46f, () => DoRemove(r));
                    }
                    else
                    {
                        float rev = RugLaunder.BelievableRevenue(r);
                        string sub = (rev >= 1f ? $"~${rev:N0}/day cover" : "no real sales yet") + fed;
                        Transform row = Row(panel, name, sub);
                        NewButton(row, "Hire dealer", Green, 92f, () => DoAttach(r));
                    }
                }
                NewText(panel, "a dealer mints dirty cash daily from the front's real takings · collect it, then launder it elsewhere",
                        11, FontStyle.Italic, new Color(0.6f, 0.6f, 0.6f)).alignment = TextAnchor.MiddleCenter;
            }

            _status = NewText(panel, _statusText, 13, FontStyle.Italic, new Color(0.9f, 0.82f, 0.5f));
            _status.alignment = TextAnchor.MiddleCenter;

            NewButton(panel, "←  Back to Laundry", Slate, 0f, () => { Close(); RugLaunderUI.Open(); });
            NewButton(panel, "Close  (Esc)", Slate, 0f, Close);
        }

        private static void DoAttach(BuildingRegistration reg)
        {
            RugSidecars.Attach(reg);
            SetStatus($"Put a dealer on {Name(reg)}. He starts moving product tomorrow.");
            Rebuild();
        }

        private static void DoRemove(BuildingRegistration reg)
        {
            string n = Name(reg);
            RugSidecars.Remove(reg);
            SetStatus($"Pulled your dealer off {n}. (Anything he was holding went to the stash.)");
            Rebuild();
        }

        private static void DoCollect(BuildingRegistration reg)
        {
            SetStatus(RugSidecars.Collect(reg));
            Rebuild();
        }

        private static string Name(BuildingRegistration reg)
            => string.IsNullOrWhiteSpace(reg.BusinessName) ? "that place" : reg.BusinessName;

        // Rebuild the panel contents in place after an action (mirrors RugDealUI's destroy-children-then-Build).
        private static void Rebuild()
        {
            if (_panel == null) return;
            for (int i = _panel.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_panel.GetChild(i).gameObject);
            _status = null;
            Build(_panel);
            RugUi.Fit(_panel);
        }

        private static void SetStatus(string s) { _statusText = s; if (_status != null) _status.text = s; }

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
            catch (Exception e) { Debug.LogError("[RUGS!] dealers input block toggle failed: " + e); }
        }

        // ---- tiny uGUI helpers (mirrors RugLaunderUI) ----

        private static Font UiFont()
        {
            if (_font == null) _font = RugTheme.Mono(); // monospace terminal font (RugTheme handles the fallback)
            return _font;
        }

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

            AddText(row.transform, name, 15, FontStyle.Bold, Color.white, 250f);
            AddText(row.transform, sub, 12, FontStyle.Italic, new Color(0.7f, 0.85f, 0.7f), 170f);
            return row.transform;
        }
    }
}
