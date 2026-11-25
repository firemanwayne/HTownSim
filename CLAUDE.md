# HTownSim — Architecture & AI Workflow Rules

## Repository & Git Workflow
- **Repo:** `J:\Unity\projects\HTownSim` (`firemanwayne/HTownSim`)
- **Active Branch:** `feature/station16-neighborhood` (merges to `main` via fast-forward).
- **LFS:** `Assets/Scenes/HoustonResponse.unity` is tracked via Git LFS. Never manually edit `.unity` YAML; modify the scene strictly through `AgentScripts/` or Editor menu commands.
- **Monorepo Layout:**
  - `Assets/HTownSim/Runtime/` — Core gameplay (`FireResponse`, `ApparatusProfile`, `ApparatusLoadout`, `StartingTruckMenu`, `Incidents/`, traffic, weather, HUD map, aerial ladder, patient/hospital transport).
  - `Assets/HTownSim/Editor/` — Editor menu tools (Station 8/16 apparatus setup, Med Center ER bays, Drive Ladder/Ambulance).
  - `Assets/HTownSim/Data/` — City GIS text files (neighborhoods, gaps, US-59, south roads, footprints, OSM).
  - `Assets/HTownSim/Vehicles/` — FBX models and liveries (`Engine 07`, `Ladder 07`, `Ambulance 07`, `District Chief 08`, `Supervisor 16` EMS supervisor pickup, Station 8/16 variants).
  - `Assets/HTownSim/Shaders/` — Custom shaders (`WorldTriplanar`, sky).
  - `AgentScripts/` — Automated Unity Editor scripts executed via CLI (`unity command script run --file <path> --entry <Method>`).
  - `Blender/HTown/` — Headless city map & upgrade pipeline (`fetch_houston.js` -> `build_map.py` -> `render_all.py` -> `Assets/HTownSim/Textures/Map/CityMapBlender.png`).
  - `Blender/HFD Apparatus/` — Procedural apparatus builders and `HFD_UNIT_NUMBER` livery/decal generators.
  - `Tools/HoustonIncidents/` — .NET 10 harvester for the live City of Houston incident feed (started by Unity on Play; see `Documentation/LiveIncidents.md`).

## Engine & Rendering Constraints (Unity 6000.6.3f1 URP)
- **Render Pipeline:** URP (`PC_RPAsset`) with **GPU Resident Drawer** enabled.
- **Batching / Mesh Rule:** Do **not** merge large city-wide meshes unless explicitly requested. GPU Resident Drawer handles instanced static renderers efficiently; large combined meshes degrade frustum culling.
- **Packages:** Unity Input System, Cinemachine, TextMeshPro. Do not introduce legacy `UnityEngine.Input` calls.

## Spatial & Coordinate Rules
- **World Origin:** Station 7 is `(0, 0, 0)`.
- **Grid Alignment:** Base Houston street grid is rotated **35.06°**. Neighborhood roots are built in local grid space and rotated into world placement (e.g., Station 16 root uses **0.1°**).
- **Geo-Conversion:** All real-world GIS/OSM latitude and longitude data (ArcGIS parcels, LiDAR footprints, Overpass Med Center data) must pass through `GeoProjection` so coordinates align with the Station 7 origin and grid rotation.

## Cross-Tool Pipelines (Unity <-> Blender <-> Node)
- **Unity Editor Automation:** When writing automation in `AgentScripts/`, provide the exact CLI command to run it:
  `unity command script run --file AgentScripts/<ScriptName>.cs --entry <Namespace.Class.Method>`
- **Blender Execution:** Scripts in `Blender/` run headless (`blender -b --python <script>.py`) or via Blender MCP when open. Always apply transforms (`Scale/Rotation = 1,1,1`) and export FBX models using Unity's `-Z Forward, Y Up` convention into `Assets/HTownSim/Vehicles/`.
- **Livery Generation:** Parameterize apparatus scripts using `HFD_UNIT_NUMBER` rather than duplicating mesh builders per station.

## Coding Standards
- Separate pure C# data/simulation logic (e.g., `IncidentManager` JSON parsing, `GeoProjection` math, `ApparatusProfile` configs) from `MonoBehaviour` components.
- Cache component references; avoid `GameObject.Find` or `GetComponent` inside `Update` or `FixedUpdate`.
- Before modifying existing city builders (`BuildRealMidtown`, `RepairAfterExpand`, `UseBlenderCityMap`), read the target script completely to preserve coordinate offsets and hierarchy expectations.
