namespace AAIAccess
{
    /// <summary>F1: explains the current screen. Key names are the game's default keyboard layout.</summary>
    internal static class Help
    {
        private const string ModKeys =
            "Mod keys: F1 help for the current screen. F2 repeats the last thing said. F3 gives the status of the current puzzle. H tells you how full your truth gauge is. F4 says the name of the room you are in; to rename a room, copy the name you want and press Shift F4. " +
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
            "Press the right bracket key for the next spot in the picture and the left bracket key for the previous one, then press Space to point it out. " +
            "If the contradiction is about evidence instead, open the Organizer with E, choose the evidence and present it. " +
            "A wrong answer lowers your truth gauge, so think about what doesn't fit before you answer. F3 describes the picture and tells you where the cursor is.";

        private const string OrganizerHelp =
            "Organizer. Your evidence and the profiles of the people involved. Move through the entries with W, A, S, D; each is read with its description. " +
            "When you are asked to present, choose the evidence and press T to present it. Press Z to close. F3 reads the highlighted entry again.";

        private const string ChessHelp =
            "Logic chess. Your opponent is holding back; a time gauge runs down while you think. H tells you how much time is left, and the mod warns you as it runs low. " +
            "Choose what to ask from the list with W and S; each choice names its chess piece. Stay silent lets your opponent speak instead.";

        public static void Speak()
        {
            string text;
            if (LogicChess.Active) text = ChessHelp;
            else if (Organizer.Active) text = OrganizerHelp + " " + Organizer.TabKeys();
            else if (LogicScreen.Active) text = LogicHelp;
            else if (ExamineScreen.Deduction || ExamineScreen.DeductionOutside) text = DeductionHelp;
            else if (ExamineScreen.Active) text = ExamineHelp;
            else if (Examine3D.Active || Gk2Examine3D.Active) text = Examine3DHelp;
            else if (Gk2Luminol.Active) text = LuminolHelp;
            else if (Gk2Finger.Active) text = FingerHelp;
            else if (Investigation.InInvestigation) text = InvestigationHelp + " " + ModKeys;
            else text = ModKeys + " " + GameKeys;
            Speech.Say(text, true, false);
        }
    }
}
