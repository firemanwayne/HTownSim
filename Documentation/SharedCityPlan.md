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
