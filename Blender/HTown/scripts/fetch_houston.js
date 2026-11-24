// Builds data/htown_map.json for the Blender map scene.
//
//   node scripts/fetch_houston.js
//
// Reuses the GIS caches written by HTownSim's PrepareMidtownData.js (parcels, 2018 LiDAR footprints, street
// hierarchy, METRORail) and adds City of Houston layers found through the COH ArcGIS catalog: road centrelines
// (class + one-way), park boundaries, HFD fire stations and fire hydrants. Everything is projected into the
// HTownSim scene frame (metres, Station 7 at the origin, grid rotated 35.06 degrees) so the map lines up 1:1
// with the Unity world: scene X -> Blender X, scene Z -> Blender Y.
const fs = require("fs");
const os = require("os");
const path = require("path");

const ROOT = path.join(__dirname, "..");
const OUT = path.join(ROOT, "data", "htown_map.json");
const CACHE = path.join(ROOT, "data", "cache");
fs.mkdirSync(CACHE, { recursive: true });

// Same constants as HTownSim/AgentScripts/PrepareMidtownData.js.
const LAT0 = 29.73948387;
const LON0 = -95.3734589;
const M_PER_DEG_LAT = 110574;
const M_PER_DEG_LON = 111320 * Math.cos((LAT0 * Math.PI) / 180);
const ROT = (35.06 * Math.PI) / 180;
// Unity playable window plus a margin so the map has context past the walls.
const WIN = { x0: -845, x1: 560, z0: -450, z1: 1575 };
const MARGIN = 180;

const toScene = (lon, lat) => {
  const e = (lon - LON0) * M_PER_DEG_LON;
  const n = (lat - LAT0) * M_PER_DEG_LAT;
  return [+(e * Math.cos(ROT) - n * Math.sin(ROT)).toFixed(2), +(e * Math.sin(ROT) + n * Math.cos(ROT)).toFixed(2)];
};
const toLonLat = (x, z) => {
  const e = x * Math.cos(ROT) + z * Math.sin(ROT);
  const n = -x * Math.sin(ROT) + z * Math.cos(ROT);
  return [LON0 + e / M_PER_DEG_LON, LAT0 + n / M_PER_DEG_LAT];
};

const RING = (() => {
  const c = [
    toLonLat(WIN.x0 - MARGIN, WIN.z0 - MARGIN),
    toLonLat(WIN.x1 + MARGIN, WIN.z0 - MARGIN),
    toLonLat(WIN.x1 + MARGIN, WIN.z1 + MARGIN),
    toLonLat(WIN.x0 - MARGIN, WIN.z1 + MARGIN),
  ];
  return [...c, c[0]];
})();
const GEOMETRY =
  `geometry=${encodeURIComponent(JSON.stringify({ rings: [RING], spatialReference: { wkid: 4326 } }))}` +
  `&geometryType=esriGeometryPolygon&inSR=4326&spatialRel=esriSpatialRelIntersects&outSR=4326`;

const LAYERS = {
  roads: { url: "https://services.arcgis.com/NummVBqZSIJKUeVR/arcgis/rest/services/COH_RoadCenterline/FeatureServer/0", fields: "fullname,roadclass,onewaydir,lanes" },
  parks: { url: "https://services.arcgis.com/NummVBqZSIJKUeVR/arcgis/rest/services/COH_Parks_view/FeatureServer/14", fields: "Name,Park_Acres" },
  stations: { url: "https://services.arcgis.com/NummVBqZSIJKUeVR/arcgis/rest/services/COH_Houston_Fire_Stations_view/FeatureServer/10", fields: "LABEL,ADDRESS,LADDERS" },
  hydrants: { url: "https://mycity2.houstontx.gov/gisweb02/rest/services/HoustonMap/Public_Safety/MapServer/9", fields: "*" },
  // HPARD Urban Forestry inventory: species and trunk-diameter class of city-maintained trees.
  trees: { url: "https://services.arcgis.com/NummVBqZSIJKUeVR/arcgis/rest/services/COH_UrbanForestry_Trees_VIEW_ONLY/FeatureServer/0", fields: "SPECIES,DIAMETER,STATUS,LOCATION,CONDITION" },
  // HCAD land use per parcel (parking lots, vacant land, residential...), keyed by TAX_ID.
  landuse: { url: "https://mycity2.houstontx.gov/gisweb02/rest/services/HoustonMap/Landuse/MapServer/0", fields: "TAX_ID,LANDUSE_CD,LANDUSE_DSCR,GROUP_DSCR,STATE_CLASS" },
};

async function queryAll(name, { url, fields }) {
  const cacheFile = path.join(CACHE, `${name}.json`);
  if (fs.existsSync(cacheFile)) return JSON.parse(fs.readFileSync(cacheFile, "utf8"));
  const features = [];
  for (let offset = 0; ; ) {
    const q = `${url}/query?where=1%3D1&${GEOMETRY}&outFields=${encodeURIComponent(fields)}&returnGeometry=true&resultOffset=${offset}&resultRecordCount=1000&f=json`;
    const r = await fetch(q);
    const j = JSON.parse(await r.text());
    if (j.error) throw new Error(`${name}: ${JSON.stringify(j.error)}`);
    const got = j.features || [];
    features.push(...got);
    process.stderr.write(`  ${name}: ${features.length}\n`);
    if (!j.exceededTransferLimit || !got.length) break;
    offset += got.length;
  }
  fs.writeFileSync(cacheFile, JSON.stringify(features));
  return features;
}

// HTownSim caches: the original Midtown window and the expanded window overlap, so merge and dedupe.
function readUnityCache(file) {
  const out = [];
  for (const dir of ["midtown_cache", "midtown_cache_expanded"]) {
    const f = path.join(os.tmpdir(), dir, file);
    if (fs.existsSync(f)) out.push(...JSON.parse(fs.readFileSync(f, "utf8")));
  }
  if (!out.length) throw new Error(`missing HTownSim cache ${file}; run PrepareMidtownData.js first`);
  return out;
}

const inView = (pts) => pts.some(([x, z]) => x > WIN.x0 - MARGIN && x < WIN.x1 + MARGIN && z > WIN.z0 - MARGIN && z < WIN.z1 + MARGIN);
const ringToScene = (ring) => ring.slice(0, -1).map(([lon, lat]) => toScene(lon, lat));
const key = (pts) => {
  const cx = pts.reduce((s, p) => s + p[0], 0) / pts.length;
  const cz = pts.reduce((s, p) => s + p[1], 0) / pts.length;
  return `${Math.round(cx * 2)},${Math.round(cz * 2)},${pts.length}`;
};

function polygons(features, attrs) {
  const seen = new Set();
  const out = [];
  for (const f of features) {
    for (const ring of f.geometry?.rings || []) {
      const pts = ringToScene(ring);
      if (pts.length < 3 || !inView(pts)) continue;
      const k = key(pts);
      if (seen.has(k)) continue;
      seen.add(k);
      out.push({ p: pts, ...attrs(f.attributes || {}) });
    }
  }
  return out;
}

function lines(features, attrs) {
  const out = [];
  for (const f of features) {
    for (const p of f.geometry?.paths || []) {
      const pts = p.map(([lon, lat]) => toScene(lon, lat));
      if (pts.length >= 2 && inView(pts)) out.push({ p: pts, ...attrs(f.attributes || {}) });
    }
  }
  return out;
}

function points(features, attrs) {
  const out = [];
  for (const f of features) {
    const g = f.geometry;
    if (!g || g.x === undefined) continue;
    const [x, z] = toScene(g.x, g.y);
    if (inView([[x, z]])) out.push({ x, z, ...attrs(f.attributes || {}) });
  }
  return out;
}

(async () => {
  const parcelsRaw = readUnityCache("Cadastral_0_528c31.json");
  const lidarRaw = readUnityCache("LiDAR_0_acf120.json");
  const railRaw = readUnityCache("Transportation_13_ec53a8.json");

  const fetched = {};
  for (const [name, layer] of Object.entries(LAYERS)) fetched[name] = await queryAll(name, layer);

  // Gross building area per parcel, used for heights (same idea as BuildRealMidtown: floors = area / footprint).
  const areaById = new Map();
  for (const f of parcelsRaw) {
    const a = f.attributes;
    const id = String(a.TAX_ID || "").trim();
    areaById.set(id, (areaById.get(id) || 0) + (a.TOTAL_BUILDING_AREA || 0) * 0.092903);
  }

  const parcels = polygons(parcelsRaw, (a) => ({ c: a.STATE_CLASS || "", ba: Math.round((a.TOTAL_BUILDING_AREA || 0) * 0.092903) }));
  const buildings = polygons(lidarRaw, (a) => ({ id: String(a.Parcel_ID || "").trim() }));

  // Share each parcel's gross area across its footprints by footprint area to get floors.
  const fpArea = (p) => Math.abs(p.reduce((s, [x1, z1], i) => { const [x2, z2] = p[(i + 1) % p.length]; return s + x1 * z2 - x2 * z1; }, 0) / 2);
  const totalFp = new Map();
  for (const b of buildings) { b.a = fpArea(b.p); totalFp.set(b.id, (totalFp.get(b.id) || 0) + b.a); }
  for (const b of buildings) {
    const gross = areaById.get(b.id) || 0;
    const floors = gross > 0 ? (gross * (b.a / totalFp.get(b.id))) / b.a : 0;
    b.f = floors > 0 ? Math.max(1, Math.min(75, Math.round(floors))) : 0;
    b.a = Math.round(b.a);
  }

  // Prefer the floor counts HTownSim itself uses (P records in the Unity data files), so map heights match the game.
  const unityData = path.join("J:", "Unity", "projects", "HTownSim", "Assets", "HTownSim", "Data");
  const unityParcels = [];
  for (const file of ["MidtownReal.txt", "MidtownExpanded.txt"]) {
    const f = path.join(unityData, file);
    if (!fs.existsSync(f)) continue;
    for (const line of fs.readFileSync(f, "utf8").split("\n")) {
      const t = line.split("\t");
      if (t[0] !== "P") continue;
      const poly = t[14].split(";").map((s) => s.split(",").map(Number));
      unityParcels.push({ b: t.slice(3, 7).map(Number), f: +t[8], k: t[9], poly });
    }
  }
  const inside = (poly, x, z) => {
    let c = false;
    for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
      const [xi, zi] = poly[i];
      const [xj, zj] = poly[j];
      if (zi > z !== zj > z && x < ((xj - xi) * (z - zi)) / (zj - zi) + xi) c = !c;
    }
    return c;
  };
  let matched = 0;
  for (const b of buildings) {
    const cx = b.p.reduce((s, p) => s + p[0], 0) / b.p.length;
    const cz = b.p.reduce((s, p) => s + p[1], 0) / b.p.length;
    const hit = unityParcels.find((u) => cx >= u.b[0] && cx <= u.b[2] && cz >= u.b[1] && cz <= u.b[3] && inside(u.poly, cx, cz));
    if (hit && hit.f > 0) {
      b.f = hit.f;
      b.k = hit.k;
      matched++;
    }
  }
  console.log(`unity floors matched: ${matched}/${buildings.length}`);

  const out = {
    frame: { rotationDegrees: 35.06, lat0: LAT0, lon0: LON0, window: WIN, margin: MARGIN },
    stationsGame: [
      { label: 7, x: -0.9, z: 0.1, address: "1402 ELGIN ST" },
      { label: 8, x: -733.5, z: 1091, address: "1919 LOUISIANA ST" },
    ],
    parcels,
    buildings,
    roads: lines(fetched.roads, (a) => ({ n: a.fullname || "", k: a.roadclass || "", o: a.onewaydir || "" })),
    rail: lines(railRaw, () => ({})),
    parks: polygons(fetched.parks, (a) => ({ n: a.Name || "", ac: a.Park_Acres || 0 })),
    stations: points(fetched.stations, (a) => ({ label: a.LABEL, address: a.ADDRESS, ladder: a.LADDERS === "Y" })),
    hydrants: points(fetched.hydrants, () => ({})),
    trees: points(
      fetched.trees.filter((f) => (f.attributes?.STATUS || "Present") === "Present"),
      (a) => ({ sp: a.SPECIES || "", d: a.DIAMETER || "", loc: a.LOCATION || "", cond: a.CONDITION || "" }),
    ),
    landuse: polygons(fetched.landuse, (a) => ({ id: String(a.TAX_ID || "").trim(), lu: a.LANDUSE_CD || "", ld: a.LANDUSE_DSCR || "", g: a.GROUP_DSCR || "", c: a.STATE_CLASS || "" })),
  };
  fs.mkdirSync(path.dirname(OUT), { recursive: true });
  fs.writeFileSync(OUT, JSON.stringify(out));
  for (const [k, v] of Object.entries(out)) if (Array.isArray(v)) console.log(`${k}: ${v.length}`);
  console.log(`wrote ${OUT}`);
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
