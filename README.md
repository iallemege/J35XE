# J-35XE

Closed-source **Nuclear Option** aircraft for BepInEx. Source is not included.

Download the zip from **Releases**. This repository does not ship source.

## Install

1. Install BepInEx 5 for Nuclear Option.
2. Download `J35XE_v0.1.zip` from Releases.
3. Unzip it into `BepInEx/plugins/`. The zip root belongs directly in `plugins`: `SaabJ35.baas`, `J35XE_2.6.55.dll`, `J35XE.ssbag`, `BIA.Runtime_1.0.29.dll`, and `J35XE_readme.txt`. Do not nest a `BepInEx` folder.
4. Delete older `J35XE_*.dll` files. Delete `BIA.Runtime_1.0.25.dll` if it is still there. Do not keep `SaabJ35.nobp` in plugins at the same time.
5. Fully quit the game, then start it again.

Rb 24 and Rb 28 are included. Ash Weapon Pack is not required.

## 0.1

First stable release. The runtime file inside the zip is `J35XE_2.6.55.dll`. Loading screens stay in `load/`. Rb 24 and Rb 28 use the Ash body meshes, the painted skin PNGs, and the Ash placeholder card. The wing gun is the 30mm autocannon. Faction AI can draw this aircraft from airframe stock.

- Before G2E is on, the cockpit shows `Press U to turn on the G2E`.
- F7 opens the cockpit layout menu. Saved layouts stay in `BepInEx/plugins/J35XE/Cockpits`.
- H arms SGLOC below the speed of sound. `[` and `]` pick how fast the nose swings. Centering the stick drops the mode.
- A hard pull lays vapor along both wings.
- Rb 24 and Rb 28 ship with this aircraft. The body, icon, and description match the Ash pack. Names end with `[OAS-M]`. Ash Weapon Pack is not required.
- Workshop liveries: subscribe to an Aircraft Livery whose aircraft is J-35XE, or whose AircraftKey is SaabJ35.

## License

Proprietary. All rights reserved. See `LICENSE`.
