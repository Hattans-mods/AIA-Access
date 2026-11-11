# AAI Access

An accessibility mod for blind and low-vision players of Ace Attorney Investigations Collection on PC (Steam). Version 1.1.0

AAI Access reads the game aloud through your screen reader and helps you play every part of both games without seeing the screen: dialogue, menus, the Organizer, Logic, investigations, examinations, deductions, 3D evidence, Logic Chess, the security video, luminol and fingerprint tests. It also works with the free demo. You can play with the keyboard or a controller, in all the game's languages.

This is an unofficial fan project. It is not made, approved or supported by Capcom.

## What you need

• Ace Attorney Investigations Collection, or its free demo, on Steam for Windows.  
• A screen reader. The mod speaks through the one you have running (NVDA, JAWS, ZoomText, ZDSR, PC-Talker, SenseReader, System Access and others); without one, it speaks with the Windows voice.  
• An internet connection the first time you start the game with the mod.  
• If the game shows an error about .NET when it starts, install the ".NET 6 Desktop Runtime (x64)" from Microsoft's website, then start the game again. The installer tells you if it is missing.

## Installing with the installer (easiest)

1. Download "AAIAccess-Installer.exe" from the [latest release](https://github.com/Hattans-mods/AIA-Access/releases/latest).
2. Run it. It finds the game in your Steam libraries (or press Browse to choose its folder) and tells you what is installed and what is newest.
3. Press Install. It downloads the newest version and installs it. When it is done, start the game from Steam.

The same installer updates the mod (press Update) and removes it (press Uninstall). Your mod settings, room names and the spots you have checked are kept when you update.

## Installing by hand

1. Find the game's folder. In Steam, open your Library, select the game, open its Properties, choose Installed Files, then Browse. The usual place is: `C:\Program Files (x86)\Steam\steamapps\common\Ace Attorney Investigations Collection` (the demo's folder ends in "DEMO").
2. Download the latest release zip (AAIAccess-1.1.0.zip) and copy everything in it into that folder: the MelonLoader, Mods and Plugins folders and the files version.dll, nvdaControllerClient64.dll and prism.dll. If Windows asks about replacing files, choose to replace them.
3. Start the game from Steam.

To update by hand, do the same with the new zip; your UserData folder is not in the zip, so it is kept.

The first start takes a few minutes and may look frozen. MelonLoader (the mod loader) prepares the game's code once, and downloads some tools it needs. Leave it running. Later starts are quick.

When the game asks you to press OK on its first messages, the mod reads them to you. Press Space to confirm.

## Updates

When a new version is out, the mod tells you on the title screen: "AAI Access 1.2.0 is available. Run the installer to update." It only reads the version number of the latest release on GitHub; it sends nothing about you. To switch this off, put the word `noupdate` in `UserData\AAIAccess_off.txt`.

## The keys

Press F1 at any time to hear help for the screen you are on.

• "F1" Help for the current screen.  
• "F2" Repeat the last thing said.  
• "F3" The status of the current screen: what the cursor is on, how a puzzle stands, the current video frame...  
• "F4" The name of the room you are in. To rename a room, copy the name you want, then press Shift+F4.  
• "H" How full your truth gauge is (in Logic Chess: how much time is left).

Walking around (investigations):

• "Right bracket" The next person or thing in the room, nearest first. "Left bracket" for the previous one.  
• "Backslash" Walk to the chosen thing on your own. Press it again to stop.  
• "Apostrophe" Track it with sound. The sound comes from the next turn on the way there: left or right, higher when it is further up the room, lower when further down, louder and faster as you get closer. Two notes mean you are there. Press apostrophe again to stop.

When you are close to something you hear Examine, or Talk for a person. A single beep means you checked it before.

Close-ups, deductions, 3D evidence, the video, luminol and fingerprints:

• "Right and left bracket" Move the cursor straight to the next or previous spot, detail or trace.  
• "F3" What the cursor is on, and how the puzzle stands.

The game's own keys, if you haven't changed them: Space confirms, Z cancels or runs, E opens the Organizer, Q opens Logic, T calls your partner, presents or points out a contradiction, R opens the history, G opens the options. W, A, S and D move, and the arrow keys turn evidence and look around. If you change the game's keys in its options, the mod follows them: the help names your keys, and walking on your own uses them.

## Playing with a controller

The mod names the buttons the way they are printed on your controller: Xbox (A, B, X, Y, LB, RB...), PlayStation (Cross, Circle, Square, Triangle, L1, R1...), Switch Pro, or by place for other controllers ("the bottom button"). It also finds a PlayStation or Switch controller that Steam Input passes to the game as an Xbox controller. Pressing a button stops the speech, as a key press does with your screen reader.

Hold the "left stick button (L3)" and press:

• "Start" (Options, Menu) Help for the current screen.  
• "Bottom button" Repeat the last thing said.  
• "Right button" The status of the current screen.  
• "Left button" How full your truth gauge is.  
• "Top button" The name of the room you are in.  
• "R1 / L1" Walk to the chosen thing on your own / track it with sound. Do it again to stop.  
• "R2 / L2" The next / previous person, thing, spot or detail.  
• "D-pad" Right next, left previous, down walk, up track.

While you hold L3, the game doesn't see the other buttons, so they don't also do something in the game.

Without holding anything, "R1 and L1" pick the next and previous person, thing, spot or detail while you walk around and in close-ups, deductions, 3D evidence, the video, luminol and fingerprints.

The game's own buttons: the bottom button confirms, the right button cancels or runs, the top button calls your partner, presents or points out a contradiction, the left button opens the history, the triggers (and the shoulder buttons outside the screens above) open Logic and the Organizer, Start opens the options.

To use another button than L3 to hold, put its name in `UserData\AAIAccess_pad.txt`: L3, R3, Select, Start, L1, R1, L2 or R2. If the mod names the buttons for the wrong controller, add ps5, ps4, xbox, switch or other, for example `L3 ps5`. The word `off` turns the mod's controller buttons off.

## Languages

The mod speaks in the game's text language (Options, Language): Japanese, English, French, German, Korean, Traditional Chinese or Simplified Chinese. The translations of the mod's own words were made by machine; corrections from native speakers are very welcome.

## If something goes wrong

The mod writes what it does to a log: `MelonLoader\Latest.log` in the game's folder. If something doesn't work, please send that file along with a short description of what happened. The installer writes its own log: `%TEMP%\AAIAccess-installer\installer.log`.

`UserData\AAIAccess_off.txt` can hold these words:

• `off` Turns the mod off without uninstalling it. Delete the file to turn it back on.  
• `noupdate` The mod doesn't check for new versions.  
• `noprism` Speech goes straight to NVDA (or the Windows voice) instead of through Prism.

You can add or correct descriptions of close-up pictures in `UserData\AAIAccess_closeups.txt`, one per line: `picture name|description`

## Uninstalling

The easiest way is the installer's Uninstall button. By hand: delete version.dll from the game's folder; the game then starts without any mods. To remove everything, also delete the MelonLoader, Mods, Plugins and UserData folders, nvdaControllerClient64.dll and prism.dll, plus the files GK12CodeDump.dll and GK12CodeDump.txt if they are there.

## How it works

Ace Attorney Investigations Collection keeps its program code inside GK12.exe instead of in a separate file, which MelonLoader needs. The small plugin in the Plugins folder (AAIAccessLoaderFix.dll) solves this: when the game starts, it saves a copy of the game's code from memory as GK12CodeDump.dll in the game's folder and points MelonLoader at it. It only reads; nothing in the game's own files is changed. Keep the plugin: the mod can't load without it.

## Credits and licences

AAI Access is released under the MIT licence ([LICENSE](LICENSE)). MelonLoader is by LavaGang, under the Apache 2.0 licence (in the release zip's Licenses folder). nvdaControllerClient64.dll is part of NVDA by NV Access, under the GNU LGPL 2.1 (in the release zip's Licenses folder). prism.dll is [Prism](https://github.com/ethindp/prism) (version 0.18.2) by ethindp, under the Mozilla Public License 2.0 (in the release zip's Licenses folder). Thanks to the Phoenix Wright: Ace Attorney Trilogy accessibility mod by AccessMods, which inspired several features. Ace Attorney and Ace Attorney Investigations are trademarks of Capcom.

## Building from source

You need the .NET 8 SDK (or newer) and the game with MelonLoader 0.7.3 installed and started once.

1. Make a `lib` folder next to this README, with two folders in it:  
   • `lib/ml`: MelonLoader.dll, 0Harmony.dll and the Il2CppInterop.*.dll files from the game's MelonLoader/net6 folder.  
   • `lib/il2cpp`: everything from the game's MelonLoader/Il2CppAssemblies folder (made by MelonLoader on the first start).

   These come from your own copy of the game and MelonLoader, so they are not in this repository.
2. Build the mod with `dotnet build AAIAccess/AAIAccess.csproj -c Release` and the plugin with `dotnet build LoaderFix/LoaderFix.csproj -c Release`.
3. Copy AAIAccess.dll to the game's Mods folder and AAIAccessLoaderFix.dll to its Plugins folder. prism.dll (in `redist`) goes next to GK12.exe.

The installer is built with `dotnet build Installer/AAIAccessInstaller.csproj -c Release`. It runs on the .NET Framework 4.8 that comes with Windows.

For development, add `-p:DevTools=true` when building the mod. This adds tools for working on the mod (F9 exports evidence page pictures, F10 writes map data to the log, F11 writes everything on screen to the log, the game's button assignments are written to the log, and a few crash-test switches). Release builds don't include them.
