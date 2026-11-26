# Gameplay assemblies

HTownSim gameplay is compiled into five runtime libraries and one Editor-only library.
Unity generates their .csproj files from the .asmdef assets; do not edit generated projects.

| Assembly | Responsibility | Project dependencies |
| --- | --- | --- |
| HTownSim.Contracts | Vehicle state base, apparatus values, traffic interaction contract | None |
| HTownSim.Units | Vehicle profiles, lights, audio, wheels, aerial equipment | Contracts |
| HTownSim.World | Traffic, pedestrians, patients, weather, ambient sound, signals | Contracts |
| HTownSim.Simulation | Incident parsing, queueing, dispatch rules, service client, fire spread | None |
| HTownSim.Player | Driving, camera, HUD, menus, loadout selection, live-feed integration | Contracts, Units, World, Simulation |
| HTownSim.Editor | Existing authoring tools and Editor service launchers | All runtime libraries |

World and Units consume VehicleStateBase instead of depending on FireResponse.
FireResponse implements that state and coordinates the runtime modules. Traffic registers
through ITrafficInteractions, keeping the shared contract independent of World.
The LiveIncidentFeed component remains in Player because it resolves the selected apparatus;
the incident processing code itself is in Simulation.

Existing namespaces, serialized field names, and script .meta GUIDs are retained to preserve
scene and prefab references. Serialized response fields now target VehicleStateBase, which
FireResponse derives from. Existing internal APIs remain internal, with explicit friend
assemblies only for their direct consumers. New modules must not reference Player from below.

This is an assembly-boundary refactor. It does not change map loading, menu behavior,
unit switching, incident state, or rendering. Persistent-world unit switching is a separate task.
The standalone Tools/HoustonIncidents .NET projects and third-party assets are unchanged.

Rollback baseline: commit 2d56a70 on start-scene; refactor branch: refactor/gameplay-assemblies.

## Validation

- Unity recompile: all six assemblies built, zero errors. One pre-existing CS8601 warning remains in ApparatusLights.
- All 83 original script GUIDs preserved.
- HoustonResponse scene: zero missing scripts; all seven serialized response references remain assigned.
- Both HTownSim prefabs checked: no missing scripts.
- Play Mode: StartMenu and HoustonResponse initialized; frames advanced and console errors remained zero.
- Play Mode stopped after verification; scene files and player preferences were not changed.
