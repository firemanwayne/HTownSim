# Houston Ladder 07

Editable Blender exterior model based on the four supplied apparatus photographs and the Houston FY25 Ladder Drawing. The existing Engine 07 and map scenes are not modified by the builder.

## Scale and orientation

- Metres; front points toward negative X; Z is up.
- Drawing overall length: 499 in / 12.6746 m.
- Drawing estimated stowed height: 144 in / 3.6576 m.
- Body width: 100 in / 2.54 m; mirrors and fittings project beyond the body.
- Front axle to tandem centre: 246 in / 6.2484 m.
- Front bumper to front axle: 100 in / 2.54 m.
- Tandem spacing is inferred from the drawing, approximately 54 in.

## Contents

`Ladder07.blend` is the finished standalone Blender project with materials, studio lighting, and five inspection cameras. `Houston_Ladder_07.blend` is an intermediate scene library. `build_ladder07.py` is the reproducible Blender source; `render_checks.py` renders the five checks and packages the final project. Collections separate cab, body, aerial, wheels, graphics, chassis, and studio.

Unit markings are changed to 07. The side photos determine the finished configuration; the different-unit front reference supplies front-end details. The source PDF labels its dimensions approximate, so this is a reference-based visual model, not an engineering CAD reconstruction.

The driver and passenger front doors use the supplied `HFD_Logo_May_2025.png` artwork, packed into the rigged project and retained at all three detail levels. The crew-door unit badges remain simplified. Compartment interiors and hidden mechanical systems are not reconstructed. No Unity scene changes are included.

## Rigged and optimized project

Open `Ladder07_Rigged.blend`. Select `Ladder07_CONTROLS` and expand **Object Properties > Custom Properties**. The controls are unkeyed by default so the sliders can be edited directly:

- `Steering_deg`: front axle Ackermann steering, -35 to +35 degrees.
- `Wheel_travel_m`: rolls all six wheel groups at a 0.555 m rolling radius. It does not translate the vehicle root.
- `Stabilizers`: 0 stowed to 1 deployed; four extending beams and ground-contact feet.
- `Aerial_elevation_deg`: 0 to 80 degrees.
- `Aerial_azimuth_deg`: -180 to +180 degrees around the rear turntable.
- `Aerial_extension`: 0 to 1; three nested fly sections extend 7.5 m each.
- `LOD`: 0, 1 or 2; switches visible geometry while preserving the same articulation.

Deploy stabilizers and elevate the aerial before slewing it over the cab. The rig does not enforce collision avoidance or physical operating limits. Hydraulic pistons follow the elevation linkage. This is a rigid mechanical hierarchy with drivers, not a deforming character skeleton.

Two reusable actions are included: `L07_Aerial_Deploy` (frames 1-180) and `L07_Wheels_Steering` (frames 1-120). Assign the desired action to `Ladder07_CONTROLS` in the Action Editor to play it. Clear the action to return to direct slider control.

| Level | Triangles | Mesh objects | Intended use |
| --- | ---: | ---: | --- |
| LOD0 | 190,968 | 30 | Close inspection |
| LOD1 | 67,318 | 30 | Medium distance |
| LOD2 | 19,890 | 30 | Distant vehicles |

Only one level is visible at a time. The detailed source had approximately 2,800 separate objects. Materials are retained; texture atlasing and engine-specific performance profiling are not included. Blender drivers require baking or equivalent game-side controls before export to an engine.

`build_ladder07.py` produces `Ladder07_Tagged_Source.blend`; run `rig_optimize.py` against that tagged library to rebuild the rig. `verify_rig.py` checks mesh movement, Ackermann steering, LOD visibility, driver validity and the deployment action, then renders stowed and deployed previews. Results are recorded in `optimization_report.json` and `rig_validation.json`.
