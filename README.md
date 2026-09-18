# SkillPeak

A small BepInEx mod for Valheim. In the skills menu, it draws a tick
mark on each skill's level bar at the highest level that skill has ever
reached.

## Notes

- The tick reflects the highest level ever recorded *by this mod*. If you
  install it on a character that's already lost levels to death, it seeds
  from the current (already-lowered) levels on first load.
- Toggle the tick via the generated BepInEx config
  (`BepInEx/config/gooop.valheim.skillpeak.cfg`, `General.Enabled`).
