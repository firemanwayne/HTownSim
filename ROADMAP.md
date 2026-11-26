# H-TownSim — Development Roadmap

Updated November 26, 2026.

## Vision
An immersive Houston-based city simulator where fire, EMS, police, hospitals, public works and civilians operate within an interconnected city ecosystem influenced by real-world activity.

Milestones are ordered by dependencies rather than committed dates. P0 = critical, P1 = high, P2 = planned, P3 = future. Implementation status requires validation against the Unity project.

## 1. City simulation foundation (P0)
- [ ] Establish centralized fleet authority and synchronize apparatus availability.
- [ ] Validate F9 emergency response scenario (Engine 07, Ladder 07, Engine 08, Engine 16, Ambulance 16).
- [ ] Validate dispatch, response, scene operations, clearance and return to service.
- [ ] Implement patient transport and emergency room handoff.
- [ ] Support simultaneous incidents and autonomous city operations.

Exit: The city operates reliably without player intervention.

## 2. Map loading and performance (P0)
- [ ] Profile reported approximately 30-second startup time and main-thread bottlenecks.
- [ ] Process GIS at editor time wherever practical; avoid rebuilding the city from external GIS on each launch.
- [ ] Prebuild, validate and serialize road routing graph.
- [ ] Cache generated city assets and implement asynchronous loading.
- [ ] Evaluate Addressables, geographic region streaming and distance-based activation.
- [ ] Optimize building LODs, rendering and memory.

Proposed targets: initial startup 10 seconds, long-term 3–5 seconds, gameplay 60+ FPS on a defined target system. These are not measured results.

Pipeline: Houston GIS → Unity editor city builder → generated city assets and routing graph → optimized runtime loading.

## 3. Traffic AI and vehicle movement (P0)
- [ ] Diagnose problematic intersections and validate road connectivity.
- [ ] Implement curved intersection trajectories and fluid steering.
- [ ] Improve acceleration, braking and apparatus-specific turning radii.
- [ ] Improve collision avoidance, lane changes and traffic signal compliance.
- [ ] Implement civilian yielding to emergency apparatus and congestion.
- [ ] Evaluate paid Behavior AI, traffic and vehicle movement assets against existing systems.

Buy generic movement, character controllers, animation and visual assets where justified by saved development time. Retain control of core dispatch, fleet authority and incident simulation. Houston-specific apparatus may require custom assets.

## 4. Dispatch and incident simulation (P0)
- [ ] Implement CAD, incident classification and department-specific response protocols.
- [ ] Model stations, jurisdictions and closest appropriate available unit selection.
- [ ] Manage apparatus status, incident escalation, cancellation and additional resource requests.
- [ ] Model patients, hospital selection, transport and handoff.
- [ ] Record incident history, replay and operational statistics.

Lifecycle: incident generation → CAD → resource selection → response → scene operations → clearance → available.

## 5. Player characters and apparatus staffing (P1)
Transition from vehicle-focused simulation to role-based character gameplay.
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

## 6. Emergency response gameplay (P1)
- [ ] Engine operations: pump controls, hose deployment, water supply.
- [ ] Ladder operations: aerial deployment, stabilization and rescue.
- [ ] Fireground: search, rescue, suppression and ventilation.
- [ ] EMS: patient assessment, simulated treatment, loading, transport and handoff.
- [ ] Incident command: task assignments, escalation and resource management.
- [ ] Police: patrol, traffic stops and scene security.
- [ ] AI crew: autonomous role-specific task execution.

Vertical slice: a player receives an assignment, responds through a functioning city, exits apparatus, performs scene operations, clears the incident and returns to service while the rest of Houston operates autonomously. Focus on fire and EMS first.

## 7. Live Houston data integration (P2)
- [ ] Evaluate permitted radio-derived incidents, Houston TranStar traffic, weather, hospital data and periodic GIS updates.
- [ ] Build external .NET data ingestion, validation and normalization service.
- [ ] Implement simulation backend event processing and distribution to Unity.
- [ ] Support recorded/offline incident scenarios.
- [ ] Verify data access, licensing, privacy and commercial-use permissions; anonymize, delay or synthesize sensitive incident data.

Potential technologies: ASP.NET Core, SignalR, Azure Service Bus, background workers and Redis. MCP server is deferred development tooling, not a gameplay dependency.

## 8. Expanded playable professions (P2)
- [ ] Police officer: patrol and law enforcement.
- [ ] Nurse/hospital personnel: intake, triage, treatment and capacity.
- [ ] Public works: infrastructure maintenance, road closures and utility incidents.
- [ ] Dispatcher: CAD and resource coordination.
- [ ] Civilian: exploration, driving, interactions and incident reporting.

Firefighter, Engineer/Operator, Officer, District Chief, EMS personnel and Ambulance Supervisor are covered by the core role system. Avoid separate clients until meaningful gameplay exists.

## 9. Visual polish and promotional content (P2, parallel)
- [ ] Create Engine 07 cinematic drive-by using actual gameplay.
- [ ] Implement AI-follow, chase, aerial incident and EO cab cameras.
- [ ] Implement cinematic transitions, apparatus tracking and incident replay.
- [ ] Improve authentic Houston apparatus, emergency lights, radio/environmental audio and day/night lighting.
- [ ] Produce actual gameplay promotional footage.

## 10. Commercial release
| Gate | Deliverable |
|---|---|
| Technical prototype | Autonomous emergency response |
| Vertical slice | Complete playable fire/EMS incident |
| Internal alpha | Stable simulation and role-based gameplay |
| Closed alpha | External player feedback |
| Public demo | Downloadable gameplay demonstration |
| Investor evaluation | Demonstrable product and business plan |
| Steam Early Access | Playable fire and EMS operations |
| Major update | Police operations |
| City expansion | Hospitals, public works and civilians |
| Version 1.0 | Integrated city ecosystem |
| Post-release | Multiplayer and additional city expansion |

- [ ] Prepare business plan and evaluate funding opportunities.
- [ ] Establish Steam store presence.
- [ ] Release playable demo, conduct closed alpha and prepare Early Access.
- [ ] Evaluate multiplayer architecture, MCP tooling and additional cities (P3).

## Immediate priorities
1. Centralized fleet authority and F9 lifecycle validation.
2. Patient care, transport and ER handoff.
3. Startup profiling, cached city assets and road graph.
4. Traffic pathfinding and smooth vehicle motion.
5. Character controller, crew roles and apparatus occupancy.
6. Complete playable fire/EMS vertical slice.

This roadmap captures the Work discussion and proposed design decisions. Staffing is based on stated requirements; performance targets, architecture and release gates are proposals. Source code implementation status has not been independently verified.
