# HTownSim task board

The project's task list, kept in Git so it moves with the code; each open task is also a GitHub issue (the Issue column), carrying the same priority and kind labels, on the [HTownSim Task Board](https://github.com/users/firemanwayne/projects/2) project (Todo, In Progress, Done). One row per task. To work the board: move a row between sections (Backlog, In progress, Done), keep its ID, and commit the change with the work. New tasks take the next free ID.

Priority: **P1** blocks testing or players, **P2** should happen soon, **P3** when convenient.
Kind: `bug`, `feature`, `ops` (build, deploy, tooling), `debt` (cleanup, noise).

## In progress

| ID | Pri | Kind | Task | Notes | Issue |
|---|---|---|---|---|---|
| T-001 | P1 | ops | Deploy the batched server changes to Docker and test everything against the real server | Waiting on: ViewerRole enforcement, AI lights, Ambulance 07 fleet unit, incident-service container. Docker builds are paused until enough changes pile up (decided 2026-11-27). Run `Tools/CityServer/build-server.ps1`. | [#3](https://github.com/firemanwayne/HTownSim/issues/3) |
| T-002 | P1 | bug | Test the full-screen map (N) in the running game | Everything else from the original test pass (unit-visibility roles, AI lights and horn, driving numbers, bay-apron exit and spacing, unit-switch carry-over) was verified working by the user (2026-11-27). | [#4](https://github.com/firemanwayne/HTownSim/issues/4) |

## Backlog

| ID | Pri | Kind | Task | Notes | Issue |
|---|---|---|---|---|---|
| T-003 | P2 | bug | Spectator "Join City" button does not respond to a click | Worked around with Enter/Space and `-city-autojoin`. Root cause not found: the window rendered a dark frame with no panel when the click was tried. Synthetic key presses reach the game; the physical click did not. | [#11](https://github.com/firemanwayne/HTownSim/issues/11) |
| T-004 | P2 | feature | Authenticated viewer roles | Today every connection is granted Spectator (sees every unit) and may ask for less (`ROLE Fire`). When player connections exist, the server must assign the role from the unit the player drives. Rule lives in `Contracts/ViewerRole.cs`. | [#5](https://github.com/firemanwayne/HTownSim/issues/5) |
| T-005 | P2 | feature | Server-authoritative incidents and unit control | Roadmap item; the player drives their own vehicle and reports its pose, the server owns everything else. Stage 1 (server rebuilt in Docker and checked with raw socket clients on 2026-11-27: two clients at once, a taken unit refused, poses relayed, role set from the unit and never widened, a seated unit not sent back to its player, LEAVE and drop free the unit): the server takes any number of connections; `SEAT`/`LEAVE`/`P` commands seat a client in a parked unit, mirror its reported pose to the others, assign its viewer role from the unit (the core of T-004) and hand the unit back to the computer when it leaves or drops. Stage 2 (the server half is verified the same way: both seated players were sent the test fire's `D` call; the game client was run in the Editor against the Docker server on 2026-11-27: it took a seat, its truck's pose reached other clients, a seat switch freed the old unit, and a test fire's call reached its HUD; finishing a call from the client was not run): start the client with `-city-player` (and `-city-host <address>`), pick a unit as usual; the truck drives locally, reports its pose, and the server sends it calls (`D` line, rebuilt into the local incident manager) and takes its completion (`DONE`). A leaving player's unfinished call carries on as a computer-driven unit. Stage 3 (checked 2026-11-27 against the rebuilt Docker server, with the Editor and a Windows player as two game clients at once, both seated and both sent a test fire): the server streams the incident board (`BS`, `B`, `BE` once a second; the HUD shows it with FIRST DUE / YOUR UNIT for the player's unit) and the map shows the server's units to a player; the player's own call is not drawn a second time from the server's fire; a unit that is driving home can be taken; an Online card on the start screen (key 5) joins the server's city as the picked fire unit, and a spectator who picks a unit (U) takes it there; finishing a call from the client sends `DONE` and frees the unit for the next call. A player's patrol car (HPD Patrol 46837) is seated in the first free server patrol unit (HPD Unit 1 to 4; checked against the Docker server: four patrol clients seated, the next two told every patrol unit is taken; online police are started with `-city-player` and the HPD service, the Online card is the fire department's). Left: retire `CityCarryOver` in single-player once the server is the only city; the `-autostart` pick can name a unit the server does not have | [#6](https://github.com/firemanwayne/HTownSim/issues/6) |
| T-006 | P2 | feature | Multiplayer seating | `Seats` in `ApparatusProfileValues` are data only (EO, Officer, FF, FF; ambulance EO, FF, Patient, Patient). Nothing seats anyone yet. | [#7](https://github.com/firemanwayne/HTownSim/issues/7) |
| T-007 | P3 | bug | A spectator keeps stale units when its role narrows mid-session | Units already shown stay at their last pose. Only matters if roles can change after connecting. | [#8](https://github.com/firemanwayne/HTownSim/issues/8) |
| T-008 | P3 | feature | Lens glow halos on the AI ambulance (and the steady headlamps) | The player's ambulance has them (`Amb07Lenses`); the AI copy does not. | [#9](https://github.com/firemanwayne/HTownSim/issues/9) |
| T-009 | P3 | debt | 320 "Trying to access a shader but no shaders were included" lines in the Linux server log | Harmless. Not from the AI lights (checked). Printed right after the six audio clips are set up. No managed stack trace, so it needs another approach to find (`HTOWNSIM_SERVER_TRACE=1` adds script traces to a server build, which did not help). | [#12](https://github.com/firemanwayne/HTownSim/issues/12) |
| T-010 | P3 | feature | Show the spectator and map units for chiefs and EMS supervisors separately | Chiefs and supervisors share one `UnitBehavior` row, so both count as fire for visibility. | [#10](https://github.com/firemanwayne/HTownSim/issues/10) |

## Done

| ID | Done | Task |
|---|---|---|
| T-100 | 2026-11-26 | City server runs in Docker with a healthcheck; build script `Tools/CityServer/build-server.ps1` |
| T-101 | 2026-11-26 | Windows spectator: free-fly camera, hideable map (M), no assignment HUD, Spectator card in the service menu, U to leave |
| T-102 | 2026-11-26 | F9 test fire with units responding, visible fire, lights, siren and horn streamed to the spectator |
| T-103 | 2026-11-26 | AI units: spacing, station-wall clearance, horn at junctions and blocked roads |
| T-104 | 2026-11-27 | Per-vehicle-kind profile: flash pattern, sirens, driving, horn, seats and transport defined once per kind (`UnitBehavior`, `ApparatusProfileValues`) |
| T-105 | 2026-11-27 | Seats corrected to the roadmap's confirmed staffing |
| T-106 | 2026-11-27 | AI engine roof bar flashes like the player's; ladder, ambulance and chief AI lights read the profile's flash groups |
| T-107 | 2026-11-27 | Harvester publishes with the Windows build (NuGet.Config fixes NU1507) |
| T-108 | 2026-11-27 | Incident service runs in its own container; server reaches it over the compose network |
| T-109 | 2026-11-27 | Unit switch keeps the city: AI units, fires, incident board and the unit left behind carry on (`CityCarryOver`); Ambulance 07 becomes an AI unit via the `Medic07` prop |
| T-110 | 2026-11-27 | Whole-screen city map (N) |
| T-111 | 2026-11-27 | Unit-location visibility is a server-enforced permission (`ViewerRole`): spectator and police see all, fire and EMS see fire and EMS |
| T-112 | 2026-11-27 | Join the city with Enter/Space or `-city-autojoin`; build script tolerates Unity's stderr notice |
| T-113 | 2026-11-27 | Spartan rear-mount ladder on every ladder: one Blender asset exported per unit number (`export_spartan_unity.py`, `HFD_UNIT_NUMBER` 07 and 16), installed by `AgentScripts/InstallSpartanLadder.cs`; new roof lightbars flash as two alternating groups (driver red + officer white, officer red + driver white); Ladder 16 has its own model, replacing the cut-out-and-overlay renumbering |
| T-114 | 2026-11-27 | Reusable Spartan engine and Frazer ambulance on every engine and ambulance: one Blender asset each, exported once per unit with the number baked into its paint (`Blender/HFD Apparatus/export_reusable_unity.py`; `HFD_UNIT_NUMBER`, `HFD_MEDIC_LABEL` for Medic 08), installed by `AgentScripts/InstallReusableApparatus.cs`; engines flash by lens material like the ladders; old Engine43/Hi3D/Ambulance07/Ladder07 art, the Station 8/16 livery folders, the hidden legacy scene visuals, `ApparatusLights` and the old one-off installers removed (`RetireLegacyVehicleVisuals`) |

## Dropped

| ID | Task | Why |
|---|---|---|
| T-900 | Find the cause of two Unity batch builds that hung at "Package Manager log level set" | Not seen again; dropped by the user (2026-11-27). |
