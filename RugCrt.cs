using UnityEngine;
using UnityEngine.UI;

namespace Rugs
{
    /// <summary>
    /// CRT glass for the CROOKBOOKS™ terminal — the layer that turns a green panel into a physical tube.
    /// Two procedurally-generated textures (no assets, no Harmony), laid over the panel as a raycast-transparent
    /// overlay that resizes with it:
    ///   • SCANLINES — a tiny repeating texture, tiled, so faint dark horizontal lines ride over everything;
    ///   • VIGNETTE  — a soft radial darkening toward the panel edges (phosphor falloff);
    /// plus a whisper of brightness flicker on the whole overlay. Everything under the glass stays clickable
    /// (the overlay never eats a raycast). Textures are built once and cached.
    /// </summary>
    internal static class RugCrt
    {
        // ---- tuning (all trivially eyeball-tunable) ----
        private const int   ScanPeriodPx = 3;     // one dark scanline every N reference px
        private const float ScanDarkness = 0.15f; // alpha of the dark line
        private const float VigStart     = 0.60f; // radius (0..1) where the vignette begins
        private const float VigMax       = 0.34f; // darkest alpha at the corner
        private const float FlickerBase   = 0.97f; // overlay alpha centre …
        private const float FlickerAmp    = 0.03f; // … ± this, slow sine (whisper-subtle)
        private const float FlickerHz     = 0.5f;

        private static Sprite _scan, _vig;

        /// <summary>Lay the CRT glass over a terminal panel. Call AFTER the panel's scroll shell is built so
        /// the overlay is the last child (drawn on top of content, scrollbar and all).</summary>
        internal static void AddGlass(Image panel)
        {
            if (panel == null) return;
            try
            {
                var overlay = new GameObject("crt-glass", typeof(RectTransform));
                overlay.transform.SetParent(panel.transform, false); // last child → drawn on top of everything
                Fill((RectTransform)overlay.transform);
                var cg = overlay.AddComponent<CanvasGroup>();
                cg.blocksRaycasts = false; // the glass never intercepts a click
                cg.interactable = false;

                // Vignette first (underneath the scanlines within the overlay).
                var vig = new GameObject("vignette", typeof(RectTransform));
                vig.transform.SetParent(overlay.transform, false);
                Fill((RectTransform)vig.transform);
                var vi = vig.AddComponent<Image>();
                vi.sprite = Vignette();
                vi.type = Image.Type.Simple;
                vi.raycastTarget = false;

                // Scanlines on top.
                var scan = new GameObject("scanlines", typeof(RectTransform));
                scan.transform.SetParent(overlay.transform, false);
                Fill((RectTransform)scan.transform);
                var si = scan.AddComponent<Image>();
                si.sprite = Scanlines();
                si.type = Image.Type.Tiled;
                si.raycastTarget = false;

                overlay.AddComponent<RugCrtFlicker>().group = cg;
            }
            catch (System.Exception e) { Debug.LogError("[RUGS!] CRT glass failed: " + e); }
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        // 1×N texture: one dark row, the rest clear → tiled, gives a dark scanline every N reference px.
        private static Sprite Scanlines()
        {
            if (_scan != null) return _scan;
            int h = Mathf.Max(2, ScanPeriodPx);
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            var px = new Color[h];
            for (int i = 0; i < h; i++) px[i] = new Color(0f, 0f, 0f, 0f);
            px[0] = new Color(0f, 0f, 0f, ScanDarkness);
            tex.SetPixels(px); tex.Apply();
            _scan = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 1f);
            return _scan;
        }

        // Radial darkening: clear in the centre, ramping to VigMax black at the corners (phosphor falloff).
        private static Sprite Vignette()
        {
            if (_vig != null) return _vig;
            int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float c = (n - 1) / 2f;
            float maxd = new Vector2(c, c).magnitude;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x - c, y - c).magnitude / maxd; // 0 centre → 1 corner
                    float t = Mathf.Clamp01((d - VigStart) / (1f - VigStart));
                    px[y * n + x] = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, t) * VigMax);
                }
            tex.SetPixels(px); tex.Apply();
            _vig = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 1f);
            return _vig;
        }

        internal static float FlickerAlpha(float t)
            => FlickerBase + FlickerAmp * Mathf.Sin(t * FlickerHz * 2f * Mathf.PI);
    }

    /// <summary>Whisper of CRT brightness flicker — modulates the glass overlay's alpha. Dies with the overlay.</summary>
    internal sealed class RugCrtFlicker : MonoBehaviour
    {
        internal CanvasGroup group;
        private void Update() { if (group != null) group.alpha = RugCrt.FlickerAlpha(Time.unscaledTime); }
    }
}
