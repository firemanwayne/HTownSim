# Neighborhoods

Houston's street grid is not one grid. Midtown's streets run about 35 degrees off true north; Montrose and Hyde Park (around Fire
Station 16) run almost north-south. The city builders and the traffic code assume one axis-aligned grid, so a district with a
different angle came out with axis-aligned buildings on rotated lots and no traffic.

A **neighborhood** is a district built in its own grid frame and then rotated into the city:

```
MidtownReal                       (world frame, never rotated)
  ...Midtown and the Station 8 blocks, built as before
  Neighborhood_Station16          root rotated to the neighborhood's grid angle
    Ground, ParcelSlabs, Buildings, Roofs, Markings, Crosswalks, Signals, trees, props, parked cars,
    FireStation16, IncidentSites
  Traffic_Station16               a TrafficSystem for that frame
  NeighborhoodFill                plain ground over the gap between Midtown and the neighborhood
```

Inside the root everything is axis-aligned in the neighborhood's own frame, so the existing builders and traffic code run unchanged.
The root's yaw is `-(35.06 - <neighborhood grid angle>)` degrees (35.06 is Midtown's), about the world origin, which is the
same origin the GIS projection uses, so no translation is needed.

## Building one

1. **Data.** `NEIGHBORHOOD=station16 node AgentScripts/PrepareMidtownData.js` downloads the City parcels, footprints and streets in
   the neighborhood's own frame (angle and window come from the `NEIGHBORHOODS` table at the top of the script) into
   `Assets/HTownSim/Data/Neighborhood_Station16.txt`. Use a fresh `MIDTOWN_CACHE` folder per run. The file's `R` line holds the
   grid angle and `H 16` the station, in the neighborhood's frame.
2. **Unity.** With the scene saved and Play Mode off, run `BuildRealMidtown.ExpandStation16`
   (`unity command script run --file AgentScripts/BuildRealMidtown.cs --entry BuildRealMidtown.ExpandStation16`). It replaces its
   own earlier output, builds the neighborhood, copies the Midtown `TrafficSystem` and writes this neighborhood's street axes, frame yaw
   and signals into the copy, rotates the root, widens the playable area to cover Midtown plus the neighborhood, and saves.
3. **After it:** `AddCityDetail.Run` (shop signs and lot furniture), then `AddCityMap.Build` and `UseBlenderCityMap.Apply` for the
   HUD map, and `AddStation8Apparatus.Apply` if the Station 8 building was rebuilt.

`MidtownExpanded.txt` (Midtown plus the Station 8 blocks) is unchanged: `EXPANDED=1 node AgentScripts/PrepareMidtownData.js`.

## The HUD map

The map covers the whole playable area (scene x -2250..560, z -2350..1575). The Blender map scripts use the same window
(`fetch_houston.js`, `build_map.py`, `render_all.py`; `MIDTOWN_CACHE_DIRS` points the fetch at the data download's cache folders).
Copy `Blender/HTown/renders/htown_map_hud.png` to `Assets/HTownSim/Textures/Map/CityMapBlender.png` and apply it. Station 16 is
marked at the City's geocode for 1700 Richmond Ave (scene x -1941, z -2059).

## What is and isn't there

- Station 16's neighborhood is about 1.3 x 1.3 km (about 1,900 lots) around the station. Between it and Midtown there is plain ground:
  drivable, but no buildings. Further neighborhoods would fill that gap, each with its own definition.
- Cars and pedestrians follow the neighborhood's own streets (a second `TrafficSystem`), with signals and crosswalks on Richmond Ave.
  They do not cross between a neighborhood and Midtown.
- The building, ground and tree upgrades from the Blender passes still cover Midtown only.
- There is no apparatus at Station 16 yet and it is not on the start screen.

## Adding another neighborhood

Add an entry to `NEIGHBORHOODS` in `PrepareMidtownData.js` (centre, grid angle, half size, file), add a matching `NeighborhoodDef` in
`BuildRealMidtown.cs` (window in the neighborhood's frame, signal street) with an `ExpandXxx` entry, and repeat the steps above. The grid
angle is the bearing that makes the neighborhood's streets axis-aligned; the data script prints an estimate
("Estimated rotation over the expanded window"), but a value near 90 means the grid is about 0 (the estimate is modulo 90).

## Editor tip

Objects built by a script and then moved (here, by rotating the neighborhood root) can show stale, un-rotated copies in the Scene and Game
views until the scene is reloaded. If buildings look axis-aligned over rotated lots after a build, reopen the scene before
judging it; the saved transforms are correct.

## Filling the gap

Between Midtown and the Station 16 neighborhood there are about 6,500 more lots. `BuildRealMidtown.ExpandGap` fills them with a first
layer, in the world frame (Midtown's), so the whole area is drivable:

- **Roads:** an asphalt base over the part of the data rectangle outside Midtown. The gaps between lots are the streets, as in the other builders.
- **Lots:** each lot gets its sidewalk, curb and surface (grass, paving or parking), with a mesh collider on the sidewalks.
- **Building squares:** a plain gray box per built lot, turned to the lot's own angle (the minimum-area rectangle of the lot polygon),
  pulled in from its edges, with a height from the floor count (3.4 to 80 m) and a collider.
- Lots already built (the Midtown window and the Station 16 neighborhood) are left out. Everything is under `Gap_Placeholders` and the pass
  replaces its own earlier output.

Data: `NEIGHBORHOOD=gap node AgentScripts/PrepareMidtownData.js` writes `Assets/HTownSim/Data/Gap_Southwest.txt`
(the whole rectangle in Midtown's frame; use the same `MIDTOWN_CACHE` folder as an earlier download to skip the download).

Not there yet: lane markings, crosswalks, signals and traffic in the gap, real buildings, trees, props and parked cars, and the far
south-west corners outside the data (plain ground). The next step is real neighborhoods for the gap, built the same way as Station 16 and
replacing the placeholders under their area.

## Which neighborhoods are next

Measuring the street angle of every lot edge in 300 m cells (relative to Midtown's grid) shows the gap is mostly two grids:

- **Montrose grid (about 36 degrees from Midtown's):** the whole west and south-west, from x -2400 to about -900, the same angle as Station 16's
  neighborhood. These neighborhoods can share Station 16's frame (grid angle 0.1) and only differ in their window, so each one is
  one more `NeighborhoodDef` with the same `GridDegrees`.
- **Midtown grid (about 0 degrees):** south of Midtown from x -600 to 560. `SouthMidtown` (x -845..560, z -1450..-450, `ExpandSouthMidtown`) is the first of these;
  its root is not turned.
- A mixed strip around the US-59 corridor (x -900 to -600) where the angle changes cell by cell. Neighborhood boundaries should follow the freeway there.

`AllNeighborhoods` in `BuildRealMidtown.cs` lists every neighborhood built so far. The gap pass leaves their lots out, and the playable area
and `NeighborhoodFill` cover all of them. After building a neighborhood, run `ExpandGap` again (it rebuilds the placeholders around it) and
`AddCityDetail.Run`.

## US 59

The Southwest Freeway runs diagonally through the gap and the neighborhoods. `BuildRealMidtown.AddUs59Freeway` builds it as raised
carriageways (a 9 m deck with barriers, asphalt, lane paint and columns, the same look as the freeway along Midtown's east edge) along the City's
road centre lines: one 16 m deck per carriageway line, 105 segments and about 7.5 km. Data: `node AgentScripts/ExtractFreeway.js` writes
`Assets/HTownSim/Data/US59.txt` from `Blender/HTown/data/htown_map.json` (only the south-western run; the Midtown-edge part is the existing
deck). It is scenery: there are no ramps or frontage-road logic, the streets pass underneath, and it is not part of any neighborhood, so it
survives neighborhood rebuilds. Re-run the pass to replace it.

## Building footprints

Buildings are boxes built from the 2018 LiDAR footprint bounds, which do not line up with the tax lots (in South Midtown 30% of the boxes reached
more than a sidewalk's width past their own lot, the worst by 74 m, and many overlapped their neighbours). When any expansion or neighborhood is
built (`_expanding`), each footprint box is first clamped to its lot's bounds (`FootprintSlack`, 0.5 m), then `ResolveFootprintOverlaps` moves the
shared edge of any two overlapping boxes from different lots to the middle of the overlap, never making a box smaller than 3 m. Midtown's original,
Blender-rebuilt buildings are not affected. This is a stop-gap until buildings use oriented footprint rectangles with their own angle.
