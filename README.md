# SR5-Save-Editor

A fan-made save editor for **Saints Row (2022)** on Windows. Unofficial, and independent of Volition, Deep Silver and Epic.

Every value in your save is here to change: money, weapons, vehicles, your character, unlocks, missions and more, with a backup made every time you save.

## What it edits

- **Overview:** cash, XP, level, empire tier, waiting cash, Boot Hill coins and XP, where you start, the "used cheats" mark.
- **Weapons:** own any weapon, and set its level, upgrades and ultimate. Pick one for its skin, material, ammo and every other option.
- **Garage:** add and remove vehicles, and upgrade them. Pick a car for every option it has: performance, sound, paint and finish
  with colour pickers, each material, wraps, parts, wheels and underglow. Set one option on every car, or copy a look between cars
  of the same vehicle.
- **Character:** skin, every outfit slot and what's in it, body and voice.
- **Loadout:** difficulty sliders, equipped perks, emotes and HQ theme.
- **Unlocks:** skills, perks and slots, emotes, clothing, weapon skins and decals, vehicle models, parts and paint, HQ props, gang
  members, radio and fast travel, with **Unlock everything** for the lot. Vehicle models and story and progress lists, such as homies,
  achievements, tutorials and treasure, are yours to pick by hand.
- **Stats:** kills, distance, play time and the rest.
- **Tables:** every kind of record in the save, one column per value: missions, challenges, side hustles, ventures, the weapon
  wheel, Boot Hill and all the rest, with **For all listed** to set a whole column.
- **All data:** every value in the save, by name, including long lists.
- **Inventory:** everything in the save and everything the editor can add, with its category, state and screen. Search it, filter it,
  and double-click to go there. Items from your own mods are labelled and stay with the saves that have them.

## Using it

1. Close Saints Row.
2. Run `SR5-Save-Editor.exe`, or build it with `build.bat`. If Windows asks about the publisher, choose **More info**, then **Run anyway**.
   The editor finds Epic saves by itself, and **Folder...** opens any other location.
3. Pick a save at the top, change what you want, and press **Save** or **Save as new**.

**Save as new** keeps your original and adds a new entry to the load menu. Start the game to see it, and load it from the main menu.

Every **Save** first copies your files to `%LOCALAPPDATA%\SR5SaveEditor\backups`, and **Restore backup...** puts any of them back.

## Tips

- **Keep your runs apart.** All saves share `AUTOSAVE1` and `AUTOSAVE2`, so make a manual save of a run you care about before
  playing another.
- **Try values and look.** Parts, wraps and wheels are numbered per vehicle. The About column shows the values real saves use, and `?`
  marks a value a stock car hasn't set yet.
- **DLC items come with their pack.** The Merry Saintsmas pack, for example, brings Santa's Truckster, its clothing and three emotes.
- **Add XP to level up.** The game levels you up on your next XP gain and gives each level's rewards.
- **Epic cloud saves:** your saves are in a folder Epic can sync. To keep full control of your files, you can turn cloud saves off for
  the game in the Epic launcher.
- **Plenty of room:** Unlock everything used about 60% of the save's fixed size in the largest save tried, and the editor checks the size
  before it writes.

## Building

`build.bat` needs only Windows: it uses the C# compiler in the .NET Framework that comes with it.

`data\` holds tables built into the program:

| File | From |
|---|---|
| `names.txt`, `weapons.txt`, `vehicles.txt`, `vehicle_variants.txt`, `garage_options.txt` | `python -I tools\gen_data.py SAVES GAME SR5_TWEAKS` (your saves, the game files, the SR5-Tweaks research) |
| `catalog.bin` | `build\Tests.exe --build-catalog SAVES data\catalog.bin`: blank records for items a save can gain, with player data removed |
| `custom_items.txt` | By hand: names from your own mods |

Rebuild after regenerating them. Your own saves go in `samples\original`, which stays on your PC.

## Inventory lists

`inventory\inventory_types.csv` and `inventory_items.csv` list every kind of record and every item, with its category and screen.
Rebuild them with `build\Tests.exe --inventory samples\original inventory`.

## Tests

```bash
test.bat
```

Against every save in `samples\original`:

- Reading and rewriting each save gives back the identical file, byte for byte.
- Changing cash changes only those bytes in the save, and the load menu's copy follows.
- Adding records leaves other sections as they were, and the result reads back identically.
- Unlock everything works on every save and stays within the file's size.
- Damaged files and oversized saves are caught before anything is written, and backups hold the original.
- `tools\sr5save.py`, a separate decoder that shares no code with the editor, reads all of the editor's output.

`SR5-Save-Editor.exe --ui-check SAVE OUTDIR` opens the real window off-screen with a copy of a save, draws each screen to a
PNG, edits through the controls, saves, and reads the result back.
