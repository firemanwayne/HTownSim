# HTown map (Blender)

A restyled city map for HTownSim, built in Blender from City of Houston GIS data. It covers the same ground as
the Unity world, in the same coordinates.

## Outputs (`renders/`)

| File | Size | Use |
| --- | --- | --- |
| `htown_map_hud.png` | 1421x2048 | Top-down map without street trees, for the 350 px HUD map |
| `htown_map_detail.png` | 2842x4096 | Top-down map with street trees, for a full-screen or zoomed map |
| `htown_hydrants.png` | 2842x4096, transparent | Fire hydrant dots only, to layer over either map |
| `htown_hero.png` | 1920x1080 | Three-quarter view at dusk |
| `preview_*.png` | | Previews at HUD size and close up |

Each top-down image covers exactly x -845..560, z -450..1575 in Unity scene metres, which is the window of the
`CityMap` camera, with +Z at the top. Pixel to world: `x = -845 + u * 1405`, `z = 1575 - v * 2025` (u, v from 0
to 1, measured from the top left).

## Data (`data/htown_map.json`)

`scripts/fetch_houston.js` reuses HTownSim's GIS caches (parcels, 2018 LiDAR footprints, METRORail) and adds
City of Houston layers found through the COH ArcGIS catalog:

- Road centrelines (`COH_RoadCenterline`): road class (freeway, ramp, major, local) and one-way direction
- Park boundaries (`COH_Parks_view`): Baldwin, Root Memorial Square, Glover, Antioch, Bob Smith
- HFD fire stations (`COH_Houston_Fire_Stations_view`): Stations 7 and 8 land within 2 cm of where the game puts them
- Fire hydrants (`Public_Safety/MapServer/9`): 465 in the area

Building heights are the floor counts HTownSim uses, matched per parcel (1,805 of 2,155 footprints). Footprints
under 80 m² are kept to one floor. Everything else is one floor.

## Regenerating

```powershell
node scripts/fetch_houston.js
```

Then in Blender's Python console (or through the MCP):

```python
exec(open(r"J:\Blender\Projects\HTown\scripts\build_map.py").read())
exec(open(r"J:\Blender\Projects\HTown\scripts\render_all.py").read())
```

The build takes about 90 seconds, most of it spent placing street trees. Rendering takes about 6 seconds and
then saves `HTownMap.blend`.

## Buildings

`scripts/build_buildings.py` rebuilds all of HTownSim's buildings (shops, apartments, towers and houses) on their real outlines. House roofs sit on each outline's minimum-area rectangle. Their pitch, gable/hip choice, shingle colour and chimney come from the same name hash Unity uses, so they match what the game picked. Outlines that fill less than 80% of that rectangle get a flat roof instead. It reads two files:
- `data/unity_buildings.json`, written by Unity's `ExportBuildings.Run`: heights, wall materials and which side faces the street.
- `data/htown_map.json`: the building outlines.

It writes `data/upgraded_buildings.txt`, which Unity's `UpgradeBuildings.Apply` reads. The facade textures come from `scripts/make_facades.py` (system Python with Pillow) and go to `textures/`. Copy them to HTownSim's `Assets/HTownSim/Textures/Facades`. Each tile is a whole number of bays and floors, so window rows land on the real floor lines.

Comparison shots are in `renders/`:
- `unity_before_grid.png` and `unity_after_grid.png`: the same four views before and after.
- `crop_after_*.png`: close-ups.
- `unity_houses_compare.png`: a house block from above and at street level, before and after.

## Ground, trees and building variety

Run these in Blender in this order, then the matching Unity script (`UpgradeBuildings`, `UpgradeGround`, `UpgradeTrees`, entry `Apply`):

1. `scripts/build_buildings.py`. It also adds wall variants, porches, balconies and entrances, and writes `data/footprints_used.json`.
2. `scripts/build_ground.py` writes `data/upgraded_ground.txt`: lot surfaces from HCAD land use, driveways, walks, beds, shrubs, fences and stalls.
3. `scripts/build_trees.py` writes `data/tree_models.txt` and `data/tree_places.txt`: species models and the City of Houston tree inventory.

`scripts/geom.py` holds the shared geometry helpers. `node scripts/fetch_houston.js` also downloads the tree inventory and land use. `python scripts/convert_ground_textures.py` turns the Poly Haven scans in `textures/polyhaven/` into HTownSim detail maps. `python scripts/make_facades.py` writes the facade tiles and their `_emit` lit-window maps; copy `textures/*.png` to HTownSim's `Textures/Facades`.

Poly Haven assets used, all CC0: asphalt_04, leafy_grass, grass_ground, concrete_pavement_03, yellow_flower_mulch, bark_brown_02 and brown_planks_05. Comparison shots: `renders/unity_ground_compare.png`, `unity_trees_compare.png` and `unity_variety_compare.png`.

## Style

The style is a dark dispatch-console map in HTownSim's palette, made to sit on the HUD's navy panel:

- Brick-toned homes, concrete-grey commercial buildings, pale towers
- Red station lots, the amber METRORail line, raised freeway decks that cast shadows
- Sun from the upper left
- No baked-in labels: Unity draws the street names and markers itself
