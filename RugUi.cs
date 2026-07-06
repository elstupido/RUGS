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
            return crt;
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
