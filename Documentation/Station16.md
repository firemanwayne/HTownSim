# Station 16

Fire Station 16 (1700 Richmond Ave, Montrose) is the copy of Station 7's building that the neighborhood builder places (see `Neighborhoods.md`).
**HTownSim > Station 16 > Add Apparatus** parks its four units: **Ambulance 16** (left bay), **Ladder 16** (middle), **Engine 16** (right) and
**Supervisor 16** on the apron beside the left bay. Run it with the scene saved and Play Mode off.

- Engine 16 and Ambulance 16 are the Engine 07 and Ambulance 07 models with the number 16 painted by the art scripts
  (`HFD_UNIT_NUMBER=16 HFD_OUT_DIR=<Vehicles/Station16>` for `Engines/make_livery.py`, `Engines/make_decals_07.py`, `EMS/make_livery_amb.py`).
- Ladder 16 is the Ladder 07 model (its aerial rig moves to the Station 16 bay when you pick it; the livery still carries Ladder 07's lettering).
- Supervisor 16 is a red Chevrolet Suburban built in Blender (`Blender/HFD Apparatus/EMS/build_supervisor16.py`, headless:
  `blender -b --python build_supervisor16.py -- export`), after the real Supervisor unit: white band with red pin lines, HFD shield on both
  front doors, push bar, roof light bar, and SUPERVISOR 16 with the HFD emblem on the black rear-quarter windows, mirrored on both sides.
  It drives like a tight, quick EMS unit (no water, EMS tones, plain horn).

On the start screen Station 16 is the third station; picking an apparatus works like Station 8 (`ApparatusLoadout.ApplyStation16`).
It is greyed out until the apparatus have been added to the scene.
