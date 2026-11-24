# Blender sources

Blender-side work for HTownSim, kept in the same repo as the Unity project.

- `HTown/` - restyled city map, buildings, ground and trees built from City of Houston GIS data. See `HTown/README.md`.
- `HFD Apparatus/` - Houston Fire Department engine and ambulance models.

`.blend`, `.glb` and the raw Poly Haven scans are not tracked (too large for GitHub); regenerate them with the scripts.
Unity editor scripts in `AgentScripts/` read and write this folder at `<repo>/Blender/...`, resolved from the Unity project path, so the repo can live anywhere. Blender scripts find their data relative to their own location; when run through `exec(open(...).read())` they fall back to `J:\Unity\projects\HTownSim\Blender`.
