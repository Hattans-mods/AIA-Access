extern alias GK1;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>
    /// Evidence "Check" pages (first game), e.g. a magazine: the game shows them as pictures, so there is
    /// no text to read. The mod says which page is showing.
    /// </summary>
    internal static class EvidenceDetail
    {
        private static string _sig = "";
        private static bool _open;
        private static int _page = -1;

        private static string _text = "";
        private static bool _hook;

        /// <summary>Harmony hooks: the game started / finished showing a page picture.</summary>
        public static void OnDetail2D(string what, bool open)
        {
            Main.Log.Msg("Detail view " + what);
            _hook = open;
        }
        public static bool Active => _open;

        /// <summary>F3: the page's text again.</summary>
        public static void ReadStatus() => Speech.Say(string.IsNullOrEmpty(_text) ? "No text known for this page." : _text, true, false);

        // The text of each page picture (read from the pictures beforehand), by picture name.
        private static Dictionary<string, string> _texts;
        private static int _textsVer = -1;
        private static string PageText(int page)
        {
            if (_texts == null || _textsVer != Loc.Version)
            {
                _texts = new Dictionary<string, string>(); _textsVer = Loc.Version;
                try
                {
                    using var st = Loc.Resource("pagetext.txt");
                    using var rd = new System.IO.StreamReader(st);
                    string l;
                    while ((l = rd.ReadLine()) != null)
                    {
                        if (l.StartsWith("#")) continue;
                        int bar = l.IndexOf('|');
                        if (bar > 0) _texts[l.Substring(0, bar)] = l.Substring(bar + 1).Replace("\\n", " ");
                    }
                }
                catch (Exception e) { Main.Log.Warning("Page texts: " + e.Message); }
            }
            int item = Organizer.LastItemId;
            string name = null;
            try
            {
                if (item >= 0)
                {
                    // The organizer's item number -> the item's detail number -> that detail's page pictures.
                    int dn = GK1::Il2Cpp.ItemListMenu.GetItemListDetailNumS(item);
                    try
                    {
                        var lt = GK1::Il2Cpp.ItemListMenu.ReadOnly.ItemListTbl;
                        if (item < lt.Length && lt[item] != null) Main.Log.Msg($"Item table: item {item} detail {lt[item].ItemDetailNum} (function said {dn})");
                        if (dn <= 0 && item < lt.Length && lt[item] != null) dn = (int)lt[item].ItemDetailNum;
                    }
                    catch (Exception e) { Main.Log.Warning("Item table: " + e.Message); }
                    var tbl = GK1::Il2Cpp.ItemListMenu.ReadOnly.DetailListTbl;
                    uint ncg = 0;
                    if (dn > 0 && dn < tbl.Length && tbl[dn]?.files != null && page < tbl[dn].files.Length) ncg = (uint)tbl[dn].files[page].NCG;
                    if (ncg != 0) name = PageExport.NcgName(ncg);
                    Main.Log.Msg($"Detail page: item {item} detail {dn} ({(Il2CppGK_System.AutoPlay.GK1.DETAIL_ID)dn}) page {page} -> ncg {ncg} {name}");
                }
            }
            catch (Exception e) { Main.Log.Warning("Detail page lookup: " + e.Message); }
            return name != null && _texts.TryGetValue(name, out var t) ? t : "";
        }

        /// <summary>Which of this detail's page pictures the game actually has (a missing one may crash the game).</summary>
        /// <returns>How many pages from the first have their picture (-1 when unknown).</returns>
        private static int _lastDn = -1;
        private static int LogPictures(int max)
        {
            try
            {
                _lastDn = -1;
                int item = Organizer.LastItemId; if (item < 0) return -1;
                int dn = GK1::Il2Cpp.ItemListMenu.GetItemListDetailNumS(item);
                var tbl = GK1::Il2Cpp.ItemListMenu.ReadOnly.DetailListTbl;
                _lastDn = dn;
                if (dn <= 0 || dn >= tbl.Length || tbl[dn]?.files == null) return -1;
                var parts = new List<string>();
                int good = 0; bool gap = false, laterOk = false;
                for (int k = 0; k < tbl[dn].files.Length; k++)
                {
                    string n = PageExport.NcgName((uint)tbl[dn].files[k].NCG);
                    if (n == null) { parts.Add("?"); gap = true; continue; }
                    bool has = false;
                    foreach (var a in new[] { $"GK1/Ipnce/FHD/Texture/{n}_eng.png", $"GK1/Ipnce/FHD/Texture/{n}.png" })
                        try { if (Il2CppGK_System.Resource.ResourceManager.AddressIsExist(a)) has = true; } catch { }
                    parts.Add(n + (has ? "" : " (missing)"));
                    if (!has) gap = true; else if (!gap) good++; else laterOk = true;
                }
                Main.Log.Msg($"Detail {dn} pictures: {string.Join(", ", parts)}; pages {max}");
                // Some pages are text pages (no picture, the game shows text): only cut the count when nothing after
                // the first missing picture exists either.
                return good;
            }
            catch (Exception e) { Main.Log.Warning("Detail pictures: " + e.Message); return -1; }
        }

        private static string _cutSaid = "";

        private static string CutText(GK1::Il2Cpp.ItemListMenu m)
        {
            try
            {
                var c = m.m_TextCut; if (c == null || !c.IsDisplay) return "";
                return TextCleaner.Clean(c.m_Text?.text) ?? "";
            }
            catch { return ""; }
        }

        private static bool PagerShown(GK1::Il2Cpp.ItemListMenu m)
        {
            try { var c = m.m_PagerUI?.PagerCtr; return c != null && !c.WasCollected && c.gameObject.activeInHierarchy; } catch { return false; }
        }

        /// <summary>An evidence page (2D detail view) is showing.</summary>
        public static bool PageOpen() { var il = GK1::Il2Cpp.GKJ1.ItemList; return il != null && il.itemListMode == 10; }

        public static void Poll()
        {
            if (Main.NoDetail) return;
            try
            {
                var m = GK1::Il2Cpp.GKJ1.ItemList;
                if (m == null) return;
                int mode = m.itemListMode, page = m.currentDetailPage, max = m.detailPageMax;
                bool pager = PagerShown(m);
                bool text = false; try { text = m.m_TextCut != null && m.m_TextCut.IsDisplay; } catch { }
                string sig = $"mode={mode} page={page} max={max} pager={pager} textcut={text} organizer={Organizer.Active}";
                if (sig != _sig) { _sig = sig; Main.Log.Msg("Evidence detail: " + sig); }
                bool open = (pager && max > 0) || _hook;
                if (open != _open)
                {
                    _open = open; _page = -1; _cutSaid = "";
                    if (!open) return;
                }
                // A text page: the game writes the page's text on screen (not a picture). It may arrive a moment after
                // the page turns, so it is read whenever it changes.
                string cut = text ? CutText(m) : "";
                if (open && page == _page && cut.Length > 0 && cut != _cutSaid)
                {
                    _cutSaid = cut; _text = cut;
                    Speech.Say(cut, false, false); Speech.Remember(cut);
                    return;
                }
                if (!open || page == _page) return;
                bool first = _page < 0;
                _page = page;
                // A text page (the game shows the text itself, not a picture) is one page, whatever the count says.
                string where = max > 1 ? $"Page {page + 1} of {max}" : "";
                if (first)
                {
                    int have = LogPictures(max);
                    // The Preliminary Findings (episode 3) say 2 pages but are one, and turning to the second crashed the
                    // game: stop at the first. Other text pages (no picture) are real pages and stay as they are.
                    if (_lastDn == 46 && have >= 0 && have < max)
                    {
                        int cap = Math.Max(1, have);
                        try { m.detailPageMax = cap; Main.Log.Msg($"Pages limited to {cap} (the rest have no picture)"); } catch (Exception e) { Main.Log.Warning("Couldn't limit pages: " + e.Message); }
                        max = cap; where = max > 1 ? $"Page {page + 1} of {max}" : "";
                    }
                }
                _text = PageText(page);
                // The text from the page before may still be up for a moment: wait for the new one then.
                if (cut.Length > 0 && cut != _cutSaid) { if (_text.Length == 0) _text = cut; _cutSaid = cut; }
                Speech.Announce(Menus.Join(first ? Organizer.LastName : "", where, _text), true);
            }
            catch (Exception e) { Main.Log.Error("Evidence detail poll failed: " + e.Message); }
        }
    }
}
