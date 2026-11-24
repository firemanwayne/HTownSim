# Blender sources

Blender-side work for HTownSim, kept in the same repo as the Unity project.

- `HTown/` - restyled city map, buildings, ground and trees built from City of Houston GIS data. See `HTown/README.md`.
- `HFD Apparatus/` - Houston Fire Department engine and ambulance models.

`.blend`, `.glb` and the raw Poly Haven scans are not tracked (too large for GitHub); regenerate them with the scripts.
Several Unity editor scripts in `AgentScripts/` still read and write `J:\Blender\Projects\...` directly.
