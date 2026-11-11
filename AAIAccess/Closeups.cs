using System;
using System.Collections.Generic;

namespace AAIAccess
{
    /// <summary>
    /// Descriptions of the close-up pictures: a line for the whole picture, and boxes for the things in it so each
    /// spot can be described. Built in (closeups.txt), and UserData/AAIAccess_closeups.txt can add or override lines;
    /// that file is read again whenever it changes, so new descriptions work without restarting the game.
    /// Format: name|description of the whole picture
    ///         name@x,y,w,h|what is in that box (in the same picture units as the spots)
    /// </summary>
    internal static class Closeups
    {
        private struct Box { public int X, Y, W, H; public string Text; }
        private static Dictionary<string, string> _whole;
        private static Dictionary<string, List<Box>> _boxes;
        private static DateTime _userStamp = DateTime.MinValue;
        private static int _ver = -1;
        private static string UserFile => System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AAIAccess_closeups.txt");

        private static void Load()
        {
            DateTime stamp = DateTime.MinValue;
            try { if (System.IO.File.Exists(UserFile)) stamp = System.IO.File.GetLastWriteTimeUtc(UserFile); } catch { }
            if (_whole != null && stamp == _userStamp && _ver == Loc.Version) return;
            _userStamp = stamp; _ver = Loc.Version;
            _whole = new Dictionary<string, string>(); _boxes = new Dictionary<string, List<Box>>();
            try
            {
                // The evidence page texts describe some of the same pictures.
                using var pt = Loc.Resource("pagetext.txt");
                if (pt != null) { using var rd = new System.IO.StreamReader(pt); Parse(rd.ReadToEnd(), false); }
            }
            catch { }
            try
            {
                using var st = Loc.Resource("closeups.txt");
                if (st != null) { using var rd = new System.IO.StreamReader(st); Parse(rd.ReadToEnd(), false); }
            }
            catch (Exception e) { Main.Log.Warning("Close-up descriptions: " + e.Message); }
            try { if (stamp != DateTime.MinValue) Parse(System.IO.File.ReadAllText(UserFile), true); }
            catch (Exception e) { Main.Log.Warning("AAIAccess_closeups.txt: " + e.Message); }
            Main.Log.Msg($"Close-up descriptions: {_whole.Count} pictures");
        }

        private static void Parse(string all, bool user)
        {
            var replaced = new HashSet<string>();
            foreach (var raw in all.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                int bar = l.IndexOf('|'); if (bar <= 0) continue;
                string key = l.Substring(0, bar).Trim(), text = l.Substring(bar + 1).Trim().Replace("\\n", " ");
                int at = key.IndexOf('@');
                if (at < 0) { _whole[key] = text; continue; }
                string name = key.Substring(0, at);
                var p = key.Substring(at + 1).Split(',');
                if (p.Length != 4 || !int.TryParse(p[0], out int x) || !int.TryParse(p[1], out int y) || !int.TryParse(p[2], out int w) || !int.TryParse(p[3], out int h)) continue;
                // The user file's boxes for a picture replace the built-in ones.
                if (user && replaced.Add(name)) _boxes.Remove(name);
                if (!_boxes.TryGetValue(name, out var list)) _boxes[name] = list = new List<Box>();
                list.Add(new Box { X = x, Y = y, W = w, H = h, Text = text });
            }
        }

        public static string Whole(string pic)
        {
            if (string.IsNullOrEmpty(pic)) return "";
            Load();
            return _whole.TryGetValue(pic, out var t) ? t : "";
        }

        /// <summary>What a spot shows: the smallest box holding the point the cursor aims at, else the box
        /// covering most of the spot's area.</summary>
        /// <summary>What a spot made of several boxes (e.g. a statue's body and its raised sword) shows: the described
        /// box that covers most of it.</summary>
        public static string SpotArea(string pic, IEnumerable<(int x, int y, int w, int h)> rects)
        {
            if (string.IsNullOrEmpty(pic)) return "";
            Load();
            if (!_boxes.TryGetValue(pic, out var list)) return "";
            var rs = new List<(int x, int y, int w, int h)>(rects);
            long total = 0; foreach (var r in rs) total += (long)r.w * r.h;
            if (total <= 0) return "";
            string best = ""; long bestOv = 0, bestSize = long.MaxValue;
            foreach (var b in list)
            {
                long ov = 0;
                foreach (var r in rs)
                {
                    long ow = Math.Min(r.x + r.w, b.X + b.W) - Math.Max(r.x, b.X), oh = Math.Min(r.y + r.h, b.Y + b.H) - Math.Max(r.y, b.Y);
                    if (ow > 0 && oh > 0) ov += ow * oh;
                }
                long size = (long)b.W * b.H;
                if (ov > bestOv || (ov == bestOv && ov > 0 && size < bestSize)) { bestOv = ov; bestSize = size; best = b.Text; }
            }
            return bestOv * 4 >= total ? best : "";   // at least a quarter of the spot
        }

        public static string Spot(string pic, int aimX, int aimY, int x, int y, int w, int h)
        {
            if (string.IsNullOrEmpty(pic)) return "";
            Load();
            if (!_boxes.TryGetValue(pic, out var list)) return "";
            string best = ""; long bestArea = long.MaxValue;
            foreach (var b in list)
                if (aimX >= b.X && aimX < b.X + b.W && aimY >= b.Y && aimY < b.Y + b.H && (long)b.W * b.H < bestArea) { bestArea = (long)b.W * b.H; best = b.Text; }
            if (best.Length > 0) return best;
            long bestOv = 0;
            foreach (var b in list)
            {
                long ow = Math.Min(x + w, b.X + b.W) - Math.Max(x, b.X), oh = Math.Min(y + h, b.Y + b.H) - Math.Max(y, b.Y);
                if (ow <= 0 || oh <= 0) continue;
                if (ow * oh > bestOv) { bestOv = ow * oh; best = b.Text; }
            }
            return bestOv * 4 >= (long)w * h ? best : "";   // at least a quarter of the spot
        }
    }
}
