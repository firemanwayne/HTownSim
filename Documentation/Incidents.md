# Incidents

Every emergency in HTownSim starts the same way: a standard incident JSON goes to the **IncidentManager**
(`Assets/HTownSim/Runtime/Incidents/Managers/IncidentManager.cs`). The city's data, a future scanner-transcript
feed and the random fallback calls all use it, so a call from any of them is dispatched, shown and scored identically.

## The payload

```json
{
  "incidentId": "COH-2026-11-23-0042",
  "agency": "HFD",
  "callType": "STRUCTURE_FIRE",
  "coordinates": { "lat": 29.740445, "lon": -95.371423 },
  "severity": 3,
  "requiredUnits": ["ENGINE", "LADDER"],
  "address": "1402 Elgin St",
  "source": "coh-mcp"
}
```

| Field | Required | Values |
| --- | --- | --- |
| `incidentId` | yes | Unique text, up to 64 characters. An id already received this session is ignored. |
| `agency` | yes | `HFD`, `EMS` or `HPD` (also accepts `FIRE`, `MEDICAL`, `POLICE`). |
| `callType` | yes | `STRUCTURE_FIRE`, `VEHICLE_FIRE`, `BRUSH_FIRE`, `FIRE_ALARM`, `MEDICAL`, `TRAFFIC_ACCIDENT`, `HAZMAT`, `RESCUE`. Any other text is accepted and shown as sent. Case, spaces, `_` and `-` do not matter. |
| `coordinates` | yes | `lat` and `lon` in decimal degrees (WGS84). `0, 0` counts as missing. |
| `severity` | yes | Whole number 1 (minor) to 5 (major). |
| `requiredUnits` | no | Any of `ENGINE`, `LADDER`, `AMBULANCE`, `RESCUE`, `HAZMAT`, `BATTALION`, `PATROL`. Empty or missing means any unit. Unknown names are kept as "other". |
| `address` | no | Shown in the HUD. Without it the nearest street (or the coordinates) is shown. |
| `source` | no | Free text for the log. Defaults to the file name or `fallback`. |

A batch is `{ "incidents": [ {...}, {...} ] }`. A `.jsonl` file holds one incident object per line.

A payload is refused (and logged with the reason) when a required field is missing or invalid, when the id was already
received, when the position is outside the dispatch area, or when 50 calls are already waiting.

## Coordinates

`GeoProjection` (`Runtime/Incidents/Projections/GeoProjection.cs`) maps latitude and longitude into the scene with the same constants the GIS import
uses: the Station 7 geocode (29.73948387, -95.3734589) is the origin, the street grid is rotated 35.06 degrees, +Z is the
top of the map. The playable area is scene x -845..560, z -450..1575; a call outside it is refused.

A call within 30 m of one of the scene's storefront sites burns at that storefront (with its shop sign). Anywhere else
inside the area it burns at the exact spot, with no sign.

## Sending calls

**Inbox folder (for other programs).** Write a `.json` or `.jsonl` file into

```
%USERPROFILE%\AppData\LocalLow\DefaultCompany\HTownSim\Incidents
```

(the game also logs the folder at start). The game picks the file up within about a second once it has stopped
changing, and moves it to `processed\` (or to `rejected\` with a `.error.txt` listing the reasons).
Write the file under another name or extension and rename it when it is complete, to be safe.

**From code.** `IncidentManager.Instance.Submit(json, "my-source")` returns how many calls were accepted and the
refusal reasons.

**Fallback.** When no outside call is waiting, the old random spawn runs through the same door: `FallbackIncidentSource`
builds a payload (source `fallback`, ids `FB-0001`...) and submits it as JSON. It asks for the player's own
apparatus, so a random call is always one the player is assigned to. An ambulance gets an EMS medical call; engines and
ladders get a structure fire (ladders and severity 3+ also ask for `LADDER`).

## In the game

- The most severe waiting call is dispatched first (the oldest of equals). An outside call is dispatched about
  3 seconds after the previous one clears; a fallback call waits out the 25 second free-roam delay.
- The dispatch tone follows the agency: EMS calls play the EMS high/low tone, others the station ring and beeps.
- The banner shows the call type, location, severity, and whether the driven unit is assigned
  ("NOT ASSIGNED (needs ENGINE, LADDER)" still lets you respond).
- Fire calls play as before: hold E to spray water. Other calls (medical, traffic, alarm, ...) have no fire; stop within
  range of the marker and hold still for 8 seconds to clear them. Higher severity shortens the response time.
- An EMS call (agency `EMS`, or call type `MEDICAL`) shows a **downed citizen**: one of the city's pedestrian models
  (the ones TrafficSystem uses), placed 2.5 m in front of the storefront (or at the exact spot) and left to collapse as
  a ragdoll, then frozen once settled. The patient stays until the call clears, then is removed after 6 seconds.
- `IncidentManager` raises `Accepted`, `Rejected` and `Completed` (cleared, or failed when time ran out) for anything
  that wants to follow along, such as the live radio feature.

## Samples

`Documentation/Incidents/samples/` has a structure fire, a medical call and a batch to copy into the inbox folder.
