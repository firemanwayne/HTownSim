# Station 8 apparatus: Engine 08, Engine 01, Medic 08

Station 8's bays hold copies of the Engine 07 and Ambulance 07 models with their own numbers. They are static, parked
props (box collider, no lights or sound), standing where Station 7's bays are relative to the station building. Seen from the street,
left to right: **Medic 08**, **Engine 01** (where Station 7 keeps its ladder; its bay sign reads ENGINE 1) and **Engine 08**.

## Art

The numbers are drawn into the livery and decal textures by the same scripts that draw Engine 07 and Ambulance 07
(system Python with Pillow). Set `HFD_UNIT_NUMBER` to the number to draw; the default `07` regenerates the originals unchanged.

```powershell
cd "Blender/HFD Apparatus/Engines"
$env:HFD_UNIT_NUMBER = "08"; python make_livery.py; python make_decals_07.py
$env:HFD_UNIT_NUMBER = "01"; python make_livery.py; python make_decals_07.py
cd ../EMS
$env:HFD_UNIT_NUMBER = "08"; python make_livery_amb.py
```

Output goes to `Assets/HTownSim/Vehicles/Station8` (`HFD_OUT_DIR` overrides it): `Engine08_Livery.png`, `Engine08_Decals.png`,
`Engine01_Livery.png`, `Engine01_Decals.png`, `Medic08_Livery.png`. The shared logo and flag textures are not changed.

## In the scene

With the scene saved and Play Mode off, run **HTownSim > Station 8 > Add Apparatus** (`AddStation8Apparatus.Apply`). It
gives the new textures the import settings of the ones they replace, makes copies of the paint and decal materials that use them
(under `Vehicles/Station8/Materials`), parks the three under `Station8Apparatus`, relabels the middle bay sign, and saves the
scene. It can be run again; **Remove Apparatus** (`Revert`) takes them away and restores the sign.

## Driving them

The start screen asks for a station first, then an apparatus. Choosing a Station 8 apparatus moves the driven rig into that
bay and gives it the parked unit's livery and number (the parked unit is hidden for the run). It keeps Engine 07's
handling, siren and lights (Engine 08, Engine 01) or Ambulance 07's (Medic 08), under the new unit name in the HUD, and
Engine 07 is parked back at Station 7. Station 8 is greyed out on the start screen until **Add Apparatus** has been run on the scene.
