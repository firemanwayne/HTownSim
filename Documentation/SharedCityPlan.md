# HTownSim shared city plan

Proposed implementation order · November 26, 2026

A player should be able to join a running Houston, take over an available unit, work a call alongside AI or other players, and leave while the city continues. Build shared fleet authority first. Then follow one patient from an EMS scene to an ER handoff before introducing a separate role client.

## Starting point

The harvester and Incident Manager already run outside Unity. On the [ai-fleet baseline](https://github.com/firemanwayne/HTownSim/commit/64c7614c27cd07e59c5db688c7a5035ab1c5cd56), Unity still matches units, drives the AI fleet and tracks its response phases. Ambulances and medics can load a patient, select the nearest ER bay and return after a timed stop. F9 sends available units to a test fire. The [fleet and dispatch notes](FleetAndDispatch.md) record matching rules, substitution settings and the multiplayer direction.

## Ownership

| Component | Responsibility |
| --- | --- |
| External harvester | Collect city feed records and deliver them to the incident pipeline. |
| External Incident Manager | Own incident identity and lifecycle, including clearance when field assignments finish. Use this service as the initial central host for the new .NET modules. |
| Fleet and dispatch module | Own unit identities, availability, player claims, AI control, and assignment phases. Apply the existing dispatch rules once per city. |
| Patient Care module | Own patient identity, condition, caregiver, transport and transfer of care. Link each patient to a call using an incident ID. |
| Facility directory | Give each receiving hospital a stable ID and an ER destination that the host and Unity can resolve. Start with the existing nearest-ER behavior. |
| Unity game client | Collect player input and present nearby units, vehicle motion, lights, sirens, patients, HUD and world effects. Apply host decisions to the scene. |
| Shared .NET client library | Expose host commands and state updates to Unity and future role clients. Keep the contracts usable without Unity types. |

Start with fleet and patient care as modules in one host deployment. A hospital or police *client* is a player interface. A host module owns the shared rules regardless of whether the action comes from Unity, another interface or AI.

## Milestones

1. **Give the host authority over units.** Reuse the matching rules in `FleetDispatcher`, including configured substitutions. Let the host reserve a unit and record its assignment in one operation. Introduce claim, release and reconnect through the shared client library. Unity can continue driving the visible vehicle while the host validates phase changes. Verify two clients racing for the same unit, two incidents competing for it, a player disconnect during a call and a reconnect that restores the current assignment. A released unit should resume AI control without reloading the city.

2. **Complete one EMS call end to end.** Dispatch an engine and a transporting unit to one patient. Allow the engine to finish its assignment independently. Record patient pickup, destination, arrival and acceptance of care. Use the ER timer as the AI receiver until a hospital player can accept the handoff. Verify that either a player or AI can transport, a retried handoff transfers care once, and a patient remains available to the hospital after the field incident clears. Track ambulance availability under fleet rules independently of the patient's subsequent care.

3. **Add role clients when they have decisions to own.** A hospital receiving view becomes worthwhile when a player can see incoming patients, accept a handoff or manage capacity. A police workflow becomes worthwhile when an officer has on-scene actions or a custody transfer to record. Patrol assignment and unit availability already belong to fleet. Pick the next interface according to the first playable workflow.

## Frame-time baseline

Before moving simulation work, profile a standalone development build on a recorded hardware and graphics configuration. Use the same Midtown camera, weather and traffic settings for an idle run and an F9 run. Capture dispatch, vehicle approach and arrival. Record CPU main-thread, render-thread and GPU frame times, plus median and 95th-percentile frame time across repeated runs. At 60 FPS the frame budget is 16.7 ms. Repeat on the same configuration after each relevant change.

If dispatch spikes on the CPU, measure matching and route calculation individually. If ongoing fleet updates dominate, test advancing distant units on the host and sending occasional position updates for Unity to interpolate. Include message processing and, for a locally hosted city, contention on the same machine in the comparison. If frame times rise as lit vehicles enter view, profile lights, shadows, meshes and effects. The [Midtown culling experiment](https://github.com/firemanwayne/HTownSim/commit/4035123d36c4eacf2b78cb813e0974a603249f4d) and its [visibility adjustment](https://github.com/firemanwayne/HTownSim/commit/604a65e7ddee35d504e1d1dfde807334ba5afa67) give a reason to check distant-building visibility alongside FPS.

## Decisions to revisit

Choose transport protocol, host placement and position update rates when testing two clients in the same city. Use reconnect behavior, movement quality and measured frame times to guide those choices. The first implementation target is a mixed player/AI call with one authoritative assignment per unit.

## Claude implementation handoff

**First assignment:** implement milestone 1 as a vertical slice from incident intake to host assignment to Unity. Use a feature branch based on the current AI fleet work. Review the repository instructions and the latest changes since the baseline before editing.

- Trace one existing mixed-unit call through `LiveIncidentFeed`, `FleetDispatcher`, `AiFleet` and `AiUnit`. Find the actual external Incident Manager solution, its persistence and existing client contracts. Record the entry points and proposed changes for Wayne's implementation review. If the host source is missing, identify the checkout you need before introducing any host implementation.
- Put reservation, claim and release in the existing host. Reuse its persistence and communication conventions. Make duplicate deliveries and retried commands safe. Have Unity consume the resulting assignment through the normal incident path so a single call cannot independently dispatch a second fleet. Preserve existing exact-match, substitution and police dispatch behavior.
- Exercise a mixed player/AI response, competing claims, overlapping calls for a unit, duplicate incident delivery, disconnect and reconnect. Check the existing F9 response in Play Mode and capture the frame-time baseline where Unity and representative hardware are available.
- Open a draft implementation PR per affected repository. Link related PRs and this plan. Include build and test commands, results, a short manual walkthrough and any Unity checks still needing a local run. Keep patient care and role clients for subsequent milestones.

## Next phase: load the city once and switch units in place

Assume the host now owns unit claims and assignments. The reported baseline is about 30 seconds to load the map. The fleet notes identify the reason a unit change repeats that wait: the selection flow reloads the city to run the apparatus setup again. The cost breakdown for the initial load still needs measurement. Deliver these as two reviewable changes.

### 1. Transfer control inside the running city

- Trace the current selection flow after the host work lands. Separate entering a city from taking control of a unit. Show the unit picker over the running session. A selection of the current unit should resume immediately.
- Request an atomic switch from the host. Keep control of the current unit until the host grants the new one. If the request fails, restore the picker with the current selection intact. On success, return the old unit to AI at its current position and assignment, and attach the local driver to the selected unit at its actual position.
- Replace the one-time player wiring in the loadout and startup callbacks with explicit attach/detach operations. Rebind input, camera, HUD and dispatch identity. Check vehicle-specific equipment (ladder, pump, patrol lights), wheel animation, audio and event subscriptions. Ensure each unit has one driver and each local input has one recipient.
- Verify repeated swaps, a rejected claim, a switch during an active response and a switch across town. The city scene must have zero reloads during swaps. Incidents, patient custody, unit positions and the simulation clock must continue. Record request-to-control latency for a unit in an already loaded area. A provisional target is under one second on the baseline PC. Report remote-area loading separately if streaming is introduced.

### 2. Shorten time to first drive

- Profile a standalone build on a recorded PC and storage device. Time launch to menu and selection to first controllable frame separately. Record first launch after a reboot and repeated launches, with the same station, build and graphics settings. Capture scene read/deserialization, activation, startup callbacks, host snapshot wait and local unit binding. Include a trace and identify the largest measured cost.
- Fix the dominant cost first. If callbacks dominate, inspect road graph construction, searches for hospital bays and scene-wide object scans. Bake static data or references during the editor build step where the capture supports it. If host synchronization dominates, overlap it with safe local loading. If whole-city content dominates, prototype a persistent session scene and additive geographic scenes.
- For that prototype, load the selected station, its surroundings, drivable roads and collision first. Stream neighboring areas ahead of travel; keep distant simulation on the host. On a distant unit switch, prepare the destination area and bind the camera and controls when its geometry and collision are ready. Keep the current view responsive during preparation. Measure peak memory during transitions.
- Compare before/after timings on the same machine, verify first input actually moves the unit, and drive across a region boundary at speed while a call is active. Record the longest frame during streaming and check for gaps, missing collision and duplicate units. Set the cold-start target after the baseline capture. Async loading can improve responsiveness while the total work still needs to be reduced.

**Claude handoff:** Start with a baseline trace and the in-place unit-switch PR. Include the scene-load count, switch latency and state-continuity checks. Use the startup trace to propose the smallest effective cold-start change in a separate PR before restructuring city assets.

## Follow-up notes for future work

Discussion items from November 26, 2026. The first Claude assignment remains fleet authority. These items are candidates for later work.

### Map preparation and road diagnostics

Houston GIS data is imported during city authoring. Starting the game loads the saved scene. The AI fleet separately builds a routing graph from packaged street files. Preserve that distinction when profiling startup. A future host can consume road and facility data exported from the same source as the Unity assets.

Wayne has seen vehicles struggle in some areas and turn abruptly. The affected intersections and vehicle types still need a reproducible example. For a civilian car, draw its intended lane and crossing and display what it is waiting for. For an emergency unit, draw its route and the nodes selected at each end. Capture a failing intersection before changing the routing rules.

### Smoother vehicle turns

Prototype one left and one right turn near Station 7 with a sedan and Ladder 7. Join the incoming and outgoing lanes with a curve, advance the vehicle along it by distance traveled, and align its heading to the curve. Tune speed and turning radius for the longer apparatus. Watch from above and at curb height. Check that vehicles enter the intended lane smoothly and the ladder's rear wheels clear the curb. Preserve traffic signals, yielding and dispatch behavior.

### Asset purchase criteria

Evaluate purchases against one playable scene and include integration time. Try the turning prototype before evaluating a steering package. For crew AI, try a medic/stretcher scenario and look for time saved debugging interrupted or blocked actions. Consider paired stretcher animations at the patient-care milestone and reusable environment props when dressing city blocks. No purchase has been selected.

### Gameplay promotion

Explore a 30–45 second promo captured from actual gameplay. Start with an eight-second curbside shot of Engine 7 leaving Station 7 during an F9 test dispatch. Review how its turn looks before expanding the shoot. A proposed capture feature follows a selected AI unit independently of the player and toggles the HUD. A first cut could move from dispatch audio and the station drive-by to a rear-quarter follow shot, a street-level arrival, and a player operating the water monitor, ending on the HTownSim title. Separate takes are sufficient for the first cut.

## Future phase: playable characters

The player joins the city as a character who can drive an apparatus, step out and work a call on foot. Build on the host's unit claims and Unity's in-place control switching. Track character identity, crew assignment and seat occupancy separately. Stepping out at a call should keep the character assigned to the crew and the apparatus parked on scene. Explicitly releasing the unit remains a separate action.

**Proposed first playable milestone:** Drive Engine 7 to a call, park, exit at a clear spot beside the cab, walk around the engine, return to the driver's door and resume driving in the same city session. Transfer input, camera and HUD together. Verify that another client sees the firefighter outside, the truck stays parked, the incident continues and only one character can occupy the driver seat. If the exit is obstructed or the seat claim fails, preserve the player's current control state.

Use this loop as the foundation for a later role interaction, such as a medic walking from the ambulance to a patient. Camera perspective, character appearance and the first on-foot job are open design choices.
