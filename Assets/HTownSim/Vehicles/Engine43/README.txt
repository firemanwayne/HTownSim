Houston Engine 43 - reference-inspired visual prefab

Open Engine43Preview.unity to inspect the asset, or drag HoustonEngine43.prefab into a scene.
Unity 6000.6.3f1 / Universal Render Pipeline / TextMeshPro.
Forward +Z; up +Y; metres; pivot at ground level.
Overall bounds including mirrors and extended rear step: 3.65 x 3.41 x 9.38 metres.
150 renderers, 217106 triangles; static details combined by assembly/material.
Separate wheel assembly pivots. Dedicated persisted meshes and materials.
Emergency Light components disabled; lens materials have no active emission.
Standalone prefab has no siren AudioSource, vehicle controller, colliders, interiors, or LODs.
This is a detailed prototype visual asset; optimize LODs before populating a fleet.
Installed as Engine43Visual under the existing Engine07 gameplay root in HoustonResponse. Driving, station spawn, camera, map, siren, and water behavior remain on that root. PreviousEngineVisuals is disabled for rollback. 76 individual warning lenses are connected to the L-key controller.

References: user-supplied engine-01 photograph and Engine43 detail screenshots
in Sample-Asset-Images. Geometry and materials are authored approximations;
no source game model was extracted. Branding uses simplified HFD markings.

Validation: prefab reloaded; persistent geometry, supported shaders, no missing
scripts and disabled emergency lights verified. Front, rear and side rendered
in Documentation/Images/Engine43*.png. Gameplay checks passed for spawn, driving, orbit, suppression, lights, siren-off startup and reset. No updated standalone player build was made.

Authoring sources: AgentScripts/BuildEngine43.cs and PreviewEngine43.cs.
Build updates generated assets in place and preserves their GUIDs. Back up before rebuilding.

Refined from November 21 close-ups: closed peaked hosebed cover, suction hose,
roof 43, twin-row lightbar optics, side and rear marker lenses, smoother fender
arches, dark lug nuts, hub recesses, rear dual tires, and corrected parallel striping.

