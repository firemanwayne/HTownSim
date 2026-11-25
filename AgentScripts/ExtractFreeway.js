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

// The run north of Midtown: US 59 from where the Midtown-edge deck stops, to the interchange with I-45, then I-45 (both ways) and
// US 59 north, each cut where it leaves the map window. Written to I45.txt for BuildRealMidtown.AddI45Freeway. Ramps are left out.
const OUT_I45 = path.join(__dirname, "..", "Assets", "HTownSim", "Data", "I45.txt");
const NORTH_OF = 640;
const W = map.frame.window;

// The part of the segment a-b inside the window (Liang-Barsky), or null.
function clip(a, b) {
  let t0 = 0, t1 = 1;
  const d = [b[0] - a[0], b[1] - a[1]];
  const edges = [[-d[0], a[0] - W.x0], [d[0], W.x1 - a[0]], [-d[1], a[1] - W.z0], [d[1], W.z1 - a[1]]];
  for (const [p, q] of edges) {
    if (p === 0) { if (q < 0) return null; continue; }
    const r = q / p;
    if (p < 0) { if (r > t1) return null; if (r > t0) t0 = r; } else { if (r < t0) return null; if (r < t1) t1 = r; }
  }
  return [[a[0] + d[0] * t0, a[1] + d[1] * t0], [a[0] + d[0] * t1, a[1] + d[1] * t1]];
}

const north = [];
for (const road of map.roads) {
  const freeway = (road.k === "FREEWAY" && /US 59/i.test(road.n)) || (road.k === "HIGHWAY" && /IH 45/i.test(road.n));
  if (!freeway) continue;
  const us59South = /^S US 59$/i.test(road.n);
  const pts = [];
  for (let i = 0; i + 1 < road.p.length; i++) {
    const s = clip(road.p[i], road.p[i + 1]);
    if (!s) continue;
    // US 59 south of the interchange is already built up to NORTH_OF; keep only its run beyond that.
    if (us59South && s[0][1] < NORTH_OF && s[1][1] < NORTH_OF) continue;
    pts.push(s);
  }
  if (!pts.length) continue;
  // Rejoin consecutive pieces into one polyline per road.
  let current = [pts[0][0], pts[0][1]];
  const flush = () => north.push(["F", road.n, current.map((q) => q[0].toFixed(1) + "," + q[1].toFixed(1)).join(";")].join(String.fromCharCode(9)));
  for (let i = 1; i < pts.length; i++) {
    const last = current[current.length - 1];
    if (Math.hypot(last[0] - pts[i][0][0], last[1] - pts[i][0][1]) < 0.5) current.push(pts[i][1]);
    else { flush(); current = [pts[i][0], pts[i][1]]; }
  }
  flush();
}
fs.writeFileSync(OUT_I45, north.join(String.fromCharCode(10)) + String.fromCharCode(10));
console.log("Wrote " + OUT_I45 + ": " + north.length + " carriageway lines");
