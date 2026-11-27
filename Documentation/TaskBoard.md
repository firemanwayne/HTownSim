# HTownSim task board

The project's issue tracker, kept in Git so it moves with the code. One row per task. To work the board: move a row between sections (Backlog, In progress, Done), keep its ID, and commit the change with the work. New tasks take the next free ID.

Priority: **P1** blocks testing or players, **P2** should happen soon, **P3** when convenient.
Kind: `bug`, `feature`, `ops` (build, deploy, tooling), `debt` (cleanup, noise).

## In progress

| ID | Pri | Kind | Task | Notes |
|---|---|---|---|---|
| T-001 | P1 | ops | Deploy the batched server changes to Docker and test everything against the real server | Waiting on: ViewerRole enforcement, AI lights, Ambulance 07 fleet unit, incident-service container. Docker builds are paused until enough changes pile up (decided 2026-11-27). Run `Tools/CityServer/build-server.ps1`. |
| T-002 | P1 | bug | Test pass of everything that compiles but has not been seen running | Full-screen map (N), unit-visibility roles (`-city-view-as Fire`), AI ladder/ambulance/chief lights, AI horn, per-kind driving numbers, bay-apron exit and spacing, unit-switch carry-over. |

## Backlog

| ID | Pri | Kind | Task | Notes |
|---|---|---|---|---|
| T-003 | P2 | bug | Spectator "Join City" button does not respond to a click | Worked around with Enter/Space and `-city-autojoin`. Root cause not found: the window rendered a dark frame with no panel when the click was tried. Synthetic key presses reach the game; the physical click did not. |
| T-004 | P2 | feature | Authenticated viewer roles | Today every connection is granted Spectator (sees every unit) and may ask for less (`ROLE Fire`). When player connections exist, the server must assign the role from the unit the player drives. Rule lives in `Contracts/ViewerRole.cs`. |
| T-005 | P2 | feature | Server-authoritative incidents and unit control | Roadmap item. Unit-switch carry-over (`CityCarryOver`) is a single-player stopgap; with the city on the server a switch would not reload anything. |
| T-006 | P2 | feature | Multiplayer seating | `Seats` in `ApparatusProfileValues` are data only (EO, Officer, FF, FF; ambulance EO, FF, Patient, Patient). Nothing seats anyone yet. |
| T-007 | P3 | bug | A spectator keeps stale units when its role narrows mid-session | Units already shown stay at their last pose. Only matters if roles can change after connecting. |
| T-008 | P3 | feature | Lens glow halos on the AI ambulance (and the steady headlamps) | The player's ambulance has them (`Amb07Lenses`); the AI copy does not. |
| T-009 | P3 | debt | 320 "Trying to access a shader but no shaders were included" lines in the Linux server log | Harmless. Not from the AI lights (checked). Printed right after the six audio clips are set up. No managed stack trace, so it needs another approach to find (`HTOWNSIM_SERVER_TRACE=1` adds script traces to a server build, which did not help). |
| T-010 | P3 | feature | Show the spectator and map units for chiefs and EMS supervisors separately | Chiefs and supervisors share one `UnitBehavior` row, so both count as fire for visibility. |

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

## Dropped

| ID | Task | Why |
|---|---|---|
| T-900 | Find the cause of two Unity batch builds that hung at "Package Manager log level set" | Not seen again; dropped by the user (2026-11-27). |
