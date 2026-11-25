# Tuning guide: where each knob is

Every tunable number in the runtime code carries a doc comment next to it that says what it is and what raising or lowering it does
("Raise it for ...; lower it for ..."). This page is the map: what you want to change, and where the number lives. Open the file and
read the comment on the constant (they are plain `const` values or serialized fields; change the value, let Unity recompile).

When you add a new tunable number, give it the same kind of comment: what it is, its unit, and what raising and lowering it does.

## Driving

| I want to ... | Change | Where |
|---|---|---|
| Make every unit faster or slower | `TopSpeed` (metres per second; 22 is about 49 mph) | `Runtime/Player/FireResponse.cs` |
| Change how fast units pick up speed | `Acceleration` | `FireResponse.cs` |
| Change reverse speed | `ReverseSpeed` | `FireResponse.cs` |
| Make one unit turn tighter or wider | `turnRate` (higher turns tighter) | the unit's `ApparatusProfileValues` in `Runtime/Player/ApparatusLoadout.cs` (`AmbulanceAs`, `PatrolAs`, ...), documented on `Runtime/Contracts/ApparatusProfileValues.cs` |
| Make steering ease off at speed | `SteerEaseSpeed`, `SteerEaseRate` | `FireResponse.cs` |
| Move the chase camera | `cameraDistanceScale` per unit, zoom limits `MinimumZoom`, `MaximumZoom`, `ZoomPerNotch` | `ApparatusLoadout.cs`, `FireResponse.cs` |
| Change how hard crashes are | `MinimumImpactSpeed`, `ImpactRetainedSpeed`, traffic `CarKnockFactor`, `CarMass` | `FireResponse.cs`, `Runtime/World/TrafficSystem.cs` |
| Change ladder and outrigger speeds | `ElevationSpeed`, `SlewSpeed`, `ExtensionRate`, `OutriggerSeconds`, `JackSeconds`, `MaximumElevation` | `Runtime/Units/AerialController.cs` |

## Sound

| I want ... | Change | Where |
|---|---|---|
| Louder or quieter sirens and horn (player) | `SirenVolume`, `HornVolume` | `FireResponse.cs` |
| Louder or quieter engine | `MasterVolume`, `RoadVolume`, `TurboVolume`, `SquealVolume`, `AirBrakeVolume`, `AlarmVolume` | `Runtime/Units/EngineSound.cs` |
| Change how revs and gears behave | `RpmRiseRate`, `RpmFallRate`, `GearTop`, `ShiftDrop`, `HeavyPitch` | `EngineSound.cs` |
| Computer-driven units' siren loudness and range | `SirenVolume`, `SirenMinimumDistance`, `SirenMaximumDistance` | `Runtime/Units/AiEmergencyLights.cs` |
| City traffic, horns, birds | `CarVoices`, `CarMaxDistance`, `IdleVolume`, `MovingVolume`, `HornMinInterval`, `BirdMinInterval` | `Runtime/World/CityAmbience.cs` |
| Fire crackle range | `AudioMinDistance`, `AudioMaxDistance` | `Runtime/Player/LiveFireVfx.cs`, `Runtime/Simulation/FireSpread.cs` |
| Rain, weather blending | `RainRate`, `TransitionSeconds` | `Runtime/World/EnvironmentWeather.cs` |

## Lights

| I want ... | Change | Where |
|---|---|---|
| Faster or slower warning lights | `FlashHertz`, `DutyCycle`, `FlashSpread` | `Runtime/Units/ApparatusLights.cs` (engine), `LensFlasher.cs` (lens-mesh units), `PatrolLightbar.cs`, `AiEmergencyLights.cs` |
| Brighter or dimmer bloom on lenses | `FlashEmission`, `HaloAlpha`, `HaloSize` | the same files |
| Scene (flood) lights | `LightIntensity`, `LightRange`, `SpotAngle` | `Runtime/Units/SceneLights.cs` |
| Gap street traffic lights timing | `Cycle`, `GreenEnd`, `AmberEnd`, ... | `Runtime/World/GapSignalCycle.cs` |

## How runs (calls) are generated

| I want ... | Change | Where |
|---|---|---|
| Calls to arrive more or less often from the live feed | `_minimumGapSeconds`, `_maximumGapSeconds`, `_boardSize` | `Runtime/Simulation/Incidents/Models/LiveFeedSettings.cs` (can be overridden with a `LiveFeedSettings` asset in Resources) |
| More or less time between free-roam calls | `RoamCallDelay`, `MinimumCallDistance` | `FireResponse.cs` |
| How long the player has per call | `DispatchSeconds`, `SceneSeconds` | `FireResponse.cs` |
| Made-up calls: how severe | `MinSeverity`, `MaxSeverityExclusive`, `LadderSeverity` | `Runtime/Simulation/Incidents/Sources/FallbackIncidentSource.cs` |
| How many units a call gets | `MaximumUnits`, `MaximumOfficers` | `Runtime/Simulation/Incidents/Managers/FleetDispatcher.cs` |
| Rules for which unit goes | the rules in `Documentation/FleetAndDispatch.md` | `FleetDispatcher.cs`, `LiveDispatchRule.cs` |

## Computer-driven units

| I want ... | Change | Where |
|---|---|---|
| Faster or slower responses | `ResponseSpeed`, `ReturnSpeed`, `Acceleration`, `Braking` | `Runtime/Player/AiUnit.cs` |
| Longer or shorter time on scene | `FireSceneSeconds`, `SupportSceneSeconds`, `PatientSceneSeconds`, `HospitalSeconds` | `AiUnit.cs` |
| More or fewer patrol officers | `PatrolCount` | `Runtime/Player/AiFleet.cs` |
| Fires: growth, knock-down, how many | `GrowthSeconds`, `KnockdownSeconds`, `MaximumFires` | `Runtime/Player/LiveIncidentFires.cs` |
| Fire look | `MinScale`, `MaxScale`, `SmokeBoostMax`, `SmokeWind` | `Runtime/Player/LiveFireVfx.cs` |

## Traffic and the city

| I want ... | Change | Where |
|---|---|---|
| Busier or emptier streets | `CarCount`, `PedestrianCount` | `Runtime/World/TrafficSystem.cs` |
| Faster or slower traffic | `CruiseSpeedMin`, `CruiseSpeedMax` | `TrafficSystem.cs` |
| How traffic reacts to the siren | `YieldRange`, `StopRange`, `YieldHoldSeconds` | `TrafficSystem.cs` |
| Cloud cover and wind | `CloudCount`, `WindSpeed`, `ClearCloudFraction` | `Runtime/World/SkyDecor.cs` |

## HUD and map

| I want ... | Change | Where |
|---|---|---|
| Resize the top-left panel | `IncidentScale` | `Runtime/Player/HudLayout.cs` |
| Zoom the city map | `Zoom` | `Runtime/Player/CityMap.cs` |
| Fleet markers on the map | `ParkedSize`, `OutSize`, `ParkedAlpha` | `Runtime/Player/FleetMapMarkers.cs` |
| Move or resize the siren and aerial panels | `PanelWidth`, `Margin`, `BottomMargin`, aerial `StackGap` | `Runtime/Player/EmergencyHud.cs`, `AerialHud.cs` |

## Rendering performance

| I want ... | Change | Where |
|---|---|---|
| Hide tiny far objects for frame rate | `m_SmallMeshScreenPercentage` (0.5 now; 2 hid buildings) | `Assets/Settings/PC_RPAsset.asset` |
| Sleep far neighborhoods' traffic | `SimulationRadius` | `TrafficSystem.cs` |

Notes: the effects in the comments were written from reading the code and are not each tested. If a comment turns out to be wrong,
fix the comment next to the number.
