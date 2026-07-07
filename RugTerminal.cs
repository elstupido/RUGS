using System;
using UnityEngine;
using UnityEngine.UI;

namespace Rugs
{
    /// <summary>
    /// CROOKBOOKS™ by RUGSOFT — the laundering computer's one and only program (the v1.6 overhaul that
    /// unified the old LAUNDERING and GRAND LEDGER panels). Deliberately paced like a late-80s machine:
    /// click the computer → a short type-on BOOT (any key skips) → a numbered MAIN MENU (clickable AND
    /// number-key driven) → full screens. Esc backs out one level at a time: screen → menu → power off.
    ///
    ///   [1] THE BOOKS — businesses, riders, totals, the balance verdict, the night crew (RugBooksScreen)
    ///   [2] THE WASH  — quick wash, wash-all-safe, per-front washing (RugWashScreen)
    ///   [3] EXIT
    ///
    /// This class owns the WINDOW: canvas/backdrop/CRT panel (via RugUi's capped scroll shell), the boot
    /// sequence, the menu, the shared GLANCE STRIP (stash · heat · wash room — always one look away), the
    /// status line, and keyboard navigation via a driver component that only lives while the terminal is
    /// open. The screens are content builders that render into the panel and call back for status/refresh.
    /// Future screens (supply lines' THE CONNECT) get the next menu number. Code-only, no Harmony.
    /// </summary>
    internal static class RugTerminal
    {
        internal enum Screen { Boot, Menu, Books, Wash }

        private const float BootCharsPerSec = 300f;  // type-on speed (whole boot ≈ 0.7s)
        private const float BootHoldSecs    = 0.35f; // pause on the completed POST before the menu
        private const float BootSkipGrace   = 0.20f; // ignore the opening click for this long

        private static GameObject _root;
        private static Transform _content;
        private static Text _status;
        private static string _statusText = "";
        private static Screen _screen = Screen.Boot;
        private static CursorLockMode _prevLock;
        private static bool _prevCursor;

        // Boot type-on state.
        private static Text _bootText;
        private static string _bootFull = "";
        private static float _bootChars;
        private static float _bootHold;
        private static float _openedAt;

        internal static bool IsOpen => _root != null;

        private static readonly Color Slate = new Color(0.30f, 0.25f, 0.18f);

        internal static void Open()
        {
            Close();
            _statusText = "";
            try
            {
                _root = new GameObject("RugTerminal");
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30000; // above the game's UI
                var scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f;
                _root.AddComponent<GraphicRaycaster>();
                _root.AddComponent<RugTerminalDriver>(); // keyboard + boot type-on, alive only while open

                // Dim backdrop; clicking outside the panel backs out one level (screen → menu → off).
                Image backdrop = RugUi.NewImage(_root.transform, new Color(0f, 0f, 0f, 0.6f));
                RugUi.Stretch(backdrop.rectTransform);
                var bdBtn = backdrop.gameObject.AddComponent<Button>();
                bdBtn.transition = Selectable.Transition.None;
                bdBtn.onClick.AddListener(Back);

                Image panel = RugUi.NewImage(_root.transform, new Color(0.05f, 0.07f, 0.05f, 0.98f)); // CRT green-black
                _content = RugUi.MakeScrollable(panel, 700f, new RectOffset(18, 18, 12, 14), 4f);
                RugCrt.AddGlass(panel); // scanlines + vignette + flicker over the whole tube (last child → on top)

                _openedAt = Time.unscaledTime;
                _screen = Screen.Boot;
                Rebuild();
                BlockInput(true);
            }
            catch (Exception e)
            {
                Debug.LogError("[RUGS!] terminal failed to boot: " + e);
                Close();
            }
        }

        // ---- navigation ----

        internal static void Show(Screen s)
        {
            _screen = s;
            Rebuild();
        }

        /// <summary>Back out one level: screen → menu; menu/boot → power off.</summary>
        internal static void Back()
        {
            if (_screen == Screen.Books || _screen == Screen.Wash) Show(Screen.Menu);
            else Close();
        }

        /// <summary>Screens call this after an action so the whole terminal (glance strip included) recomputes.</summary>
        internal static void Refresh() => Rebuild();

        internal static void SetStatus(string s)
        {
            _statusText = s ?? "";
            if (_status != null) _status.text = StatusLine();
        }

        private static string StatusLine() => string.IsNullOrEmpty(_statusText) ? "> ready." : "> " + _statusText;

        // ---- build ----

        private static void Rebuild()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
            _status = null;
            _bootText = null;
            switch (_screen)
            {
                case Screen.Boot:  BuildBoot();  break;
                case Screen.Menu:  BuildMenu();  break;
                case Screen.Books: BuildScreen("THE BOOKS", RugBooksScreen.Build); break;
                case Screen.Wash:  BuildScreen("THE WASH",  RugWashScreen.Build);  break;
            }
            RugUi.Fit(_content);
        }

        private static void BuildBoot()
        {
            _bootFull =
                "RUGSOFT(R) CROOKBOOKS(TM) v" + RugsConfig.Version + "\n" +
                "(C) 1989 RUGSOFT CORP — ALL RIGHTS DENIED\n" +
                "\n" +
                "640K RAM ................. OK\n" +
                "TWO SETS OF BOOKS ........ FOUND\n" +
                "HEAT SHIELD .............. NOMINAL\n" +
                "\n" +
                "C:\\RUGS> RUN CROOKBOOKS.EXE_";
            _bootChars = 0f;
            _bootHold = 0f;
            _bootText = RugUi.NewText(_content, RevealBoot(0), 14, FontStyle.Normal, RugTheme.Green);
            _bootText.supportRichText = true; // the un-revealed tail rides along invisibly → constant layout size
        }

        // The revealed prefix + the remainder in fully transparent rich text, so the panel never resizes mid-boot.
        private static string RevealBoot(int chars)
        {
            chars = Mathf.Clamp(chars, 0, _bootFull.Length);
            return _bootFull.Substring(0, chars) + "<color=#00000000>" + _bootFull.Substring(chars) + "</color>";
        }

        private static void BuildMenu()
        {
            RugUi.NewText(_content, RugTheme.Banner("CROOKBOOKS™"), 18, FontStyle.Bold, RugTheme.GreenBright)
                .alignment = TextAnchor.MiddleCenter;
            RugUi.NewText(_content, $"by RUGSOFT · v{RugsConfig.Version}", 11, FontStyle.Italic, RugTheme.GreenDim)
                .alignment = TextAnchor.MiddleCenter;
            RugUi.NewText(_content, RugFlavor.DealerQuote(), 12, FontStyle.Italic, new Color(0.7f, 0.7f, 0.7f))
                .alignment = TextAnchor.MiddleCenter;

            GlanceStrip();
            RugUi.NewText(_content, new string('─', 56), 13, FontStyle.Normal, RugTheme.GreenDim);

            MenuItem("1", "THE BOOKS", "businesses, riders, the balance", () => Show(Screen.Books));
            MenuItem("2", "THE WASH",  "move the stash through your fronts", () => Show(Screen.Wash));
            MenuItem("3", "EXIT",      "back to the couch", Close);

            RugUi.NewText(_content, new string('─', 56), 13, FontStyle.Normal, RugTheme.GreenDim);
            _status = RugUi.NewText(_content, StatusLine(), 12, FontStyle.Normal, RugTheme.Amber);
            RugUi.NewText(_content, "C:\\RUGS> _", 12, FontStyle.Normal, RugTheme.GreenDim);
        }

        // A menu line, clickable and keyed: "[1]  THE BOOKS .......... businesses, riders, the balance"
        private static void MenuItem(string key, string label, string desc, Action onClick)
        {
            var go = new GameObject("menuitem", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.04f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            go.AddComponent<LayoutElement>().minHeight = 34f;

            string line = ($"[{key}]  {label} ").PadRight(26, '.') + "  " + desc;
            Text t = RugUi.NewText(go.transform, line, 14, FontStyle.Bold, RugTheme.Green);
            t.alignment = TextAnchor.MiddleLeft;
            RugUi.Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(12f, 0f); // left padding inside the row
        }

        // Chrome for a full screen: title banner, the glance strip, the content, status, and the way home.
        private static void BuildScreen(string title, Action<Transform> buildContent)
        {
            RugUi.NewText(_content, RugTheme.Banner(title), 16, FontStyle.Bold, RugTheme.GreenBright)
                .alignment = TextAnchor.MiddleCenter;
            GlanceStrip();
            RugUi.NewText(_content, new string('─', 56), 13, FontStyle.Normal, RugTheme.GreenDim);

            try { buildContent(_content); }
            catch (Exception e) { Debug.LogError("[RUGS!] terminal screen build failed: " + e); }

            RugUi.NewText(_content, new string('─', 56), 13, FontStyle.Normal, RugTheme.GreenDim);
            _status = RugUi.NewText(_content, StatusLine(), 12, FontStyle.Normal, RugTheme.Amber);
            RugUi.NewButton(_content, "←  MENU  (Esc)", Slate, 0f, Back);
        }

        // The numbers you always need, one look away on every screen: the stash, the heat, today's wash room.
        private static void GlanceStrip()
        {
            string s = $"STASH ${RugBooks.Dirty:N0}   ·   HEAT: {RugHeat.Band(RugBooks.Heat)}   ·   WASH ROOM ${RugLaunder.TotalSafeCapacity():N0} today";
            RugUi.NewText(_content, s, 13, FontStyle.Bold, RugTheme.Amber).alignment = TextAnchor.MiddleCenter;
        }

        // ---- driver (keyboard + boot), called from the component's Update while open ----

        internal static void DriverUpdate(float dt)
        {
            if (_root == null) return;

            if (_screen == Screen.Boot)
            {
                TickBoot(dt);
                // Any key/click past the opening grace skips the POST. (Esc falls through to Back below.)
                if (Input.anyKeyDown && !Input.GetKeyDown(KeyCode.Escape) && Time.unscaledTime - _openedAt > BootSkipGrace)
                {
                    Show(Screen.Menu);
                    return;
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape)) { Back(); return; }

            if (_screen == Screen.Menu)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) Show(Screen.Books);
                else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) Show(Screen.Wash);
                else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) Close();
            }
        }

        private static void TickBoot(float dt)
        {
            if (_bootText == null) return;
            int before = Mathf.Min((int)_bootChars, _bootFull.Length);
            _bootChars += dt * BootCharsPerSec;
            int now = Mathf.Min((int)_bootChars, _bootFull.Length);
            if (now != before) _bootText.text = RevealBoot(now);
            if (now >= _bootFull.Length)
            {
                _bootHold += dt;
                if (_bootHold >= BootHoldSecs) Show(Screen.Menu);
            }
        }

        // ---- lifecycle ----

        internal static void Close()
        {
            if (_root == null) return;
            UnityEngine.Object.Destroy(_root);
            _root = null;
            _content = null;
            _status = null;
            _bootText = null;
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
            catch (Exception e) { Debug.LogError("[RUGS!] terminal input block toggle failed: " + e); }
        }
    }

    /// <summary>Drives the terminal's boot type-on and keyboard while it's open (attached to its canvas root,
    /// so it's destroyed with the window — no global per-frame cost when the terminal is closed).</summary>
    internal sealed class RugTerminalDriver : MonoBehaviour
    {
        private void Update() => RugTerminal.DriverUpdate(Time.unscaledDeltaTime);
    }
}
