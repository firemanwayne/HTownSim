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

## Open items / ideas

- Test the player being matched together with computer-driven units (mixed incident), including the player finishing first.
- Computer-driven units do not stop for traffic lights; the unit is not blocked by cars (cars are cleared instead).
- Fire effects sit at the incident's geocoded point (often a street); could be snapped to the nearest lot.
- Fires other than "FIRE" types (alarm, gas, rescue) get no effect.
- Chiefs and supervisors respond only when listed; no auto back-up rule.
- Station 7 has no ambulance or medic unit in the scene; adding one would let A007 be matched.
- Units already on a call are not re-sent; an incident whose listed units change later is not re-matched once matched.
- Not yet merged to main / not in the standalone build (branch `ai-fleet`).
