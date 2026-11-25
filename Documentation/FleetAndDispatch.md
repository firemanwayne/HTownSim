# Fleet and dispatch: rules, scenarios and open items

Working notes for the incident-to-unit logic. Rules and scenarios come from the project owner; add new ones as they are given.
Code: `Runtime/Simulation/Incidents/Managers/FleetDispatcher.cs` (pure matching), `Runtime/Player/AiFleet.cs` and `AiUnit.cs`
(computer-driven units), `Runtime/Player/Incidents/Sources/LiveIncidentFeed.cs` (hooks it to the feed and the incident service),
`Runtime/Simulation/Fleet/RoadGraph.cs` (street routing), `Runtime/Player/LiveIncidentFires.cs` (fire effects).

## Rules (decided)

1. **Match the units the feed lists.** Each unit code the City lists on a fire or EMS incident (E007, L016, A008, DC08 ...) goes to the
   game unit of that name when the game has it and it is free (`LiveDispatchRule.UnitCodes` maps names to codes).
2. **No substitutes.** A listed unit the game does not have gets nobody (Ambulance 07 / A007, a Station 7 medic unit, A008 ...).
   A listed unit that is busy gets nobody either. An incident that lists no unit gets none until the City lists one.
3. **Only Ambulance and Medic units transport** a patient to a hospital (`LiveDispatchRule.CanTransport`). Engines, ladders,
   supervisors, chiefs, patrol cars and the citizen car never do, including the player's unit.
4. **The player is one candidate.** If the player's unit is among the matched units the incident is sent to the player as before;
   the other matched units are driven by the computer. The incident is cleared (incident service) when the last unit on it is done.
5. **Police calls: up to 4 officers**, spaced out. The game has four computer-driven patrol units (HPD Unit 1-4, parked on the HPD
   substation apron). A police incident gets as many as the City lists, at most four, closest free first (only the count matters:
   the City's police unit numbers mean nothing in the game). At a scene units stop 11 m apart along the street.
6. **Computer-driven units** drive the street route to the scene with lights and siren (the siren stops on scene), work the scene
   (fire about 110 s, other calls about 60 s, patient load about 45 s), an ambulance or medic with a patient drives to the nearest ER
   bay (25 s there), and every unit drives back to its station. Wheels always roll with the distance travelled.
7. **Fires the computer handles show flames, smoke and crackle**: they grow while nobody works them, heat falls with units on scene
   (more units, faster), they go out, and are removed after the incident clears. At most four fires at once.
8. **Traffic yields:** cars within 9 m of a responding computer-driven unit are cleared out of its way.

## Scenarios given (use as tests)

| Scenario | Expected |
|---|---|
| EMS call for E007, A008 | Engine 07 goes. A008 is not in the game: nobody. |
| Structure fire E007, L007, E016, L016, E008, A007, DC08 | Engine 07, Ladder 07, Engine 16, Ladder 16, Engine 08, District Chief 08. A007: nobody. Flames and smoke at the scene. |
| Either unit may be the player, the other computer-driven | The computer-driven one drives itself with lights and siren along the route, arrives, then (an ambulance) transports the patient. |
| Police call with several units listed | Up to 4 patrol units, spaced out. |

## Test harness (editor)

Play starts at the StartMenu scene. A script run in the editor can reach the internal classes by reflection: pick a unit with
`StartingTruckMenu.Choose`, build `LiveIncidentRecord`s from JSON, wrap them in `LiveBoardEntry` and call the feed's `Evaluate`,
then read `AiFleet.Units` (name, phase, position). Time can be run at 4-8x (`Time.timeScale`). The player was a citizen car so
every unit was computer-driven. The player-dispatched path (player matched) was not exercised in these runs.

## Requested, not built yet (waiting for the go-ahead)

- **Other units on the City Map.** The owner wants to see the other (computer-driven) units on their map. Nothing was changed; the
  owner asked for no changes until told. Plan to propose when told: a marker per fleet unit (the 10 apparatus and 4 HPD officers) on the
  scrolling map in `Runtime/Player/CityMap.cs`, colored by kind (fire red, EMS white, chief orange, police blue), bright with a short
  label (E16, A16, DC8, P1) while out on a call and dim without a label when parked.
  **Colors decided by the owner: red for fire, blue for PD.** Still open: the color for EMS (proposed white) and the chief (proposed
  orange), labels on parked units, whether to show only units that are out.

- **Ladder aerial HUD placement.** On the ladder trucks the aerial HUD (`Runtime/Player/AerialHud.cs`) should stack on top of the
  emergency (siren) controls (`Runtime/Player/EmergencyHud.cs`, laid out by `HudLayout.cs`) instead of sharing or overlapping their
  place. Noted only; no change made yet.

- **Vehicle collision boundaries.** Vehicles clip into each other, so their physical boundaries (colliders) need to be extended.
  Places to look: the player's `CharacterController` (`Runtime/Player/FireResponse.cs`), the parked and computer-driven units'
  BoxColliders (`AiUnit`, `AiFleet`; computer-driven units move as plain bodies and ignore physics), and the traffic cars' colliders
  (`Runtime/World/TrafficSystem.cs`). Noted only; no change made yet.

- **Changing units must not rebuild the map.** Design direction from the owner: the map (the city) is something the player *joins*,
  not something that is built for them. Choosing or changing a unit should put the player into that unit in the already-running
  city, not reload the scene. Today the pick is saved and the whole city scene is reloaded (`StartingTruckMenu.Choose` /
  `Reload`, about 30 s), because the unit is fitted once in `FireResponse.Awake` (`ApparatusLoadout.Fit`: model, wheels, lights,
  audio, camera) and everything reads the profile from there. Direction: keep the city loaded and make "take control of unit X" a
  runtime operation, with the other units staying where they are as computer-driven units (the fleet in `AiFleet` already drives
  every parked unit but the player's). Things this touches: re-running the loadout fit and its dependents (`WheelSpin`,
  `LensFlasher`, `ApparatusLights`, `SceneLights`, `EngineSound`, camera follow), handing the old unit back to `AiFleet` and taking the
  new one out of it, the live incident feed's player-unit identity (`LiveIncidentFeed`, dispatch candidates), and the start flow
  (the start scene would join an existing city rather than load one). Noted only; no change made yet.

- **Spectator mode.** A mode with no unit under the player's control where you float freely around the map (a free camera), watching
  the computer-driven units work. It fits the "join the map" direction above: the spectator joins the running city without a unit,
  and could later take control of one. Open choices to settle when it is built: how it is entered (start screen choice and/or a key
  in game), camera controls and speed, whether it can follow a unit or an incident, which HUD parts stay (map, incident board),
  and that with no player unit every unit (including the one the player would have driven) is computer-driven. Noted only; no change
  made yet.

- **Reduce the size of the HTOWN HUD.** The top-left panel headed "H T O W N / <unit>" (the unit name, call text, speed, distance and
  status block) should be smaller. It is built and sized in `Runtime/Player/HudLayout.cs` / `HudLayoutParts.cs` and filled by
  `FireResponse`; the city map panel below it is positioned from it, so the map and the other HUD blocks may need to move up with it.
  Noted only; no change made yet.

- **AI siren sound.** When watching a computer-driven unit respond, its siren does not sound right. It should use the same sirens the
  player can control (the real wail, yelp and pier tones and the air horn from the siren control in `Runtime/Player/FireResponse.cs`
  and `Runtime/Units/AirHorn.cs`, resources under `Resources/SirenAudio`), not the plain synthesized wail now made in
  `Runtime/Units/AiEmergencyLights.cs`. The sound also needs a sense of distance (louder and brighter near, quieter and duller far,
  with the pitch shift of a passing vehicle) and echo or reverb (stronger between buildings), instead of the plain linear fade now used.
  Noted only; no change made yet.

- **Incidents at start should be close to the current time.** When the game starts, the incidents that are loaded should be recent:
  close to the current time, not the stale ones the City page still lists (the incident board shows calls opened hours earlier, for
  example 21:22 and 00:23 on a morning start). Where to look: the harvester (`Tools/HoustonIncidents`, which posts each poll to the
  incident service), the service's ledger (`%LOCALAPPDATA%\HTownSim\IncidentService\ledger.json`, which persists between runs and can hold
  old incidents), and the feed queue (`Runtime/Simulation/Incidents/Managers/LiveIncidentQueue.cs`, which releases waiting incidents in
  random order, using `callTimeOpened` only for display). Open choices: how recent counts as close (a window such as the last hour),
  whether to drop older incidents or just release the recent ones first, and how to treat the persisted ledger on start. Noted only;
  no change made yet.

- **Night time.** The game needs a night (the owner asked about this earlier and it was never built; the city is always daytime).
  It ties into the items above: the emergency lights and glow halos only read properly in the dark (the HPD lightbar bloom was never
  checked at night), and it should follow the current time (see "incidents close to the current time"). Things it touches: a
  time-of-day setting driving the sun and sky (`Runtime/World/SkyDecor.cs`, `EnvironmentWeather.cs`), street lamps and building lights
  (the gap kit's `GapStreetLamps`, `SceneLights`), vehicle headlights and tail lamps (`SignalLamps`, `RearLamps`, `LensFlasher`),
  traffic and signal lenses, window lights on buildings, and the map's brightness. Open choices: real clock or an accelerated day,
  a manual toggle, and how dark it should get. Noted only; no change made yet.

## Direction: future development should be multiplayer oriented

Owner's direction: build new things so they can work with several people in the same city. Nothing is implemented; this is a design
constraint for every later change. It agrees with the "join the map" note (a player joins a running city, and the incident service
and AI fleet already exist outside any one player). What it means for design, to apply as things are built:

- **One city, many players.** The city, its incidents and its computer-driven units are shared state; a player is a client that
  takes control of one unit (or spectates). Avoid anything that assumes exactly one player (`GameObject.FindGameObjectWithTag("Player")`,
  a single `FireResponse`, one saved start pick in PlayerPrefs, the static `StartingTruckChoice`, the one-player `LiveIncidentFeed`).
- **Authority.** Decide who owns the incident ledger and the dispatch (the incident service is the natural home, and it already owns
  created / assigned / handled / cleared), and who simulates the computer-driven units (one host, not every client).
- **Dispatch is by unit, not by "the player".** `FleetDispatcher` already matches units by name; keep candidates as units that are
  either person-driven or computer-driven, so any number of people can hold units.
- **Units are claimed, not assumed.** Two people cannot drive the same unit; a unit nobody holds is computer-driven; a person who
  leaves hands their unit back to the computer. Changing unit should be release one, claim another.
- **Keep per-player state per player:** camera, HUD, map, siren, scene lights, the current call. Keep world state shared: units,
  fires, patients, incidents, traffic (decide whether traffic is simulated on the host and replicated, or seeded identically).
- **Deterministic or replicated.** Pick for each system (fire growth, AI routes, traffic): either deterministic from a shared seed and
  clock, or simulated on the host and sent to clients. Routing already works from fixed data (`RoadGraph`).
- **Time.** A shared clock (also what night time and "incidents close to the current time" need).
- **Network choice and hosting** (Unity Netcode for GameObjects, dedicated server, or peer-hosted) are not decided.

## Open items / ideas

- Test the player being matched together with computer-driven units (mixed incident), including the player finishing first.
- Computer-driven units do not stop for traffic lights; the unit is not blocked by cars (cars are cleared instead).
- Fire effects sit at the incident's geocoded point (often a street); could be snapped to the nearest lot.
- Fires other than "FIRE" types (alarm, gas, rescue) get no effect.
- Chiefs and supervisors respond only when listed; no auto back-up rule.
- Station 7 has no ambulance or medic unit in the scene; adding one would let A007 be matched.
- Units already on a call are not re-sent; an incident whose listed units change later is not re-matched once matched.
- Not yet merged to main / not in the standalone build (branch `ai-fleet`).
