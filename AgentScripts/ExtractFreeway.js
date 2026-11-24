// Writes the US-59 freeway carriageways south of Midtown into Assets/HTownSim/Data/US59.txt, from the road centre lines in
// Blender/HTown/data/htown_map.json (City of Houston road layer, already in the scene frame, metres).
//
//   node AgentScripts/ExtractFreeway.js
//
// One line per carriageway segment: F <TAB> name <TAB> x,z;x,z;...
// The part along Midtown's east edge is already the decorative deck built by BuildRealMidtown.BuildFreeway, so only segments
// that reach z < -330 (the south-western run) are kept. Used by BuildRealMidtown.AddUs59Freeway.
const fs = require("fs");
const path = require("path");

const SOURCE = path.join(__dirname, "..", "Blender", "HTown", "data", "htown_map.json");
const OUT = path.join(__dirname, "..", "Assets", "HTownSim", "Data", "US59.txt");
const SOUTH_OF = -330;

const map = JSON.parse(fs.readFileSync(SOURCE, "utf8"));
const lines = [];
for (const road of map.roads) {
  if (road.k !== "FREEWAY" || !/US 59/i.test(road.n)) continue;
  const pts = road.p;
  if (pts.length < 2) continue;
  // Points are [x, z] pairs; keep the segment when its far end is south of the cut.
  if (!pts.some((q) => q[1] < SOUTH_OF)) continue;
  lines.push(["F", road.n, pts.map((q) => q[0].toFixed(1) + "," + q[1].toFixed(1)).join(";")].join(String.fromCharCode(9)));
}
fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, lines.join(String.fromCharCode(10)) + String.fromCharCode(10));
console.log("Wrote " + OUT + ": " + lines.length + " carriageway lines");
