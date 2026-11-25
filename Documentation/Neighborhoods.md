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

## Texas Medical Center

`NEIGHBORHOOD=medcenter node AgentScripts/PrepareMidtownData.js`, then `BuildRealMidtown.ExpandMedCenter` (window x -450..650, z -4700..-3300, Midtown's grid). The City's tax data has no floor count for most hospital campuses, so `AddHospitalCampuses` gives large exempt lots on campus streets a tall building. `HTownSim > Med Center > Add Hospital ER Bays` places six `ER_Bay_*` pads at the City's hospital points (Memorial Hermann, Houston Methodist, Texas Children's, St. Luke's, Ben Taub, MD Anderson).

An ambulance (a unit with no water) that finishes a medical call moves the marker to the nearest ER bay and the call ends when it stops there (`FireResponse.BeginTransport`). The HUD map now covers x -2250..690, z -4740..1575 (Blender scripts and `AddCityMap` use the same window; the HUD panel is 230 px wide so the taller map fits). `BuildRealMidtown.AddSouthRoads` (data from `node AgentScripts/ExtractSouthRoads.js`) lays flat asphalt along the City's street centre lines south of the gap and a straight Main St spine to the Med Center, because the City's centre lines stop in Hermann Park. Not done: those roads have no markings or traffic, the area west of the Med Center window has no parcel data, and the Med Center has only the two traffic axes the City data yields.

`BuildRealMidtown.AddCorridorFootprints` puts plain boxes on the 116 LiDAR footprints of the Hermann Park corridor (the zoo, museums, golf clubhouse), oriented to each footprint, with floors from its area. Data: `CORRIDOR_CACHE=<medcorridor cache> node AgentScripts/ExtractCorridorFootprints.js`. The corridor has no lots, trees or markings.

The south gap (Station 16 / Medical Center / corridor) is filled by `NEIGHBORHOOD=gapsouth node AgentScripts/PrepareMidtownData.js` then `BuildRealMidtown.ExpandGapSouth`: the same lots, sidewalks and placeholder squares as the first gap, over asphalt (grass in Hermann Park), skipping the Medical Center window. The City's road layer has almost no streets inside Hermann Park, so the park is mostly open grass with the few streets it does list.

## Gap buildings and ground (the gap kit)

The gap regions between the neighborhoods (Midtown to Station 16, and the south gap down to the Medical Center) were first filled with one
plain grey box per lot. `HTownSim > Gap > Build Gap Buildings` (`Assets/HTownSim/Editor/GapKit`, entry `AddGapBuildings`) replaces
those boxes with real buildings, one mesh per building, from the same City lot data (`Gap_Southwest.txt`, `Gap_South.txt`):

- **Which lots, what style.** The lots that had a box; style and height by land use and floors, the same rules as the neighborhood
  builders: houses (class A, up to two floors), shops (non-residential up to three floors), apartments, towers of ten floors or more
  (stone with windows, or glass curtain wall when built after 1985).
- **Placement.** The smallest rectangle around the lot outline, so a building sits on its lot's own angle. The street front is the side
  with no other lot just beyond it; front setbacks and side margins depend on the style and shrink on small lots.
- **Look.** Wall materials from Generated (siding, brick, stucco, limestone), window facades from the facade textures tiled in metres
  (`FacadeRes`, `FacadeShop`, `Storefront`, `TowerWindows`, `HouseFront`, `HouseSide`, `CurtainWall`), gable or hip roofs on houses,
  flat roofs with a parapet and rooftop unit on the rest. Each building has a box collider.
- **Reversible.** The boxes are hidden, not deleted; `Remove Gap Buildings` brings them back. Meshes are kept in
  `Generated/GapBuildings_<area>.asset`.
- **Ground.** `HTownSim > Gap > Texture Gap Ground` gives the gap lots the textured ground materials (paved sidewalks, lawns, parking
  asphalt, park grass); `Plain Gap Ground` puts the flat colours back. Streets are unchanged.

- **Street detail.** `HTownSim > Gap > Add Street Detail` (`AddGapStreetDetail`) gives the gaps what the neighborhoods have, with the same
  rules and assets as `BuildRealMidtown.Streetscape`: a street tree (the same weighted prefab list) in front of every second building
  with 8 m or more of frontage, a lamp and a prop (hydrant, bin, bench, parking meter ...) in front of one building in six, live oaks in
  parks (loose grid, thinned to 3,000 per gap so large parks stay open), and stall lines with parked cars on vacant parking lots. Lamps
  share one mesh (`Generated/GapLamp.asset`) so they draw instanced. Run it after Build Gap Buildings; `Remove Street Detail` undoes it.

Not done yet in the gaps: road markings, crosswalks, signals and traffic. The gap data has no street centre lines (only the Midtown-grid
axes), so they need a street network first. The Station 16, South Midtown and Medical Center neighborhoods still use the plain
procedural buildings (the Midtown upgrade has not been extended to them).

## The Medical Center skyline

`HTownSim > Med Center > Build Skyline` (`AddMedCenterSkyline`, `Assets/HTownSim/Editor/GapKit`) builds the Texas Medical Center from the
OpenStreetMap buildings in `Assets/HTownSim/Data/Osm_MedCenter.txt` (`AgentScripts/FetchOsmMedCenter.js` refreshes it), with the same
building kit as the gaps:

- **Floors.** The floors OSM records are kept. Named buildings OSM leaves low are raised to the height such buildings have: a "Tower"
  gets 14 to 24 floors, a hospital or medical plaza 8 to 16, research, institute, pavilion, clinic, college and similar 6 to 11. Parking
  structures become plain concrete garages. Museums, churches, parks and hotels keep their OSM floors.
- **Campuses.** The 12 big medical outlines are massed like hospitals, not as plain boxes:
  - A single large hospital building (9,000 to 30,000 m2: Mays Clinic, Texas Children's West Tower, Zayed, Methodist Main, Ben Taub ...) is a
    low podium over its footprint (the floors OSM records, 3 to 6) carrying one to three towers along its long axis, the middle one tallest.
  - A whole campus (over 30,000 m2: MD Anderson, St. Luke's) is an irregular layout: the outline is cut into blocks 55 to 80 m across with
    10 to 16 m streets, each block pulled in by a different amount, about one in eight left as an open plaza, none on top of a named
    building standing inside the campus. Blocks are mid-rise to high-rise, the tallest toward the middle, one in six a 22 to 34 floor tower;
    a block of 13 floors or more is a 4-floor podium with a tower on it set off to one side.
- **Style.** Ten floors or more: a tower in stone with windows or glass curtain wall. Four to nine: an institutional mid-rise (limestone,
  concrete, stucco, with tall windows). Each building faces the nearest OSM road.
- **What it replaces.** The procedural apartments, shops, glass and striped towers and houses of `Neighborhood_MedCenter` inside the OSM window
  (x -450..650, z -4700..-2400), their combined pitched roofs, and the street trees and props that would stand inside the new buildings.
  They are hidden, not deleted: `Remove Skyline` brings them back.
- **Ground.** The campus lots get the paved ground texture (`Texture Gap Ground` covers the Medical Center too).

Result: about 250 buildings, of which about 74 are towers of 10 or more floors (21 of 20 or more).

### The corridor and the Medical Center streets

- **Corridor buildings.** `HTownSim > Med Center > Build Corridor Buildings` (`AddCorridorBuildings`) replaces the 116 cream boxes of the Hermann
  Park and Museum District corridor (`Corridor_Footprints.txt`, LiDAR) with civic buildings from the kit: two storeys and up in limestone,
  concrete or stucco with tall windows, one storey as a low building. A box that overlaps a building the skyline already makes from OpenStreetMap
  is left out, so 90 of the 116 are not rebuilt (70 duplicated OSM buildings, the rest were tiny sheds). The boxes are hidden; `Remove
  Corridor Buildings` brings them back.
- **Hermann Park trees.** `Add Street Detail` also scatters about 1,500 trees in groves over the park meadow (the south gap's park slab), clear of
  streets (`SouthRoads.txt`, OSM) and buildings.
- **Street paint.** `HTownSim > Med Center > Paint Streets` (`AddMedCenterMarkings`) paints Midtown's double yellow center line along every
  OSM arterial, collector and local street of the Medical Center (about 33 km), broken near junctions. The data has no one-way flags, so a
  divided road gets a double yellow line on each carriageway. Crosswalks, signals and traffic are not done.
