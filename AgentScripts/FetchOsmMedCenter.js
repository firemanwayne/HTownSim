// Downloads OpenStreetMap buildings and streets for the Medical Center / Hermann Park window and writes them in Midtown's frame:
//   Assets/HTownSim/Data/Osm_MedCenter.txt
//     B <TAB> cx <TAB> cz <TAB> width <TAB> depth <TAB> yawDegrees <TAB> floors <TAB> name      (oriented box around a building outline)
//     W <TAB> class <TAB> x,z;x,z;...                                                          (street or path centre line)
//     H <TAB> name <TAB> x <TAB> z                                                             (hospital / emergency entrance point)
// Run: node AgentScripts/FetchOsmMedCenter.js     (data (c) OpenStreetMap contributors, ODbL)
const fs = require("fs");
const path = require("path");

const OUT = path.join(__dirname, "..", "Assets", "HTownSim", "Data", "Osm_MedCenter.txt");
const WIN = { x0: -450, x1: 650, z0: -4700, z1: -2400 };
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
const toGeo = (x, z) => {
  const e = x * Math.cos(T) + z * Math.sin(T);
  const n = -x * Math.sin(T) + z * Math.cos(T);
  return [LON0 + e / M_LON, LAT0 + n / M_LAT];
};
const corners = [toGeo(WIN.x0, WIN.z0), toGeo(WIN.x1, WIN.z0), toGeo(WIN.x1, WIN.z1), toGeo(WIN.x0, WIN.z1)];
const south = Math.min(...corners.map((c) => c[1])) - 0.0005;
const north = Math.max(...corners.map((c) => c[1])) + 0.0005;
const west = Math.min(...corners.map((c) => c[0])) - 0.0005;
const east = Math.max(...corners.map((c) => c[0])) + 0.0005;
const bbox = `${south},${west},${north},${east}`;
const inWin = (x, z) => x >= WIN.x0 && x <= WIN.x1 && z >= WIN.z0 && z <= WIN.z1;

const queryFor = (box) => `[out:json][timeout:90];(way["building"](${box});way["highway"](${box});node["amenity"="hospital"](${box});way["amenity"="hospital"](${box});node["emergency"="yes"](${box}););out geom tags;`;

// The window is fetched in a 6 x 6 grid of smaller boxes (one big query times out) and merged by element id.
const tiles = [];
const N = 6;
for (let i = 0; i < N; i++) {
  for (let j = 0; j < N; j++) {
    const s0 = south + ((north - south) * i) / N;
    const s1 = south + ((north - south) * (i + 1)) / N;
    const w0 = west + ((east - west) * j) / N;
    const w1 = west + ((east - west) * (j + 1)) / N;
    tiles.push(`${s0},${w0},${s1},${w1}`);
  }
}

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
  return { cx: mu * best.c - mv * best.s, cz: mu * best.s + mv * best.c, w: best.x1 - best.x0, d: best.z1 - best.z0, deg: best.deg, area: best.area };
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function fetchTile(box) {
  const hosts = ["https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter"];
  for (let attempt = 0; attempt < 12; attempt++) {
    const host = hosts[attempt % hosts.length];
    try {
      const r = await fetch(host, { method: "POST", body: "data=" + encodeURIComponent(queryFor(box)), headers: { "Content-Type": "application/x-www-form-urlencoded", "User-Agent": "HTownSim-dev/1.0" } });
      const text = await r.text();
      return JSON.parse(text).elements;
    } catch (e) {
      console.error(host + " " + box + ": " + e.message.slice(0, 80));
      await sleep(8000);
    }
  }
  throw new Error("Overpass unavailable for " + box);
}

(async () => {
  const seen = new Set();
  const elements = [];
  for (const box of tiles) {
    for (const el of await fetchTile(box)) {
      const key = el.type + el.id;
      if (!seen.has(key)) {
        seen.add(key);
        elements.push(el);
      }
    }
    console.error("tile done " + box);
  }
  const json = { elements };
  const TAB = String.fromCharCode(9);
  const lines = [];
  let b = 0, w = 0, h = 0;
  for (const el of json.elements) {
    const tags = el.tags || {};
    if (el.type === "way" && tags.building && el.geometry) {
      const pts = el.geometry.map((g) => toScene(g.lon, g.lat));
      const box = orientedBox(pts);
      if (!inWin(box.cx, box.cz) || box.w < 3 || box.d < 3) continue;
      let floors = parseInt(tags["building:levels"], 10);
      if (!(floors > 0) && tags.height) floors = Math.max(1, Math.round(parseFloat(tags.height) / 3.8));
      if (!(floors > 0)) floors = box.area < 300 ? 1 : box.area < 1500 ? 2 : box.area < 6000 ? 3 : 5;
      lines.push(["B", box.cx.toFixed(1), box.cz.toFixed(1), box.w.toFixed(1), box.d.toFixed(1), box.deg, floors, (tags.name || tags.building || "").replace(/\s+/g, " ")].join(TAB));
      b++;
    } else if (el.type === "way" && tags.highway && el.geometry) {
      const cls = tags.highway;
      if (/^(footway|steps|corridor|pedestrian|cycleway|path|service|motorway|motorway_link|trunk|trunk_link)$/.test(cls) && !/^(service)$/.test(cls)) continue;
      const pts = el.geometry.map((g) => toScene(g.lon, g.lat));
      if (!pts.some(([x, z]) => inWin(x, z))) continue;
      lines.push(["W", cls, pts.map((p) => p[0].toFixed(1) + "," + p[1].toFixed(1)).join(";")].join(TAB));
      w++;
    } else if (tags.amenity === "hospital" || tags.emergency === "yes") {
      const lat = el.lat !== undefined ? el.lat : el.geometry ? el.geometry[0].lat : null;
      const lon = el.lon !== undefined ? el.lon : el.geometry ? el.geometry[0].lon : null;
      if (lat === null) continue;
      const [x, z] = toScene(lon, lat);
      if (!inWin(x, z)) continue;
      lines.push(["H", (tags.name || "hospital").replace(/\s+/g, " "), x.toFixed(1), z.toFixed(1)].join(TAB));
      h++;
    }
  }
  fs.writeFileSync(OUT, lines.join(String.fromCharCode(10)) + String.fromCharCode(10));
  console.log(`Wrote ${OUT}: ${b} buildings, ${w} streets, ${h} hospital points (bbox ${bbox})`);
})();
