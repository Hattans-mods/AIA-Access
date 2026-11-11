using System.Text.RegularExpressions;

namespace AAIAccess
{
    internal static class TextCleaner
    {
        private static readonly Regex Sprite = new Regex("<sprite[^>]*?name=\"?([^\"> ]+)\"?[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"[ \t　]+", RegexOptions.Compiled);

        // Keyboard key icons: "button_03_N" where N is the game's KeyboardIconSpriteID.
        private static readonly string[] KeyNames = { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "Numpad 0", "Numpad 1", "Numpad 2", "Numpad 3", "Numpad 4", "Numpad 5", "Numpad 6", "Numpad 7", "Numpad 8", "Numpad 9", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "Backspace", "Space", "Insert", "Delete", "Home", "End", "Page Up", "Page Down", "Up arrow", "Down arrow", "Right arrow", "Left arrow" };

        private static string SpriteName(string name)
        {
            var p = name.Split('_');
            if (p.Length == 3 && p[0] == "button" && int.TryParse(p[2], out int n))
            {
                if (p[1] == "03") return n >= 0 && n < KeyNames.Length ? KeyNames[n] : "key " + n;
                return PadIcon(n);
            }
            return name.Replace('_', ' ');
        }

        // Controller icons: N is the game's GamePadIconSpriteID; named as on the connected controller (Pad.Name).
        private static readonly string[] StickIcons =
        {
            "the left stick", "the right stick",
            "the left stick up", "the left stick down", "the left stick left", "the left stick right",
            "the right stick up", "the right stick down", "the right stick left", "the right stick right",
            "the right stick up or down", "the right stick left or right",
        };

        private static string PadIcon(int n)
        {
            try
            {
                if (n >= 0 && n <= 3) return Pad.Name((Il2CppGK_System.Controller.ButtonType)n);   // A, B, X, Y
                switch (n)
                {
                    case 4: return Pad.Name(Pad.B.L1);
                    case 5: return Pad.Name(Pad.B.R1);
                    case 18: return Pad.Name(Pad.B.L3);
                    case 19: return Pad.Name(Pad.B.R3);
                    case 20: return Pad.Name(Pad.B.Start);
                    case 21: return Pad.Name(Pad.B.Select);
                }
                if (n >= 6 && n <= 17) return StickIcons[n - 6];
            }
            catch { }
            return "controller button " + n;
        }

        /// <summary>Removes rich-text tags and joins the window's lines into one readable string.</summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Sprite.Replace(s, m => " " + SpriteName(m.Groups[1].Value) + " ");
            s = Tags.Replace(s, "");
            s = s.Replace("\r", "").Replace("\n", " ");
            s = Spaces.Replace(s, " ");
            return s.Trim();
        }
    }
}
