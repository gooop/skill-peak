# SkillPeak

A small BepInEx mod for Valheim. In the skills menu, it draws a thin `|` tick
mark on each skill's level bar at the highest level that skill has ever
reached — even after death drops the skill's current level. That tells you
what to grind back toward instead of just seeing the post-death number.

## How it works

- Patches `Skills.Skill.Raise` to record a per-skill high-water mark in memory.
- Patches `SkillsDialog.Setup` to draw/update a thin colored bar segment
  ("tick") on the `currentlevel` bar element at `peak / 100` of its width.
- Patches `Player.Save` / `Player.OnSpawned` to persist the peaks into the
  character's own save data (`m_customData`), so they survive logout, death,
  and reloading.

No server-side component, no dependency on other mods.

## Build

You need a local Valheim install (for the game's `Assembly-CSharp.dll` and
Unity DLLs) plus a BepInEx install (for `BepInEx.dll` and `0Harmony.dll`).

1. Install [BepInEx 5 (Valheim pack)](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
   into your Valheim folder if you haven't already, and run the game once so
   BepInEx generates its files.
2. Point the build at your install (adjust the path for your system):

   ```sh
   export VALHEIM_INSTALL_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
   ```

   (On the default macOS Steam layout this is already the fallback in the
   `.csproj`, so this step is optional there.)
3. Build:

   ```sh
   dotnet build
   ```

   The build copies `SkillPeak.dll` straight into
   `<Valheim>/BepInEx/plugins/SkillPeak/`. Restart Valheim (fully quit, not
   just close the menu) to load it.

If your BepInEx core DLLs live somewhere other than `BepInEx/core/` (some
installs vary), edit the `BepInExCoreDir` property at the top of
`SkillPeak.csproj`.

## Notes

- The tick reflects the highest level ever recorded *by this mod*. If you
  install it on a character that's already lost levels to death, it seeds
  from the current (already-lowered) levels on first load — so the true
  peak from before installing the mod won't be known.
- Toggle the tick off via the generated BepInEx config
  (`BepInEx/config/gooop.valheim.skillpeak.cfg`, `General.Enabled`).
