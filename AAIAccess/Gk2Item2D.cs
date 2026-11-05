extern alias GK2;
using System;
using UnityEngine;
using G2 = GK2::Il2Cpp;

namespace AAIAccess
{
    /// <summary>Second game: evidence you "check" that is only a picture (a photo, a plan, a newspaper). The game shows one
    /// of its close-up pictures; the mod finds which by the texture on screen and reads its description (closeups.txt).</summary>
    internal static class Gk2Item2D
    {
        private static bool _active;
        private static string _pic = "", _text = "";
        private static float _next, _lostAt = -1;
        private static int _cut = int.MinValue;

        public static bool Active => _active;
        private static bool _logged;

        private static void LogTextures(int cut)
        {
            var sb = new System.Text.StringBuilder($"Evidence picture not found (cut {cut}); textures on screen:");
            try
            {
                foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    try { if (r.enabled && r.gameObject.activeInHierarchy) sb.Append($" [{r.gameObject.name}:{r.sharedMaterial?.mainTexture?.name}]"); } catch { }
                foreach (var ri in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.RawImage>())
                    try { if (ri.isActiveAndEnabled) sb.Append($" (raw {ri.gameObject.name}:{ri.texture?.name})"); } catch { }
            }
            catch { }
            Main.Log.Msg(sb.ToString());
        }

        private static bool Open(out G2.cSceneItem2D s)
        {
            s = null;
            try
            {
                s = G2.cSceneItem2D.Instance; if (s == null) return false;
                var d = s.m_SousaDetails;
                return d != null && !d.WasCollected && d.gameObject.activeInHierarchy;
            }
            catch { return false; }
        }

        private static string Key(string tex)
        {
            if (string.IsNullOrEmpty(tex)) return null;
            string n = tex.ToLowerInvariant();
            if (!n.StartsWith("cut")) return null;
            foreach (var suf in new[] { "_eng", "_fre", "_ger", "_kor", "_sim", "_tra" }) if (n.EndsWith(suf)) n = n.Substring(0, n.Length - suf.Length);
            if (n.EndsWith("_cm")) n = n.Substring(0, n.Length - 3);
            return n;
        }

        /// <summary>The close-up picture currently drawn: any active renderer or UI image whose texture is named cutXX_YYY.</summary>
        private static string FindPicture()
        {
            try
            {
                foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    try { var k = Key(r.sharedMaterial?.mainTexture?.name); if (k != null) return k; } catch { }
                }
                foreach (var ri in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.RawImage>())
                {
                    if (ri == null || !ri.isActiveAndEnabled) continue;
                    var k = Key(ri.texture?.name); if (k != null) return k;
                }
                foreach (var im in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>())
                {
                    if (im == null || !im.isActiveAndEnabled) continue;
                    var k = Key(im.sprite?.texture?.name); if (k != null) return k;
                }
            }
            catch (Exception e) { Main.Log.Warning("Item2D scan: " + e.Message); }
            return null;
        }

        public static void Poll()
        {
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.25f;
            if (!Open(out var s))
            {
                if (_active) { if (_lostAt < 0) _lostAt = now; else if (now - _lostAt > 0.6f) { _active = false; _pic = ""; _cut = int.MinValue; } }
                return;
            }
            _lostAt = -1;
            int cut = int.MinValue; try { cut = (int)s.mCutTop * 100 + s.mCutNo; } catch { }
            if (_active && cut == _cut) return;
            string pic = FindPicture();
            if (pic == null) { if (!_logged) { _logged = true; LogTextures(cut); } return; }
            bool fresh = !_active; _active = true; _cut = cut;
            if (pic == _pic && !fresh) return;
            _pic = pic;
            uint num = 0; try { num = s.mCutNum; } catch { }
            _text = Closeups.Whole(pic);
            for (string b = pic; _text.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(b, "_[a-z]+$"); )
            { b = System.Text.RegularExpressions.Regex.Replace(b, "_[a-z]+$", ""); _text = Closeups.Whole(b); }
            Main.Log.Msg($"Evidence picture {pic} (cut {cut}, {num} pages): {(_text.Length > 0 ? "described" : "no description")}");
            string page = num > 1 ? $"Page {s.mCutNo + 1} of {num}" : null;
            Speech.Say(Menus.Join(page, _text.Length > 0 ? _text : "A picture with no description yet"), !fresh, false);
            Speech.Remember(_text);
        }

        /// <summary>F3: the description again.</summary>
        public static void ReadStatus() => Speech.Say(_text.Length > 0 ? _text : "No description for this picture.", true, false);
    }
}
