namespace AAIAccess
{
    /// <summary>F1: explains the current screen. Key names are the game's default keyboard layout.</summary>
    internal static class Help
    {
        private const string ModKeys =
            "Mod keys: F1 help for the current screen. F2 repeats the last thing said. F3 gives the status of the current puzzle. H tells you how full your truth gauge is. F4 says the name of the room you are in; to rename a room, copy the name you want and press Shift F4.";

        private const string Beeps =
            "While walking, a high double beep means something to examine, a low double beep means someone to talk to; a single beep means you checked it already.";

        private const string GameKeys =
            "Game keys, if you haven't changed them: Space confirms, Z cancels or runs, E opens the Organizer, Q opens Logic, " +
            "T calls your partner or presents, R opens the history, G opens the options. Move and move the cursor with W, A, S, D. The arrow keys rotate evidence and look around.";

        private const string LogicHelp =
            "Logic. Here you combine two pieces of information you have found to reach a new conclusion. " +
            "Move up and down through the pieces with W and S; each one is read with its description. " +
            "Press Space to pick a piece. Pick two that you think are related, then confirm to connect them. " +
            "If the connection is right, Edgeworth reaches a new deduction; if not, try a different pair. " +
            "Press Z to take a piece back out or to leave Logic. F3 tells you which pieces are picked.";

        private const string InvestigationHelp =
            "Investigation. Walk around with W, A, S, D, and hold Z to run. " +
            "When you are close to something, you hear Examine, or Talk for a person. Press Space to examine it or talk. " +
            "Checked means you have looked at it before. " +
            "To find things, press the right bracket key for the next person or thing in the room, nearest first, and the left bracket key for the previous one. " +
            "Each is read with where it is from you. Press backslash to walk there automatically, and backslash again to stop. " +
            "Press the apostrophe key to track it: a sound repeats from the next turn on the way there, left or right in your headphones, higher when it is further up the room, lower when further down, louder and faster as you get closer. Walk toward the sound; when you reach a turn, the sound moves to the next one. Two notes mean you are there. Press apostrophe again to stop tracking. " +
            "Press Q to open Logic when you have new pieces of Logic, and E to open the Organizer with your evidence and profiles.";

        private const string ExamineHelp =
            "Examination. You are looking closely at a scene with a cursor. Some spots in the picture can be examined. " +
            "Press the right bracket key to jump the cursor to the next spot, and the left bracket key for the previous one. " +
            "You can also move the cursor yourself with W, A, S, D; a high beep means it is on a spot, a lower beep means a spot you already checked. " +
            "Press Space to examine the spot under the cursor. F3 describes the picture and tells you where the cursor is. Press Z to leave. " +
            "When Edgeworth notices a contradiction, point the cursor at what doesn't fit and press T to deduce; to show that evidence proves it, open the Organizer with E and present the evidence.";

        private const string FingerHelp =
            "Fingerprint dusting. Cover the hidden print with powder, then blow the loose powder away. " +
            "The bracket keys put the cursor on each place that still needs powder; drop powder there with the game's confirm key, usually twice, " +
            "then blow with the game's blow key. After each drop or blow the mod says how much of the print is covered and whether loose powder is left. " +
            "The print shows when enough of it is covered and no loose powder remains. F3 says how it looks now.";

        private const string LuminolHelp =
            "Luminol test. Spray luminol to find hidden traces. Press the right bracket key to put the cursor on the next trace, " +
            "the left bracket key for the previous one, then spray with the game's examine key. Some traces need more than one spray. " +
            "F3 says how many are found and what the cursor is on.";

        private const string Examine3DHelp =
            "3D examination. You are holding a piece of evidence that you can turn around to look for details. " +
            "Press the right bracket key to turn it to the next detail and put the cursor on it, and the left bracket key for the previous one. " +
            "A beep means the cursor is on a detail. Press Space to examine it. " +
            "You can also turn the evidence yourself with the arrow keys, zoom with Q and E, and move the cursor with W, A, S, D. F3 tells you whether the cursor is on a detail. Press Z to put it down.";

        private const string DeductionHelp =
            "Deduction. Edgeworth has noticed a contradiction in this scene; show where it is. " +
            // T is the game's deduce key (its X button); this sentence is translated through the "{0} ... {2}" template line.
            "Press the right bracket key for the next spot in the picture and the left bracket key for the previous one, then press T to point it out. " +
            "If the contradiction is about evidence instead, open the Organizer with E, choose the evidence and present it. " +
            "A wrong answer lowers your truth gauge, so think about what doesn't fit before you answer. F3 describes the picture and tells you where the cursor is.";

        private const string OrganizerHelp =
            "Organizer. Your evidence and the profiles of the people involved. Move through the entries with W, A, S, D; each is read with its description. " +
            "When you are asked to present, choose the evidence and press T to present it. Press Z to close. F3 reads the highlighted entry again.";

        private const string ChessHelp =
            "Logic chess. Your opponent is holding back; a time gauge runs down while you think. H tells you how much time is left, and the mod warns you as it runs low. " +
            "Choose what to ask from the list with W and S; each choice names its chess piece. Stay silent lets your opponent speak instead.";

        /// <summary>A controller is what the player is using now, and the mod's controller keys are on.</summary>
        private static bool OnPad => KeyMap.UsingController && Pad.Help(false).Length > 0;

        /// <summary>"Press F1 for help.", or the controller's help buttons when playing with a controller.</summary>
        public static string Hint() => OnPad
            ? Pad.F("Hold {0} and press {1} for help.", Pad.ModName, Pad.Name(Pad.B.Start))
            : "Press F1 for help.";

        /// <summary>The deduce key or button (the game's X: T, or Triangle), for the device in use.</summary>
        public static string DeduceButton() => OnPad
            ? Pad.Name(Il2CppGK_System.Controller.ButtonType.X)
            : KeyMap.Name(Il2CppGK_System.Controller.ButtonType.X);

        /// <summary>pad: help was asked for from the controller, so it names the connected controller's buttons instead of keys.</summary>
        public static void Speak(bool pad = false)
        {
            if (pad && Pad.Help(false).Length == 0) pad = false;
            string text;
            if (pad) text = PadText();
            else if (!KeyMap.IsDefault) text = KeyText();
            else if (LogicChess.Active) text = ChessHelp;
            else if (Examine3D.Active || Gk2Examine3D.Active) text = Examine3DHelp;   // also opens from the Organizer
            else if (Organizer.Active) text = OrganizerHelp + " " + Organizer.TabKeys();
            else if (LogicScreen.Active) text = LogicHelp;
            else if (ExamineScreen.Deduction || ExamineScreen.DeductionOutside) text = DeductionHelp;
            else if (ExamineScreen.Active) text = ExamineHelp;
            else if (Examine3D.Active || Gk2Examine3D.Active) text = Examine3DHelp;
            else if (Gk2Luminol.Active) text = LuminolHelp;
            else if (Gk2Finger.Active) text = FingerHelp;
            else if (Investigation.InInvestigation) text = InvestigationHelp + " " + ModKeys + " " + Beeps;
            else text = ModKeys + " " + Beeps + " " + GameKeys;
            Speech.Say(text, true, false);
        }

        // ---------- keyboard help with the player's own keys (only when they changed them in the options) ----------
        // Same rules as the controller help: templates are lines of lang/en.txt as written; the rest is the default help's own.

        private static string K(Il2CppGK_System.Controller.ButtonType b) => KeyMap.Name(b);

        private static string KeyText()
        {
            var A = Il2CppGK_System.Controller.ButtonType.A;
            var Bk = Il2CppGK_System.Controller.ButtonType.B;
            var X = Il2CppGK_System.Controller.ButtonType.X;
            var Y = Il2CppGK_System.Controller.ButtonType.Y;
            var L1 = Il2CppGK_System.Controller.ButtonType.L1;
            var R1 = Il2CppGK_System.Controller.ButtonType.R1;
            var Up = Il2CppGK_System.Controller.ButtonType.DPAD_Up;
            var Down = Il2CppGK_System.Controller.ButtonType.DPAD_Down;
            string move = KeyMap.Move();
            const string RightBr = "the right bracket key", LeftBr = "the left bracket key";

            if (LogicChess.Active)
                return "Logic chess. Your opponent is holding back; a time gauge runs down while you think. H tells you how much time is left, and the mod warns you as it runs low. " +
                    F("Choose what to ask from the list with {0} and {1}; each choice names its chess piece.", K(Up), K(Down)) + " Stay silent lets your opponent speak instead.";
            if (Organizer.Active && !Examine3D.Active && !Gk2Examine3D.Active)   // 3D examination opens from the Organizer
                return "Organizer. Your evidence and the profiles of the people involved. " +
                    F("Move through the entries with {0}; each is read with its description.", move) + " " +
                    F("When you are asked to present, choose the evidence and press {0} to present it.", K(X)) + " " +
                    F("Press {0} to close.", K(Bk)) + " F3 reads the highlighted entry again. " + Organizer.TabKeys(false);
            if (LogicScreen.Active)
                return "Logic. Here you combine two pieces of information you have found to reach a new conclusion. " +
                    F("Move up and down through the pieces with {0} and {1}; each one is read with its description.", K(Up), K(Down)) + " " +
                    F("Press {0} to pick a piece.", K(A)) + " Pick two that you think are related, then confirm to connect them. " +
                    "If the connection is right, Edgeworth reaches a new deduction; if not, try a different pair. " +
                    F("Press {0} to take a piece back out or to leave Logic.", K(Bk)) + " F3 tells you which pieces are picked.";
            if (ExamineScreen.Deduction || ExamineScreen.DeductionOutside)
                return "Deduction. Edgeworth has noticed a contradiction in this scene; show where it is. " +
                    F("Press {0} for the next spot in the picture and {1} for the previous one, then press {2} to point it out.", RightBr, LeftBr, K(X)) + " " +   // deduce key
                    F("If the contradiction is about evidence instead, press {0} to open the Organizer, then choose the evidence and present it.", K(R1)) + " " +
                    "A wrong answer lowers your truth gauge, so think about what doesn't fit before you answer. F3 describes the picture and tells you where the cursor is.";
            if (ExamineScreen.Active)
                return "Examination. You are looking closely at a scene with a cursor. Some spots in the picture can be examined. " +
                    "Press the right bracket key to jump the cursor to the next spot, and the left bracket key for the previous one. " +
                    F("You can also move the cursor yourself with {0}; a high beep means it is on a spot, a lower beep means a spot you already checked.", move) + " " +
                    F("Press {0} to examine the spot under the cursor.", K(A)) + " F3 describes the picture and tells you where the cursor is. " +
                    F("Press {0} to leave.", K(Bk)) + " " +
                    F("When Edgeworth notices a contradiction, point the cursor at what doesn't fit and press {0} to deduce; to show that evidence proves it, press {1} to open the Organizer and present the evidence.", K(X), K(R1));
            if (Examine3D.Active || Gk2Examine3D.Active)
                return "3D examination. You are holding a piece of evidence that you can turn around to look for details. " +
                    "Press the right bracket key to turn it to the next detail and put the cursor on it, and the left bracket key for the previous one. " +
                    "A beep means the cursor is on a detail. " + F("Press {0} to examine it.", K(A)) + " " +
                    F("You can also turn the evidence yourself with {0}, zoom with {1} and {2}, and move the cursor with {3}.", KeyMap.Look(), K(L1), K(R1), move) + " " +
                    "F3 tells you whether the cursor is on a detail. " + F("Press {0} to put it down.", K(Bk));
            if (Gk2Luminol.Active) return LuminolHelp;   // names no game keys
            if (Gk2Finger.Active) return FingerHelp;
            if (Investigation.InInvestigation)
                return "Investigation. " + F("Walk around with {0}, and hold {1} to run.", move, K(Bk)) + " " +
                    "When you are close to something, you hear Examine, or Talk for a person. " + F("Press {0} to examine it or talk.", K(A)) + " " +
                    "Checked means you have looked at it before. " +
                    "To find things, press the right bracket key for the next person or thing in the room, nearest first, and the left bracket key for the previous one. " +
                    "Each is read with where it is from you. Press backslash to walk there automatically, and backslash again to stop. " +
                    "Press the apostrophe key to track it: a sound repeats from the next turn on the way there, left or right in your headphones, higher when it is further up the room, lower when further down, louder and faster as you get closer. " +
                    "Walk toward the sound; when you reach a turn, the sound moves to the next one. Two notes mean you are there. Press apostrophe again to stop tracking. " +
                    F("Press {0} to open Logic when you have new pieces of Logic, and {1} to open the Organizer with your evidence and profiles.", K(L1), K(R1)) + " " +
                    ModKeys + " " + Beeps;
            return ModKeys + " " + Beeps + " " +
                F("Game keys: {0} confirms, {1} cancels or runs, {2} calls your partner or presents, {3} opens the history.", K(A), K(Bk), K(X), K(Y)) + " " +
                F("{0} opens Logic, {1} opens the Organizer, {2} opens the options.", K(L1), K(R1), K(Il2CppGK_System.Controller.ButtonType.Start)) + " " +
                F("Move and move the cursor with {0}; {1} turn evidence and look around.", move, KeyMap.Look());
        }

        // ---------- controller help: the same screens, with the connected controller's button names ----------
        // Every template below is a line of lang/en.txt exactly as written ({0}-{3} are button names); sentences without
        // buttons are the keyboard help's own, so they are already translated.

        private static string N(Il2CppGK_System.Controller.ButtonType b) => Pad.Name(b);
        private static string N(Pad.B b) => Pad.Name(b);
        private static string F(string t, params string[] n) => Pad.F(t, n);
        private static string Status(string template) => F(template, Pad.ModName, N(Pad.B.East));

        private static string PadText()
        {
            var A = Il2CppGK_System.Controller.ButtonType.A;     // confirm
            var Bk = Il2CppGK_System.Controller.ButtonType.B;    // cancel, run
            var X = Il2CppGK_System.Controller.ButtonType.X;     // partner, present
            var Y = Il2CppGK_System.Controller.ButtonType.Y;     // history
            string next = N(Pad.B.R1), prev = N(Pad.B.L1), mod = Pad.ModName;
            string logic = N(Pad.B.L2), organizer = N(Pad.B.R2);   // the triggers open Logic and the Organizer too

            if (LogicChess.Active)
                return "Logic chess. Your opponent is holding back; a time gauge runs down while you think. " +
                    F("Hold {0} and press {1} to hear how much time is left; the mod also warns you as it runs low.", mod, N(Pad.B.West)) + " " +
                    "Choose what to ask from the list with the left stick or the D-pad; each choice names its chess piece. Stay silent lets your opponent speak instead.";
            if (Organizer.Active && !Examine3D.Active && !Gk2Examine3D.Active)   // 3D examination opens from the Organizer
                return "Organizer. Your evidence and the profiles of the people involved. " +
                    "Move through the entries with the left stick or the D-pad; each is read with its description. " +
                    F("When you are asked to present, choose the evidence and press {0} to present it.", N(X)) + " " +
                    F("Press {0} to close.", N(Bk)) + " " + Status("Hold {0} and press {1} to hear the highlighted entry again.") + " " + Organizer.TabKeys(true);
            if (LogicScreen.Active)
                return "Logic. Here you combine two pieces of information you have found to reach a new conclusion. " +
                    "Move up and down through the pieces with the left stick or the D-pad; each one is read with its description. " +
                    F("Press {0} to pick a piece.", N(A)) + " Pick two that you think are related, then confirm to connect them. " +
                    "If the connection is right, Edgeworth reaches a new deduction; if not, try a different pair. " +
                    F("Press {0} to take a piece back out or to leave Logic.", N(Bk)) + " " + Status("Hold {0} and press {1} to hear which pieces are picked.");
            if (ExamineScreen.Deduction || ExamineScreen.DeductionOutside)
                return "Deduction. Edgeworth has noticed a contradiction in this scene; show where it is. " +
                    F("Press {0} for the next spot in the picture and {1} for the previous one, then press {2} to point it out.", next, prev, N(X)) + " " +   // deduce button
                    F("If the contradiction is about evidence instead, press {0} to open the Organizer, then choose the evidence and present it.", organizer) + " " +
                    "A wrong answer lowers your truth gauge, so think about what doesn't fit before you answer. " +
                    Status("Hold {0} and press {1} to hear the picture described and where the cursor is.");
            if (ExamineScreen.Active)
                return "Examination. You are looking closely at a scene with a cursor. Some spots in the picture can be examined. " +
                    F("Press {0} to jump the cursor to the next spot, and {1} for the previous one.", next, prev) + " " +
                    "You can also move the cursor yourself with the left stick or the D-pad; a high beep means it is on a spot, a lower beep means a spot you already checked. " +
                    F("Press {0} to examine the spot under the cursor.", N(A)) + " " +
                    Status("Hold {0} and press {1} to hear the picture described and where the cursor is.") + " " + F("Press {0} to leave.", N(Bk)) + " " +
                    F("When Edgeworth notices a contradiction, point the cursor at what doesn't fit and press {0} to deduce; to show that evidence proves it, press {1} to open the Organizer and present the evidence.", N(X), organizer);
            if (Examine3D.Active || Gk2Examine3D.Active)
                return "3D examination. You are holding a piece of evidence that you can turn around to look for details. " +
                    F("Press {0} to turn it to the next detail and put the cursor on it, and {1} for the previous one.", next, prev) + " " +
                    "A beep means the cursor is on a detail. " + F("Press {0} to examine it.", N(A)) + " " +
                    "You can also turn the evidence yourself with the right stick and move the cursor with the left stick or the D-pad. " +
                    Status("Hold {0} and press {1} to hear whether the cursor is on a detail.") + " " + F("Press {0} to put it down.", N(Bk));
            if (Gk2Luminol.Active)
                return "Luminol test. Spray luminol to find hidden traces. " +
                    F("Press {0} to put the cursor on the next trace and {1} for the previous one, then press {2} to spray.", next, prev, N(A)) + " " +
                    "Some traces need more than one spray. " + Status("Hold {0} and press {1} to hear how many are found and what the cursor is on.");
            if (Gk2Finger.Active)
                return "Fingerprint dusting. Cover the hidden print with powder, then blow the loose powder away. " +
                    F("Press {0} or {1} to put the cursor on each place that still needs powder, then press {2} to drop powder there, usually twice, and blow with the game's blow button.", next, prev, N(A)) + " " +
                    "After each drop or blow the mod says how much of the print is covered and whether loose powder is left. " +
                    "The print shows when enough of it is covered and no loose powder remains. " + Status("Hold {0} and press {1} to hear how it looks now.");
            if (Investigation.InInvestigation)
                return "Investigation. " + F("Walk around with the left stick or the D-pad, and hold {0} to run.", N(Bk)) + " " +
                    "When you are close to something, you hear Examine, or Talk for a person. " + F("Press {0} to examine it or talk.", N(A)) + " " +
                    "Checked means you have looked at it before. " +
                    F("To find things, press {0} for the next person or thing in the room, nearest first, and {1} for the previous one.", next, prev) + " " +
                    "Each is read with where it is from you. " +
                    F("Hold {0} and press {1} to walk there automatically, and do it again to stop.", mod, next) + " " +
                    F("Hold {0} and press {1} to track it: a sound repeats from the next turn on the way there, left or right in your headphones, higher when it is further up the room, lower when further down, louder and faster as you get closer.", mod, prev) + " " +
                    "Walk toward the sound; when you reach a turn, the sound moves to the next one. Two notes mean you are there. Do it again to stop tracking. " +
                    F("Press {0} to open Logic when you have new pieces of Logic, and {1} to open the Organizer with your evidence and profiles.", logic, organizer) + " " +
                    Pad.Help(false) + " " + Beeps;
            return Pad.Help(true) + " " + Beeps + " " +
                F("Game buttons, if you haven't changed them: press {0} to confirm, {1} to cancel or run, {2} to call your partner or present, {3} for the history.", N(A), N(Bk), N(X), N(Y)) + " " +
                F("Press {0} or {1} to open Logic, {2} or {3} to open the Organizer.", N(Pad.B.L1), logic, N(Pad.B.R1), organizer) + " " +
                F("Press {0} for the options.", N(Il2CppGK_System.Controller.ButtonType.Start)) + " " +
                "Move and move the cursor with the left stick or the D-pad; the right stick turns evidence and looks around.";
        }
    }
}
