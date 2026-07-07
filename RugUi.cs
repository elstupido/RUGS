using System;
using UnityEngine;
using UnityEngine.UI;

namespace Rugs
{
    /// <summary>
    /// Shared panel plumbing for the mod's runtime-uGUI screens. Born of a launch-day bug report (neriku,
    /// v1.5.0): every panel sized itself to its content with NO scrolling, so a player with a big fleet got
    /// a laundry screen taller than the display — wash buttons and the GL door unreachable off-screen.
    ///
    /// <see cref="MakeScrollable"/> turns a panel Image into a capped-height scroll container and returns
    /// the CONTENT transform the screen builds its children into (a drop-in replacement for building into
    /// the panel directly). <see cref="Fit"/> is called after (re)building content: it sizes the panel to
    /// hug short content exactly as before, but caps it at <see cref="MaxPanelHeight"/> — past that, the
    /// content scrolls (mouse wheel / drag) instead of growing off-screen.
    /// </summary>
    internal static class RugUi
    {
        // Reference canvas is 1920×1080 (height-matched), so 940 always leaves breathing room on screen.
        internal const float MaxPanelHeight = 940f;

        /// <summary>Convert <paramref name="panel"/> into a max-height scrollable container. Build children
        /// into the returned CONTENT transform. Padding/spacing move from the panel to the content.</summary>
        internal static Transform MakeScrollable(Image panel, float width, RectOffset padding, float spacing)
        {
            RectTransform prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(width, 200f); // real height set by Fit()

            // Viewport: masks the content to the panel bounds.
            var viewportGo = new GameObject("viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(panel.transform, false);
            var vrt = (RectTransform)viewportGo.transform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            viewportGo.AddComponent<RectMask2D>();

            // Content: top-anchored, width-stretched, height driven by its layout (the old panel behavior).
            var contentGo = new GameObject("content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var crt = (RectTransform)contentGo.transform;
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = padding;
            vlg.spacing = spacing;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var fit = contentGo.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.content = crt;
            scroll.viewport = vrt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            // A visible scrollbar — the AFFORDANCE. Wheel-only scrolling is invisible; players stared at a
            // clipped list with no cue that more rows existed below. Slim phosphor-green thumb on a faint
            // track, overlaid on the right edge; auto-hides whenever the content fits (small fleets see
            // exactly what they always saw).
            var sbGo = new GameObject("scrollbar", typeof(RectTransform));
            sbGo.transform.SetParent(panel.transform, false);
            var srt = (RectTransform)sbGo.transform;
            srt.anchorMin = new Vector2(1f, 0f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(1f, 0.5f);
            srt.offsetMin = new Vector2(-10f, 6f);  // 8px wide …
            srt.offsetMax = new Vector2(-2f, -6f);  // … inset 2px from the edge, 6px top/bottom
            var track = sbGo.AddComponent<Image>();
            track.color = new Color(1f, 1f, 1f, 0.07f);

            var handleGo = new GameObject("handle", typeof(RectTransform));
            handleGo.transform.SetParent(sbGo.transform, false);
            var hrt = (RectTransform)handleGo.transform;
            hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
            hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = RugTheme.GreenDim; // terminal-green thumb, draggable

            var sb = sbGo.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.targetGraphic = handleImg;
            sb.handleRect = hrt;

            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return crt;
        }

        // ---- shared widget kit (mono font via RugTheme; used by the terminal and its screens) ----

        internal static Image NewImage(Transform parent, Color color)
        {
            var go = new GameObject("img", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        internal static Text NewText(Transform parent, string text, int size, FontStyle style, Color color)
        {
            var go = new GameObject("txt", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = RugTheme.Mono();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.alignment = TextAnchor.MiddleLeft;
            return t;
        }

        internal static void NewButton(Transform parent, string label, Color color, float width, Action onClick)
        {
            var go = new GameObject("btn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 26f;
            if (width > 0f) le.preferredWidth = width;

            Text t = NewText(go.transform, label, 12, FontStyle.Bold, Color.white);
            t.alignment = TextAnchor.MiddleCenter;
            Stretch(t.rectTransform);
        }

        /// <summary>Call after (re)building content: hug the content up to the cap, then rest the scroll at the top.</summary>
        internal static void Fit(Transform content)
        {
            if (content == null) return;
            var crt = content as RectTransform;
            if (crt == null) return;
            var vrt = crt.parent as RectTransform;              // viewport
            var panel = vrt != null ? vrt.parent as RectTransform : null;
            if (panel == null) return;
            Canvas.ForceUpdateCanvases();                        // settle the layout so preferred height is real
            float h = LayoutUtility.GetPreferredSize(crt, 1);    // axis 1 = vertical (includes padding)
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, Mathf.Min(h, MaxPanelHeight));
            var scroll = panel.GetComponent<ScrollRect>();
            if (scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 1f;          // open at the top
            }
        }
    }
}
