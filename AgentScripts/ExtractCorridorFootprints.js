// Writes the 2018 LiDAR building footprints of the Hermann Park corridor (the gap between the city and the Medical Center) as oriented boxes
// into Assets/HTownSim/Data/Corridor_Footprints.txt. Needs the cache that
//   NEIGHBORHOOD=medcorridor MIDTOWN_CACHE=<dir> node AgentScripts/PrepareMidtownData.js
// wrote (the LiDAR_0_*.json file). Run: CORRIDOR_CACHE=<dir> node AgentScripts/ExtractCorridorFootprints.js
// One line per building: B <TAB> cx <TAB> cz <TAB> width <TAB> depth <TAB> yawDegrees <TAB> floors   (scene metres, Midtown frame).
const fs = require("fs");
const path = require("path");

const CACHE = process.env.CORRIDOR_CACHE;
if (!CACHE) throw new Error("Set CORRIDOR_CACHE to the medcorridor cache folder");
const OUT = path.join(__dirname, "..", "Assets", "HTownSim", "Data", "Corridor_Footprints.txt");
const LAT0 = 29.73948387;
const LON0 = -95.3734589;
const M_LAT = 110574;
const M_LON = 111320 * Math.cos((LAT0 * Math.PI) / 180);
const T = (35.06 * Math.PI) / 180;
const toScene = (lon, lat) => {
  const e = (lon - LON0) * M_LON;
  const n = (lat - LAT0) * M_LAT;
  return [e * Math.cos(T) - n * Math.sin(T), e * Math.sin(T) + n * Math.cos(T)];
};

// Smallest-area rectangle around a point set, searched in 1 degree steps.
function orientedBox(points) {
  let best = null;
  for (let deg = 0; deg < 90; deg++) {
    const a = (deg * Math.PI) / 180;
    const c = Math.cos(a);
    const s = Math.sin(a);
    let x0 = 1e9, x1 = -1e9, z0 = 1e9, z1 = -1e9;
    for (const [x, z] of points) {
      const u = x * c + z * s;
      const v = -x * s + z * c;
      x0 = Math.min(x0, u); x1 = Math.max(x1, u); z0 = Math.min(z0, v); z1 = Math.max(z1, v);
    }
    const area = (x1 - x0) * (z1 - z0);
    if (!best || area < best.area) best = { area, deg, x0, x1, z0, z1, c, s };
  }
  const mu = (best.x0 + best.x1) / 2;
  const mv = (best.z0 + best.z1) / 2;
  // Back to scene coordinates; the box's local X axis points along (cos, sin) in the scene, so Unity's yaw (about Y) is atan2(-sin, cos)... see BuildRealMidtown.
  return { cx: mu * best.c - mv * best.s, cz: mu * best.s + mv * best.c, w: best.x1 - best.x0, d: best.z1 - best.z0, deg: best.deg, area: best.area };
}

const file = fs.readdirSync(CACHE).find((f) => f.startsWith("LiDAR_0_"));
const features = JSON.parse(fs.readFileSync(path.join(CACHE, file), "utf8"));
const lines = [];
for (const f of features) {
  for (const ring of f.geometry.rings) {
    const pts = ring.map(([lon, lat]) => toScene(lon, lat));
    const b = orientedBox(pts);
    if (b.w < 3 || b.d < 3) continue;
    const floors = b.area < 300 ? 1 : b.area < 1500 ? 2 : b.area < 6000 ? 3 : 5;
    lines.push(["B", b.cx.toFixed(1), b.cz.toFixed(1), b.w.toFixed(1), b.d.toFixed(1), b.deg, floors].join(String.fromCharCode(9)));
  }
}
fs.writeFileSync(OUT, lines.join(String.fromCharCode(10)) + String.fromCharCode(10));
console.log("Wrote " + OUT + ": " + lines.length + " footprints");
