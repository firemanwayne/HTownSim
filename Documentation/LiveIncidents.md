# Live incidents (City of Houston feed)

Real Houston incidents come into the game through the **HoustonIncidents** harvester, a .NET 10 worker in `Tools/HoustonIncidents`
(its own solution, tests and design notes; brought in from the former standalone repo with its history).

## Running it

Pressing Play in Unity starts it (and stopping Play stops it): `Assets/HTownSim/Editor/HarvesterLauncher.cs` runs
`dotnet run --project Tools/HoustonIncidents/src/Harvester` hidden and logs to `Logs/HoustonIncidents.log`
(menu **HTownSim > Live Incidents** turns this off or points it elsewhere). To run it by hand:

```powershell
dotnet run --project Tools/HoustonIncidents/src/Harvester
```

Tests: `dotnet test Tools/HoustonIncidents/HoustonIncidents.slnx`.

Every 30 s it reads the City's Active Incidents pages:

- `Combined.aspx?agency=%` for every incident (agency, address, type, call time);
- `Combined.aspx?agency=F` for fire and EMS incidents, which adds the cross street, Key Map, alarm level and the **units**
  assigned (E007, L016, A008, M074, MS092 ...).

New incidents are placed on the map with the City's GIS layers (Site Addresses for numbered addresses, the road
centre lines for a street and its cross street). Each poll writes one feed file, `live-*.json`, into
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\HTownSim\Incidents\Live` with new incidents, incidents whose units
changed, and closed ids.

## In the game

- `LiveIncidentFeed` (Runtime/Incidents/Sources) reads the Live folder into a `LiveIncidentQueue`, which releases
  incidents onto the board one at a time, in random order, every 8 to 35 seconds.
- **Active incidents board** (top right, **B** shows or hides it): every released incident, newest first, with time,
  type, place and units. Incidents outside the drivable map are dimmed.
- **Dispatch**: the player is sent (as a standard call through `IncidentManager`) when
  1. one of the player's unit codes is on the incident (Engine 07 is E007, Ladder 16 L016, Ambulance 16 A016,
     Medic 08 M008, Supervisor 16 MS016, District Chief 08 D008 / DC08), or
  2. **first due**: the incident is inside the map, the nearest of Stations 7, 8 and 16 is the player's, and the type
     fits the rig (EMS events for ambulances, medics and the supervisor; fires, alarms, gas, hazards and rescues for
     engines and ladders; fires and multi-alarm incidents for the chief).
  An incident that would dispatch the player skips the random trickle. A unit added later (the City lists units a
  minute or two after the call opens) is picked up and can still dispatch the player. The dispatch is marked
  `> YOUR UNIT` or `> FIRST DUE` on the board, and it is played after any call already in progress.
- While feed files are arriving (the last 10 minutes), the random fallback calls stay off.
- Tunables (release gap, board size, HUD lines, live window): create `Assets > Create > HTownSim > Live Feed Settings`
  and save it as `Resources/LiveFeedSettings`.

## Scenes at other units' calls

- **EMS:** `LiveIncidentPatients` lies a patient on the ground at an EMS call inside the map once a unit is assigned to it.
- **Police:** `LiveIncidentPolice` parks an HPD patrol unit (Blender/HPD, exported by `Blender/HPD/export_hpd.py` to
  `Vehicles/HpdPatrol`, prefab `Resources/HpdPatrol` built by `AgentScripts/BuildHpdPatrolPrefab.cs`) at a police (`PD`) call
  inside the map, on the nearest free stretch of street and lined up with it, lightbar flashing (`PatrolLightbar` alternates the
  red and blue LED materials). At most 12 at a time.
- Neither decides when a scene goes. The **incident service** (`Tools/IncidentService`, started with Play) owns the lifecycle
  created, assigned, handled, cleared and saves it to `%LOCALAPPDATA%\HTownSim\IncidentService\ledger.json`: a call the City
  closes with no unit assigned clears at once; one with a unit assigned is held as handled (120 s) and then cleared; the game
  clears the player's own calls. The scene (patient, patrol unit) goes when the service reports the incident cleared. Police
  calls carry no units in the City feed, so their patrol unit leaves when the City closes the call.

## Limits

- The all-agencies page gives no cross street for police incidents, so many of them cannot be placed.
- Most Houston incidents are outside the drivable map; they appear on the board only.
