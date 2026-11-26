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

- **Night time.** The game needs a night (the owner asked about this earlier and it was never built; the city is always daytime).
  It ties into the items above: the emergency lights and glow halos only read properly in the dark (the HPD lightbar bloom was never
  checked at night), and it should follow the current time (see "incidents close to the current time"). Things it touches: a
  time-of-day setting driving the sun and sky (`Runtime/World/SkyDecor.cs`, `EnvironmentWeather.cs`), street lamps and building lights
  (the gap kit's `GapStreetLamps`, `SceneLights`), vehicle headlights and tail lamps (`SignalLamps`, `RearLamps`, `LensFlasher`),
  traffic and signal lenses, window lights on buildings, and the map's brightness. Open choices: real clock or an accelerated day,
  a manual toggle, and how dark it should get. Noted only; no change made yet.

## Done from the notes

- **Same-type cover for missing units** (`FleetDispatcher.CoverFor`, setting `LiveFeedSettings._substituteUnits`, on by default): after
  the owner found that about 1 live call in 38 listed a unit the game has, a listed engine (E), ladder (L) or medic (M) unit the game
  does not have is covered by the closest free game unit of that type. This relaxes rule 1 for those three types only. Still nobody:
  a listed unit the game has but that is busy, listed ambulances the game lacks (A007, A008) and chiefs. Turn it off in the settings
  asset to get the strict exact-match behaviour. Checked with crafted calls: E035 sends Engine 08 (closest), E035+E036+E037 sends three
  different engines, L040+M021+A007 sends Ladder 07 and Medic 08, busy E008 sends nobody, the scenarios from the notes are unchanged.
  Not yet watched in the running game.

- **AI siren sound** (`Runtime/Units/AiSiren.cs`, started by `AiEmergencyLights`): computer-driven units now play the player's recorded
  tones (wail, yelp, pier from `Resources/SirenAudio`), alternating every 8-15 s. Distance: logarithmic fade, a low-pass filter that
  closes from 22 kHz to 1.6 kHz with distance, and Doppler. Echo and reverb: an echo and reverb filter whose wet level rises with
  distance and with buildings found by side raycasts (street between buildings rings louder than open ground). Checked in the editor:
  all 14 units carry the source and three filters and the Wail clip plays; how it actually sounds has not been listened to, so the
  numbers (all documented on the constants) may need tuning by ear. The air horn is not used by computer-driven units.

- **Other units on the City Map** (`Runtime/Player/FleetMapMarkers.cs`, hooked in `CityMap.cs`): a square per computer-driven unit under the
  player's marker; red for fire, blue for PD (the owner's colors), white for EMS and orange for chiefs (my picks, not yet confirmed by the owner).
  A unit out on a call is bright and larger with a short label (E16, A16, DC08, P2); a parked unit is dim with no label.
- **HTOWN HUD smaller** (`HudLayout.cs`, `IncidentScale` 0.72): the whole top-left panel is drawn at 72% and the map below it moves up.
- **Aerial HUD stacks on top of the siren controls** (`AerialHud.cs`): the siren panel now stays in view and the aerial panel sits 12 units
  above it, instead of swapping with it.
- **Police cars no longer spawn stacked** (`AiFleet.GroundPoint`): the spawn takes only street, slab or ground surfaces, never roofs or
  parked cars. Checked in the editor: AiPatrol1 and 2 stand on the ground (y 0.2) beside the citizen car, about 7 m apart.
- **Incidents at start are close to the current time** (`LiveIncidentQueue`, `HoustonClock`, `LiveIncidentRecord.OpenedAt`): an incident's
  opened time (Houston local time, as the City page writes it) is read and measured against the current Houston time. Incidents opened
  within `RecentMinutes` (60) are released first, in random order; when none is that recent the newest goes first; incidents opened more
  than `MaximumAgeMinutes` (480) ago are not taken in at all (this also drops old calls the incident service's saved ledger hands back).
  Both are in `LiveFeedSettings` (override with a Resources asset). Checked in the editor with a fake clock over 200 seeds: a call from
  16 hours ago was never released, and the order was always the recent ones, then 90 minutes old, then 7.5 hours old. Not checked
  against a live City page; a call with no readable time counts as current.

## Direction: future development should be multiplayer oriented

Owner's direction: build new things so they can work with several people in the same city. Nothing is implemented; this is a design
constraint for every later change. It agrees with the "join the map" note (a player joins a running city, and the incident service
and AI fleet already exist outside any one player). What it means for design, to apply as things are built:

- **One city, many players.** The city, its incidents and its computer-driven units are shared state; a player is a client that
  takes control of one unit (or spectates). Avoid anything that assumes exactly one player (`GameObject.FindGameObjectWithTag("Player")`,
  a single `FireResponse`, one saved start pick in PlayerPrefs, the static `StartingTruckChoice`, the one-player `LiveIncidentFeed`).
- **A central host receives updates (owner's decision).** There is one central host. Every client (a person playing or spectating)
  sends its updates to the host: its unit's position and state, the actions it takes on a call, and when it claims or releases a unit.
  The host is the authority: it owns the incident ledger and the dispatch (the incident service, which already owns created /
  assigned / handled / cleared and runs outside the game, is the natural seed for it), it runs the computer-driven units, and it
  sends the shared world state back out to every client. Clients do not talk to each other. Design consequences to keep in mind:
  client updates are small and frequent (unit transform, siren and light state, phase of the call), the host decides conflicts (two
  people claiming one unit, who is closest and free), and a client that drops out has its unit handed back to the computer.
  The host is a place that needs a clock, a list of connected clients and the unit claims.
- **Dispatch is by unit, not by "the player".** `FleetDispatcher` already matches units by name; keep candidates as units that are
  either person-driven or computer-driven, so any number of people can hold units.
- **Units are claimed, not assumed.** Two people cannot drive the same unit; a unit nobody holds is computer-driven; a person who
  leaves hands their unit back to the computer. Changing unit should be release one, claim another.
- **Keep per-player state per player:** camera, HUD, map, siren, scene lights, the current call. Keep world state shared: units,
  fires, patients, incidents, traffic (decide whether traffic is simulated on the host and replicated, or seeded identically).
- **Deterministic or replicated.** Pick for each system (fire growth, AI routes, traffic): either deterministic from a shared seed and
  clock, or simulated on the host and sent to clients. Routing already works from fixed data (`RoadGraph`).
- **Time.** A shared clock (also what night time and "incidents close to the current time" need).
- **Network choice** is still open (for example Unity Netcode for GameObjects against the host, or the existing incident service
  grown into the host over HTTP/WebSocket); the shape is decided: one central host, clients send it updates. Where the host runs
  (a dedicated server or a player's PC) is not decided.

## Open items / ideas

- Test the player being matched together with computer-driven units (mixed incident), including the player finishing first.
- Computer-driven units do not stop for traffic lights; the unit is not blocked by cars (cars are cleared instead).
- Fire effects sit at the incident's geocoded point (often a street); could be snapped to the nearest lot.
- Fires other than "FIRE" types (alarm, gas, rescue) get no effect.
- Chiefs and supervisors respond only when listed; no auto back-up rule.
- Station 7 has no ambulance or medic unit in the scene; adding one would let A007 be matched.
- Units already on a call are not re-sent; an incident whose listed units change later is not re-matched once matched.
- Not yet merged to main / not in the standalone build (branch `ai-fleet`).
