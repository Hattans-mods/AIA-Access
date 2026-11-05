# AAI Access

An accessibility mod for blind and low-vision players of Ace Attorney Investigations Collection on PC (Steam).
Version 1.0.0

AAI Access reads the game aloud through your screen reader and helps you play every part of both games without
seeing the screen: dialogue, menus, the Organizer, Logic, investigations, examinations, deductions, 3D evidence,
Logic Chess, the security video, luminol and fingerprint tests. It also works with the free demo.

This is an unofficial fan project. It is not made, approved or supported by Capcom.


## What you need

- Ace Attorney Investigations Collection, or its free demo, on Steam for Windows.
- NVDA is recommended. Without NVDA, the mod speaks with the Windows voice instead.
- An internet connection the first time you start the game with the mod.
- If the game shows an error about .NET when it starts, install the ".NET 6 Desktop Runtime (x64)" from Microsoft's
  website, then start the game again.


## Installing

1. Find the game's folder. In Steam, open your Library, select the game, open its Properties, choose
   Installed Files, then Browse. The usual place is:
   C:\Program Files (x86)\Steam\steamapps\common\Ace Attorney Investigations Collection
   (the demo's folder ends in "DEMO").
2. Download the latest release zip and copy everything in it into that folder: the MelonLoader, Mods and Plugins folders and the files
   version.dll and nvdaControllerClient64.dll. If Windows asks about replacing files, choose to replace them.
3. Start the game from Steam.

The first start takes a few minutes and may look frozen. MelonLoader (the mod loader) prepares the game's code
once, and downloads some tools it needs. Leave it running. Later starts are quick.

When the game asks you to press OK on its first messages, the mod reads them to you. Press Space to confirm.


## The keys

Press F1 at any time to hear help for the screen you are on.

F1   Help for the current screen.
F2   Repeat the last thing said.
F3   The status of the current screen: what the cursor is on, how a puzzle stands, the current video frame...
F4   The name of the room you are in. To rename a room, copy the name you want, then press Shift+F4.
H    How full your truth gauge is (in Logic Chess: how much time is left).

Walking around (investigations):
Right bracket   The next person or thing in the room, nearest first. Left bracket for the previous one.
Backslash       Walk to the chosen thing on your own. Press it again to stop.
Apostrophe      Track it with sound. The sound comes from the next turn on the way there: left or right,
                higher when it is further up the room, lower when further down, louder and faster as you get
                closer. Two notes mean you are there. Press apostrophe again to stop.
When you are close to something you hear Examine, or Talk for a person. A single beep means you checked it before.

Close-ups, deductions, 3D evidence, the video, luminol and fingerprints:
Right and left bracket   Move the cursor straight to the next or previous spot, detail or trace.
F3                       What the cursor is on, and how the puzzle stands.

The game's own keys, if you haven't changed them: Space confirms, Z cancels or runs, E opens the Organizer,
Q opens Logic, T calls your partner or presents, R opens the history, G opens the options.
W, A, S and D move, and the arrow keys turn evidence and look around.


## If something goes wrong

The mod writes what it does to a log: MelonLoader\Latest.log in the game's folder. If something doesn't work,
please send that file along with a short description of what happened.

To turn the mod off without uninstalling it, make a text file called AAIAccess_off.txt in the game's UserData
folder, containing the word off. Delete the file to turn the mod back on.

You can add or correct descriptions of close-up pictures in UserData\AAIAccess_closeups.txt, one per line:
picture name|description


## Uninstalling

Delete version.dll from the game's folder; the game then starts without any mods. To remove everything, also
delete the MelonLoader, Mods, Plugins and UserData folders and nvdaControllerClient64.dll, plus the file
GK12CodeDump.dll and GK12CodeDump.txt if they are there.


## How it works

Ace Attorney Investigations Collection keeps its program code inside GK12.exe instead of in a separate file, which
MelonLoader needs. The small plugin in the Plugins folder (AAIAccessLoaderFix.dll) solves this: when the game
starts, it saves a copy of the game's code from memory as GK12CodeDump.dll in the game's folder and points
MelonLoader at it. It only reads; nothing in the game's own files is changed. Keep the plugin: the mod can't load
without it.


## Credits and licences

AAI Access is released under the MIT licence (LICENSE).
MelonLoader is by LavaGang, under the Apache 2.0 licence (in the release zip's Licenses folder).
nvdaControllerClient64.dll is part of NVDA by NV Access, under the GNU LGPL 2.1 (in the release zip's Licenses folder).
Thanks to the Phoenix Wright: Ace Attorney Trilogy accessibility mod by AccessMods, which inspired several features.
Ace Attorney and Ace Attorney Investigations are trademarks of Capcom.


## Building from source

You need the .NET 8 SDK (or newer) and the game with MelonLoader 0.7.3 installed and started once.

1. Make a `lib` folder next to this README, with two folders in it:
   - `lib/ml`: `MelonLoader.dll`, `0Harmony.dll` and the `Il2CppInterop.*.dll` files from the game's `MelonLoader/net6` folder.
   - `lib/il2cpp`: everything from the game's `MelonLoader/Il2CppAssemblies` folder (made by MelonLoader on the first start).
   These come from your own copy of the game and MelonLoader, so they are not in this repository.
2. Build the mod with `dotnet build AAIAccess/AAIAccess.csproj -c Release`
   and the plugin with `dotnet build LoaderFix/LoaderFix.csproj -c Release`.
3. Copy `AAIAccess.dll` to the game's `Mods` folder and `AAIAccessLoaderFix.dll` to its `Plugins` folder.

For development, add `-p:DevTools=true` when building the mod. This adds tools for working on the mod
(F9 exports evidence page pictures, F10 writes map data to the log, F11 writes everything on screen to the log,
and a few crash-test switches). Release builds don't include them.
