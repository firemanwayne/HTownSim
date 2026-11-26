# H-TownSim — Development Roadmap

Updated November 26, 2026.

## Vision
A multiplayer-first, server-authoritative, persistent-world Houston-based city simulator where fire, EMS, police, hospitals, public works and civilians operate within an interconnected city ecosystem influenced by real-world activity.

Milestones are ordered by dependencies rather than committed dates. P0 = critical, P1 = high, P2 = planned, P3 = future. Implementation status requires validation against the Unity project.

## Architectural requirements

Multiplayer is a foundational product requirement, not a post-release feature. Clients submit inputs and commands; a dedicated Unity server owns real-time simulation and validates actions. Multiple players can occupy one apparatus in distinct roles, with AI filling vacant positions and taking over disconnected roles where appropriate. World continuity must not depend on a particular player being connected.

### Proposed technology baseline

| Layer | Proposed choice |
|---|---|
| Client / simulation host | Unity 6 / headless Linux dedicated server |
| Game networking | UDP-based transport; prototype Unity Netcode for GameObjects, compare Entities, FishNet, Mirror and Photon Fusion |
| Persistent backend | ASP.NET Core modular monolith, EF Core |
| Primary database | PostgreSQL with PostGIS |
| Hosting | Azure VM initially; evaluate PlayFab Multiplayer Servers and AWS GameLift for later scale |
| Identity | Steam authentication with server-side validation |
| Administrative / CAD UI | Blazor and SignalR |
| Integration events | Transactional outbox; Azure Service Bus when needed |
| Snapshots / replays | Azure Blob Storage or portable object-storage abstraction |
| Cache | In-process initially; Redis when justified |
| Deployment / telemetry | Docker where appropriate, GitHub Actions, OpenTelemetry |

Keep gameplay networking separate from durable business events. Do not use SignalR or Service Bus for vehicle physics replication. Avoid direct database access from clients, premature microservices and Kubernetes. The Unity server and backend must have explicit ownership boundaries for durable state so that two processes cannot independently change fleet or incident authority.

## 1. Multiplayer foundation and dedicated server (P0)
- [ ] Audit current single-player assumptions, including local input, player-relative traffic spawning, scene resets, timers, pause, apparatus selection and F9 test harness.
- [ ] Separate presentation from simulation and move authoritative gameplay into the dedicated server.
- [ ] Evaluate networking frameworks against vehicle prediction, interpolation, character movement, multi-occupancy, interest management and bandwidth.
- [ ] Build headless server and connect two independent clients locally.
- [ ] Implement authentication, connection lifecycle, world selection, reconnect and authoritative command validation.
- [ ] Implement player/AI control handoff, seat reservation and concurrency protection.
- [ ] Implement spatial interest management and reduced update rates for distant entities.
- [ ] Establish server tick, CPU, memory, bandwidth and latency instrumentation.

Exit: Two clients share the same authoritative Engine 07, with EO and Officer roles, synchronized movement, lights, sirens and incident state.

## 2. Persistence and backend (P0)
- [ ] Establish local PostgreSQL/PostGIS and ASP.NET Core backend with EF Core migrations.
- [ ] Model global player accounts separately from world-specific characters and progression.
- [ ] Introduce WorldInstance identifiers and isolate fleet, personnel, incidents, hospitals and history by world.
- [ ] Persist departments, stations, apparatus, roles, qualifications, crew assignments, incident assignments, patients, transports and hospital capacity.
- [ ] Define authority boundaries, transactional invariants, optimistic concurrency, idempotency and outbox delivery.
- [ ] Persist significant operational transitions transactionally; checkpoint apparatus positions and world recovery state periodically.
- [ ] Keep wheel rotations, particles and high-frequency physics in memory; store graphics/control settings locally and static GIS/routing assets as versioned game assets.
- [ ] Implement versioned snapshots, recovery/reconciliation, backups and restore testing.
- [ ] Validate server restart, player reconnect, AI takeover and no duplicate incident or crew assignments.

Provisional checkpoint intervals: apparatus positions every 15–30 seconds, world snapshots every 1–5 minutes, critical assignments/progression on change. Tune by load and recovery objectives. Proposed initial replication rates: controlled apparatus 20–30 Hz, nearby entities 10–20 Hz, intermediate entities 2–5 Hz, distant entities event-driven or omitted. These are test hypotheses, not guarantees.

A persistent world may run continuously or pause when empty according to configurable policy. Final concurrency targets, empty-world policy, community-hosted servers and hosting vendor remain open decisions. Initial capacity test gates: 8–16, 32, 64, then 128+ concurrent players per world, contingent on profiling.

## 3. City simulation foundation (P0)
- [ ] Establish centralized fleet authority and synchronize apparatus availability.
- [ ] Validate F9 emergency response scenario (Engine 07, Ladder 07, Engine 08, Engine 16, Ambulance 16).
- [ ] Validate dispatch, response, scene operations, clearance and return to service.
- [ ] Implement patient transport and emergency room handoff.
- [ ] Support simultaneous incidents and autonomous city operations.

Exit: The city operates reliably without player intervention.

## 4. Map loading and performance (P0)
- [ ] Profile reported approximately 30-second startup time and main-thread bottlenecks.
- [ ] Process GIS at editor time wherever practical; avoid rebuilding the city from external GIS on each launch.
- [ ] Prebuild, validate and serialize road routing graph.
- [ ] Cache generated city assets and implement asynchronous loading.
- [ ] Evaluate Addressables, geographic region streaming and distance-based activation.
- [ ] Optimize building LODs, rendering and memory.

Proposed targets: initial startup 10 seconds, long-term 3–5 seconds, gameplay 60+ FPS on a defined target system. These are not measured results.

Pipeline: Houston GIS → Unity editor city builder → generated city assets and routing graph → optimized runtime loading.

## 5. Traffic AI and vehicle movement (P0)
- [ ] Replace single-player-centered traffic activation with server-authoritative multi-player region activation; synchronize shared traffic and collision outcomes.
- [ ] Diagnose problematic intersections and validate road connectivity.
- [ ] Implement curved intersection trajectories and fluid steering.
- [ ] Improve acceleration, braking and apparatus-specific turning radii.
- [ ] Improve collision avoidance, lane changes and traffic signal compliance.
- [ ] Implement civilian yielding to emergency apparatus and congestion.
- [ ] Evaluate paid Behavior AI, traffic and vehicle movement assets against existing systems.

Buy generic movement, character controllers, animation and visual assets where justified by saved development time. Retain control of core dispatch, fleet authority and incident simulation. Houston-specific apparatus may require custom assets.

## 6. Dispatch and incident simulation (P0)
- [ ] Implement CAD, incident classification and department-specific response protocols.
- [ ] Model stations, jurisdictions and closest appropriate available unit selection.
- [ ] Manage apparatus status, incident escalation, cancellation and additional resource requests.
- [ ] Model patients, hospital selection, transport and handoff.
- [ ] Record incident history, replay and operational statistics.

Lifecycle: incident generation → CAD → resource selection → response → scene operations → clearance → available.

## 7. Player characters and apparatus staffing (P1)
Transition from vehicle-focused simulation to multiplayer role-based character gameplay.
- [ ] Support multiple authenticated players occupying one apparatus, with server-validated role and seat allocation and AI filling unoccupied roles.
- [ ] Implement first-person controller and optional third-person camera.
- [ ] Enter/exit apparatus, switch available seats, interact with doors, compartments, tools and protective equipment.
- [ ] Support AI personnel alongside players and continued autonomous city operations.
- [ ] Implement crew assignment independently of seat occupancy, role qualifications and apparatus capabilities.

### Confirmed staffing
| Apparatus | Crew | Patient positions | Total positions |
|---|---|---:|---:|
| Engine | EO, Officer, 2 FF | 0 | 4 |
| Ladder | EO, Officer, 2 FF | 0 | 4 |
| Ambulance | EO, FF | 2 | 4 |
| Medic | EO, FF | 2 | 4 |
| District Chief | EO, Chief | 0 | 2 |
| Ambulance Supervisor | Officer | 0 | 1 |
| Police Patrol | 2 Patrolmen | 0 | 2 |

**EO = Engineer/Operator (Driver). FF = Firefighter.**

### Roles
- Engineer/Operator: apparatus driving, positioning, emergency lights and sirens, readiness and applicable equipment operation.
- Officer: crew supervision, radio communications and tactical decisions.
- Firefighter: suppression, rescue, equipment operations and patient care as qualified.
- District Chief: incident command and resource coordination.
- Ambulance Supervisor: EMS supervision and operational coordination.
- Patrolman: police incident response and law enforcement.

### Engineer/Operator gameplay
- [ ] Emergency response driving and apparatus positioning.
- [ ] Engine pump engagement, water supply, tank management and discharge pressures.
- [ ] Ladder stabilization, aerial positioning and applicable controls.
- [ ] EMS driving, patient transport and hospital arrival.

### Proposed domain model
| Entity | Responsibility |
|---|---|
| Personnel | Individual player or AI character |
| PersonnelRole | EO, Officer, FF, Chief, Patrolman |
| Qualification | Permitted operational capabilities |
| CrewAssignment | Personnel assigned to apparatus |
| Apparatus | Vehicle and equipment configuration |
| ApparatusSeat | Physical seating configuration |
| SeatOccupancy | Current seat occupant |
| ApparatusCapability | Pump, aerial, transport and other capabilities |
| PatientPosition | Patient transport capacity |
| IncidentAssignment | Operational incident assignment |

A firefighter remains assigned to an apparatus while outside it. Qualifications, crew assignments and seat occupancy must be independently modeled.

## 8. Emergency response gameplay (P1)
- [ ] Engine operations: pump controls, hose deployment, water supply.
- [ ] Ladder operations: aerial deployment, stabilization and rescue.
- [ ] Fireground: search, rescue, suppression and ventilation.
- [ ] EMS: patient assessment, simulated treatment, loading, transport and handoff.
- [ ] Incident command: task assignments, escalation and resource management.
- [ ] Police: patrol, traffic stops and scene security.
- [ ] AI crew: autonomous role-specific task execution.

Vertical slice: a player receives an assignment, responds through a functioning city, exits apparatus, performs scene operations, clears the incident and returns to service while the rest of Houston operates autonomously. Focus on fire and EMS first.

## 9. Live Houston data integration (P2)
- [ ] Evaluate permitted radio-derived incidents, Houston TranStar traffic, weather, hospital data and periodic GIS updates.
- [ ] Build external .NET data ingestion, validation and normalization service.
- [ ] Implement simulation backend event processing and distribution to Unity.
- [ ] Support recorded/offline incident scenarios.
- [ ] Verify data access, licensing, privacy and commercial-use permissions; anonymize, delay or synthesize sensitive incident data.

Potential technologies: ASP.NET Core, SignalR, Azure Service Bus, background workers and Redis. MCP server is deferred development tooling, not a gameplay dependency.

## 10. Expanded playable professions (P2)
- [ ] Police officer: patrol and law enforcement.
- [ ] Nurse/hospital personnel: intake, triage, treatment and capacity.
- [ ] Public works: infrastructure maintenance, road closures and utility incidents.
- [ ] Dispatcher: CAD and resource coordination.
- [ ] Civilian: exploration, driving, interactions and incident reporting.

Firefighter, Engineer/Operator, Officer, District Chief, EMS personnel and Ambulance Supervisor are covered by the core role system. Avoid separate clients until meaningful gameplay exists.

## 11. Visual polish and promotional content (P2, parallel)
- [ ] Create Engine 07 cinematic drive-by using actual gameplay.
- [ ] Implement AI-follow, chase, aerial incident and EO cab cameras.
- [ ] Implement cinematic transitions, apparatus tracking and incident replay.
- [ ] Improve authentic Houston apparatus, emergency lights, radio/environmental audio and day/night lighting.
- [ ] Produce actual gameplay promotional footage.

## 12. Multiplayer alpha, scalability and commercial release
- [ ] Test concurrent crews, simultaneous incidents, network latency/loss, reconnects, server recovery and sustained load.
- [ ] Validate deployment, monitoring, operational cost and capacity before public alpha.
| Gate | Deliverable |
|---|---|
| Technical prototype | Two-client authoritative multiplayer and restart recovery |
| Vertical slice | Complete cooperative fire/EMS incident |
| Internal alpha | Stable simulation and role-based gameplay |
| Closed alpha | External player feedback |
| Public demo | Downloadable gameplay demonstration |
| Investor evaluation | Demonstrable product and business plan |
| Steam Early Access | Playable fire and EMS operations |
| Major update | Police operations |
| City expansion | Hospitals, public works and civilians |
| Version 1.0 | Integrated city ecosystem |
| Post-release | Additional city expansion and multiplayer scale improvements |

- [ ] Prepare business plan and evaluate funding opportunities.
- [ ] Establish Steam store presence.
- [ ] Release playable demo, conduct closed alpha and prepare Early Access.
- [ ] Evaluate MCP tooling and additional cities (P3).

## Immediate priorities
1. Multiplayer architecture audit and networking proof of concept: two clients, one dedicated server.
2. Server-authoritative Engine 07 with EO and Officer occupancy, synchronized movement, lights, sirens and dispatch.
3. PostgreSQL persistence, backend authority boundaries, reconnect and server restart recovery.
4. Centralized fleet authority, F9 lifecycle validation, patient transport and ER handoff.
5. Startup profiling, cached city assets, routing graph, traffic pathfinding and smooth vehicle motion.
6. Character controller, multi-occupant apparatus and autonomous AI crew.
7. Complete cooperative fire/EMS vertical slice and multiplayer load testing.

This roadmap captures the Work discussion and proposed design decisions. Staffing is based on stated requirements; performance targets, architecture and release gates are proposals. Source code implementation status has not been independently verified.
