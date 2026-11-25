# Starting vehicles: HFD, HPD and citizen

The start screen has three levels: **service** (Fire, Police, Citizen), then, for the fire department only, **station** (7, 8, 16),
then **vehicle**. Backspace goes up a level. The pick is saved (`StartingTruck` in PlayerPrefs) and the scene reloads with that
vehicle fitted by `ApparatusLoadout`.

| Service | Vehicle | Where it starts |
| --- | --- | --- |
| Fire | Engines, ladders, ambulances, supervisor and district chief | their fire station's bay |
| Police | HPD Patrol 46837 (Ford SUV) | HPD Midtown substation, beside Station 7 |
| Citizen | Citizen Car (SimplePoly car at 0.85 scale) | the same apron |

## HPD Patrol 46837

- Model: `Blender/HPD/HPD_Patrol_46837.blend`, exported by `Blender/HPD/export_hpd.py` to `Assets/HTownSim/Vehicles/HpdPatrol/HpdPatrol.fbx`
  (one body mesh plus four wheels whose pivots are on the axles). Re-run the export after changing the blend, then
  `AgentScripts/BuildHpdPatrolPrefab.cs` for the scene-prop prefab.
- Lamp materials are named by role and driven by existing systems: `HPD_Flash_Red` / `HPD_Flash_Blue` (lightbar, alternating, with the
  siren/lights keys), `HPD_Steady_Headlight` / `HPD_Steady_Fog` (with the headlights), `HPD_Lamp_TurnL_*` / `HPD_Lamp_TurnR_*` (blinkers
  and hazards, front amber and rear red), `HPD_Lamp_Brake`, `HPD_Lamp_Reverse` (`SignalLamps`).
- Drives like the other apparatus: throttle, steering (front wheels turn), reversing, brake, horn, siren tones, scene lights.
- Dispatch: `LiveUnitKind.Police`. It is first due to every police (`PD`) incident inside the map, and the call plays like any
  non-fire call: drive to the marked scene, stop within range for a few seconds, the call clears.

## Citizen car

No lightbar, siren tones or siren HUD (`ApparatusProfileValues.EmergencyEquipment` is false); headlights, the add-on brake and
reverse lamps and steering/driving work. It receives no calls.

## Scene setup

`HTownSim > HPD > Add Substation` (`Assets/HTownSim/Editor/AddHpdSubstation.cs`) builds the substation (a low-poly building, apron,
parking lines and sign) on the free lot at (48, -24) beside Station 7 and parks the patrol unit (group `HpdApparatus`) and the
citizen car (group `CitizenVehicles`). It can be run again; `Remove Substation` takes it all out. The start screen greys out a
service whose vehicles are not in the scene.
