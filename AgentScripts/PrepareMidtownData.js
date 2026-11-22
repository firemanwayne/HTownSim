// Downloads real City of Houston GIS data around Fire Station 7 and writes a compact, scene-ready JSON file.
//
//   node AgentScripts/PrepareMidtownData.js [outputPath]
//
// Source: City of Houston public ArcGIS server (HoustonMap: Cadastral, Transportation, Public_Safety).
// Output frame: metres, station at (0, 0); +Z points along the Midtown NE-SW street direction (north-east),
// +X along the NW-SE streets (south-east). The rotation is estimated from the parcel edges.
const fs = require("fs");
const path = require("path");

const BASE = "https://mycity2.houstontx.gov/gisweb02/rest/services/HoustonMap";
const OUT = process.argv[2] || path.join(__dirname, "..", "Assets", "HTownSim", "Data", "MidtownReal.txt");
const CACHE = process.env.MIDTOWN_CACHE || path.join(require("os").tmpdir(), "midtown_cache");
fs.mkdirSync(CACHE, { recursive: true });

const LAT0 = 29.73948387;
const LON0 = -95.3734589;
const HALF_LAT = 0.0075;
const HALF_LON = 0.0088;
const ENV = `${LON0 - HALF_LON},${LAT0 - HALF_LAT},${LON0 + HALF_LON},${LAT0 + HALF_LAT}`;
const M_PER_DEG_LAT = 110574;
const M_PER_DEG_LON = 111320 * Math.cos((LAT0 * Math.PI) / 180);

async function getJson(url) {
  const r = await fetch(url);
  const t = await r.text();
  try {
    return JSON.parse(t);
  } catch {
    throw new Error("non-JSON (" + r.status + ") from " + url.slice(0, 120) + ": " + t.slice(0, 120));
  }
}

const LIDAR = "https://services1.arcgis.com/euMKmvUChvyJxWq2/arcgis/rest/services/LiDAR_Building_Footprints_2018_gdb/FeatureServer/0";

async function queryAll(service, layer, outFields, layerUrl) {
  const tag = require("crypto").createHash("md5").update(outFields + (layerUrl || "")).digest("hex").slice(0, 6);
  const cacheFile = path.join(CACHE, `${service}_${layer}_${tag}.json`);
  if (fs.existsSync(cacheFile)) return JSON.parse(fs.readFileSync(cacheFile, "utf8"));
  const features = [];
  for (let offset = 0; ; offset += 1000) {
    const url =
      `${layerUrl || `${BASE}/${service}/MapServer/${layer}`}/query?where=1%3D1&geometry=${encodeURIComponent(ENV)}` +
      `&geometryType=esriGeometryEnvelope&inSR=4326&spatialRel=esriSpatialRelIntersects&outSR=4326` +
      `&outFields=${encodeURIComponent(outFields)}&returnGeometry=true&resultOffset=${offset}&resultRecordCount=1000&f=json`;
    const j = await getJson(url);
    if (j.error) throw new Error(JSON.stringify(j.error));
    features.push(...(j.features || []));
    process.stderr.write(`  ${service}/${layer}: ${features.length}\n`);
    if (!j.exceededTransferLimit) break;
  }
  fs.writeFileSync(cacheFile, JSON.stringify(features));
  return features;
}

const toLocal = (lon, lat) => [(lon - LON0) * M_PER_DEG_LON, (lat - LAT0) * M_PER_DEG_LAT];

function polygonArea(p) {
  let a = 0;
  for (let i = 0; i < p.length; i++) {
    const [x1, z1] = p[i];
    const [x2, z2] = p[(i + 1) % p.length];
    a += x1 * z2 - x2 * z1;
  }
  return a / 2;
}

// Tab-separated text, one record per line (Unity JsonUtility cannot read arrays of custom classes from editor scripts):
//   R <rotation>          S <x> <z> <address>        G <ns|ew> <centre> <width> <name>
//   P <cx> <cz> <minx> <minz> <maxx> <maxz> <area> <floors> <class> <year> <units> <street> <number> <x,z;x,z;...>
//   L <x,z;x,z;...>       (rail path)
function toText(o) {
  const TAB = String.fromCharCode(9);
  const NL = String.fromCharCode(10);
  const poly = (flat) => flat.reduce((a, v, i) => (i % 2 ? (a[a.length - 1] += "," + v) : a.push(String(v)), a), []).join(";");
  const lines = [["R", o.rotationDegrees].join(TAB), ["S", o.station.x, o.station.z, o.station.address].join(TAB)];
  for (const g of o.grid.ns) lines.push(["G", "ns", g.c, g.w, g.name].join(TAB));
  for (const g of o.grid.ew) lines.push(["G", "ew", g.c, g.w, g.name].join(TAB));
  for (const p of o.parcels) lines.push(["P", p.c[0], p.c[1], p.b[0], p.b[1], p.b[2], p.b[3], p.a, p.f, p.k || "?", p.y, p.u, p.s, p.n, poly(p.p), p.m, p.fb.join(","), p.ba].join(TAB));
  for (const r of o.rail) lines.push(["L", poly(r.p)].join(TAB));
  return lines.join(NL) + NL;
}

function pointInPolygon(pts, x, z) {
  let c = false;
  const n = pts.length / 2;
  for (let i = 0, j = n - 1; i < n; j = i++) {
    const xi = pts[2 * i], zi = pts[2 * i + 1], xj = pts[2 * j], zj = pts[2 * j + 1];
    if (zi > z !== zj > z && x < ((xj - xi) * (z - zi)) / (zj - zi) + xi) c = !c;
  }
  return c;
}

// Finds the street grid from the gaps between parcels. N-S streets are gaps along X; E-W streets are gaps along Z.
function detectGrid(parcels) {
  const cell = 40;
  const cells = new Map();
  parcels.forEach((p, i) => {
    for (let gx = Math.floor(p.b[0] / cell); gx <= Math.floor(p.b[2] / cell); gx++)
      for (let gz = Math.floor(p.b[1] / cell); gz <= Math.floor(p.b[3] / cell); gz++) {
        const k = gx + "," + gz;
        if (!cells.has(k)) cells.set(k, []);
        cells.get(k).push(i);
      }
  });
  const occupied = (x, z) => (cells.get(Math.floor(x / cell) + "," + Math.floor(z / cell)) || []).some((i) => pointInPolygon(parcels[i].p, x, z));
  const gaps = (fixed, alongZ, from, to, minW) => {
    const out = [];
    let start = null;
    for (let t = from; t <= to; t += 0.5) {
      const occ = alongZ ? occupied(fixed, t) : occupied(t, fixed);
      if (!occ && start === null) start = t;
      if ((occ || t + 0.5 > to) && start !== null) {
        if (t - start >= minW && t - start <= 40) out.push({ c: (start + t) / 2, w: t - start });
        start = null;
      }
    }
    return out;
  };
  const cluster = (hits, tol) => {
    const groups = [];
    for (const h of hits.sort((a, b) => a.c - b.c)) {
      const g = groups.find((q) => Math.abs(q.c - h.c) < tol);
      if (g) {
        g.cs.push(h.c);
        g.ws.push(h.w);
        g.c = g.cs.reduce((s, v) => s + v, 0) / g.cs.length;
      } else groups.push({ c: h.c, cs: [h.c], ws: [h.w] });
    }
    return groups.filter((g) => g.cs.length >= 3);
  };
  const ns = cluster([-500, -400, -300, -200, -150, -30, -20, 25, 35, 100, 110, 200, 215, 300, 350, 420, 500, 580].flatMap((z) => gaps(z, false, -650, 650, 9)), 25);
  const ew = cluster([-550, -480, -400, -300, -150, -60, 5, 50, 140, 250, 320, 420, 500, 580].flatMap((x) => gaps(x, true, -650, 650, 9)), 25);
  const nameFor = (axisIsX, c) => {
    const counts = {};
    for (const p of parcels) {
      const off = Math.abs((axisIsX ? p.c[0] : p.c[1]) - c);
      if (off < 24 && p.s) counts[p.s] = (counts[p.s] || 0) + 1;
    }
    return Object.entries(counts).sort((a, b) => b[1] - a[1])[0]?.[0] || "";
  };
  const med = (a) => [...a].sort((x, y) => x - y)[Math.floor(a.length / 2)];
  return {
    ns: ns.map((g) => ({ c: Math.round(g.c * 10) / 10, w: Math.round(med(g.ws)), name: nameFor(true, g.c) })),
    ew: ew.map((g) => ({ c: Math.round(g.c * 10) / 10, w: Math.round(med(g.ws)), name: nameFor(false, g.c) })),
  };
}

(async () => {
  console.error("Downloading parcels, thoroughfares, rail...");
  const parcelsRaw = await queryAll("Cadastral", 0, "TAX_ID,STR_NUM,STR_NAME,STR_SFX,STATE_CLASS,YR_IMPR,ACREAGE,IMPROVEMENT_VALUE,LAND_VALUE,TOTAL_BUILDING_AREA,STACKED");
  const lidarRaw = await queryAll("LiDAR", 0, "Parcel_ID", LIDAR);
  const mtfpRaw = await queryAll("Transportation", 3, "NAME,FULL_NAME,ST_TYPE,HIER_TABLE,ROW_STATUS");
  const railRaw = await queryAll("Transportation", 13, "Status");
  const stationRaw = await getJson(`${BASE}/Public_Safety/MapServer/10/query?where=LABEL%3D7&outFields=LABEL,ADDRESS,LADDERS&outSR=4326&f=json`);

  // Estimate the street rotation from long parcel edges (circular mean of bearing * 4).
  let sx = 0;
  let sz = 0;
  for (const f of parcelsRaw) {
    const ring = f.geometry?.rings?.[0];
    if (!ring) continue;
    for (let i = 0; i < ring.length - 1; i++) {
      const [x1, y1] = toLocal(ring[i][0], ring[i][1]);
      const [x2, y2] = toLocal(ring[i + 1][0], ring[i + 1][1]);
      const len = Math.hypot(x2 - x1, y2 - y1);
      if (len < 10) continue;
      const bearing = Math.atan2(x2 - x1, y2 - y1); // from north, clockwise
      sx += len * Math.cos(4 * bearing);
      sz += len * Math.sin(4 * bearing);
    }
  }
  let theta = (Math.atan2(sz, sx) / 4 + Math.PI * 2) % (Math.PI / 2); // 0..90 degrees
  if (theta > Math.PI / 4) theta -= Math.PI / 2; // keep within -45..45 around north
  const thetaDeg = (theta * 180) / Math.PI;
  // The NE-SW streets run at roughly +38 degrees; if we landed on the NW-SE family pick the NE one.
  // Gap scans across the parcel grid show the N-S streets drift by +1.28 deg and the E-W streets by -0.5 deg in the
  // raw estimate (the real grid is slightly non-orthogonal); a +0.89 deg trim splits the difference.
  const ROTATION_TRIM = 0.89;
  const rot = (thetaDeg < 0 ? thetaDeg + 90 : thetaDeg) + ROTATION_TRIM;
  const t = (rot * Math.PI) / 180;
  console.error("Street rotation (bearing of the NE-SW streets): " + rot.toFixed(2) + " degrees");

  // Scene frame: z along bearing t, x along bearing t + 90 degrees.
  const project = (lon, lat) => {
    const [e, n] = toLocal(lon, lat);
    return [e * Math.cos(t) - n * Math.sin(t), e * Math.sin(t) + n * Math.cos(t)];
  };
  const r1 = (v) => Math.round(v * 10) / 10;
  const flat = (pts) => pts.flatMap(([x, z]) => [r1(x), r1(z)]);

  // Parcels -> buildings (condo units sharing a footprint are merged).
  const merged = new Map();
  for (const f of parcelsRaw) {
    const ring = f.geometry?.rings?.[0];
    if (!ring || ring.length < 4) continue;
    const pts = ring.slice(0, -1).map(([lon, lat]) => project(lon, lat));
    const area = Math.abs(polygonArea(pts));
    if (area < 20) continue;
    const cx = pts.reduce((s, p) => s + p[0], 0) / pts.length;
    const cz = pts.reduce((s, p) => s + p[1], 0) / pts.length;
    const key = Math.round(cx / 2) + ":" + Math.round(cz / 2);
    const a = f.attributes;
    const existing = merged.get(key);
    if (existing) {
      existing.improvement += a.IMPROVEMENT_VALUE || 0;
      existing.buildingSqFt += a.TOTAL_BUILDING_AREA || 0;
      existing.ids.push(String(a.TAX_ID || "").trim());
      existing.units += 1;
      continue;
    }
    merged.set(key, {
      pts,
      area,
      cx,
      cz,
      improvement: a.IMPROVEMENT_VALUE || 0,
      buildingSqFt: a.TOTAL_BUILDING_AREA || 0,
      ids: [String(a.TAX_ID || "").trim()],
      land: a.LAND_VALUE || 0,
      units: 1,
      cls: (a.STATE_CLASS || "").trim(),
      year: parseInt(a.YR_IMPR, 10) || 0,
      street: [(a.STR_NAME || "").trim(), (a.STR_SFX || "").trim()].filter(Boolean).join(" "),
      num: (a.STR_NUM || "").trim(),
    });
  }

  // Real building footprints (LiDAR 2018), projected into the scene frame.
  const footprints = [];
  for (const f of lidarRaw) {
    const ring = f.geometry?.rings?.[0];
    if (!ring || ring.length < 4) continue;
    const pts = ring.slice(0, -1).map(([lon, lat]) => project(lon, lat));
    const area = Math.abs(polygonArea(pts));
    if (area < 12) continue;
    footprints.push({
      pts,
      area,
      id: String(f.attributes.Parcel_ID || "").trim(),
      cx: pts.reduce((sum, q) => sum + q[0], 0) / pts.length,
      cz: pts.reduce((sum, q) => sum + q[1], 0) / pts.length,
    });
  }
  const byId = new Map();
  for (const fp of footprints) {
    if (!byId.has(fp.id)) byId.set(fp.id, []);
    byId.get(fp.id).push(fp);
  }
  const SQFT_TO_M2 = 0.092903;
  const parcelsList = [...merged.values()];
  let measured = 0;
  let rejected = 0;
  for (const p of parcelsList) {
    // Parcel IDs can repeat across the 2018 footprint layer, so only accept an ID match that actually lies on or next to the parcel.
    const pxs = p.pts.map((q) => q[0]);
    const pzs = p.pts.map((q) => q[1]);
    const bx0 = Math.min(...pxs) - 15;
    const bx1 = Math.max(...pxs) + 15;
    const bz0 = Math.min(...pzs) - 15;
    const bz1 = Math.max(...pzs) + 15;
    let fps = [];
    for (const id of p.ids) fps.push(...(byId.get(id) || []));
    fps = fps.filter((fp) => fp.cx >= bx0 && fp.cx <= bx1 && fp.cz >= bz0 && fp.cz <= bz1);
    if (fps.length === 0) {
      const flat = p.pts.flatMap(([x, z]) => [x, z]);
      fps = footprints.filter((fp) => Math.abs(fp.cx - p.cx) < 60 && Math.abs(fp.cz - p.cz) < 60 && pointInPolygon(flat, fp.cx, fp.cz));
    }
    p.fp = null;
    p.fps = [];
    p.measuredFloors = 0;
    if (fps.length === 0) continue;
    const xs = fps.flatMap((fp) => fp.pts.map((q) => q[0]));
    const zs = fps.flatMap((fp) => fp.pts.map((q) => q[1]));
    const footprintArea = fps.reduce((sum, fp) => sum + fp.area, 0);
    p.fp = [Math.min(...xs), Math.min(...zs), Math.max(...xs), Math.max(...zs)];
    p.fps = [...fps].sort((a, b) => b.area - a.area).slice(0, 12).map((fp) => {
      const fx = fp.pts.map((q) => q[0]);
      const fz = fp.pts.map((q) => q[1]);
      return [Math.min(...fx), Math.min(...fz), Math.max(...fx), Math.max(...fz)];
    });
    p.footprintArea = footprintArea;
    if (p.buildingSqFt > 0 && footprintArea >= 20) {
      let floors = Math.max(1, Math.min(70, Math.round((p.buildingSqFt * SQFT_TO_M2) / footprintArea)));
      // Plausibility guards: a tall result needs a real-sized footprint (a shed footprint paired with a whole
      // building's floor area would otherwise read as a tower), and footprints cannot exceed the lot.
      const tooSmallForTall = floors > 8 && footprintArea < 150;
      const exceedsLot = footprintArea > p.area * 1.25;
      if (tooSmallForTall || exceedsLot) {
        p.measuredFloors = 0;
        rejected++;
      } else {
        p.measuredFloors = floors;
        measured++;
      }
    }
  }
  console.error(`Footprints: ${footprints.length}; parcels with a footprint: ${parcelsList.filter((p) => p.fp).length}; with measured floors: ${measured} of ${parcelsList.length} (rejected as implausible: ${rejected})`);

  const parcels = [];
  for (const p of parcelsList) {
    const perM2 = p.improvement / p.area; // improvement dollars per square metre of lot
    // Floors from improvement value density: ~one floor per $700/m2, capped at 55.
    const floors = p.improvement <= 0 ? 0 : Math.max(1, Math.min(55, Math.round(perM2 / 700)));
    const xs = p.pts.map((q) => q[0]);
    const zs = p.pts.map((q) => q[1]);
    parcels.push({
      c: [r1(p.cx), r1(p.cz)],
      b: [r1(Math.min(...xs)), r1(Math.min(...zs)), r1(Math.max(...xs)), r1(Math.max(...zs))],
      p: flat(p.pts),
      a: Math.round(p.area),
      f: floors,
      m: p.measuredFloors || 0,
      fb: (p.fps || []).flatMap((box) => box.map(r1)),
      ba: Math.round(p.buildingSqFt * SQFT_TO_M2),
      k: p.cls,
      y: p.year,
      u: p.units,
      s: p.street,
      n: p.num,
    });
  }

  const streets = mtfpRaw
    .filter((f) => f.geometry?.paths)
    .map((f) => ({
      name: (f.attributes.FULL_NAME || f.attributes.NAME || "").trim(),
      type: (f.attributes.ST_TYPE || "").trim(),
      hier: (f.attributes.HIER_TABLE || "").trim(),
      paths: f.geometry.paths.map((path) => ({ p: flat(path.map(([lon, lat]) => project(lon, lat))) })),
    }));

  const rail = railRaw
    .filter((f) => f.geometry?.paths)
    .flatMap((f) => f.geometry.paths.map((path) => ({ p: flat(path.map(([lon, lat]) => project(lon, lat))) })));

  const grid = detectGrid(parcels);
  const st = stationRaw.features?.[0];
  const [sx0, sz0] = project(st.geometry.x, st.geometry.y);
  const out = {
    source: "City of Houston GIS (mycity2.houstontx.gov HoustonMap): Cadastral, Transportation, Public_Safety",
    rotationDegrees: Math.round(rot * 100) / 100,
    origin: { lat: LAT0, lon: LON0 },
    station: { x: r1(sx0), z: r1(sz0), address: st.attributes.ADDRESS, ladders: st.attributes.LADDERS },
    grid,
    parcels,
    streets,
    rail,
  };
  fs.mkdirSync(path.dirname(OUT), { recursive: true });
  fs.writeFileSync(OUT, toText(out));
  const kb = Math.round(fs.statSync(OUT).size / 1024);
  console.error(`Wrote ${OUT} (${kb} KB): ${parcels.length} buildings (from ${parcelsRaw.length} parcels), ${streets.length} thoroughfare segments, ${rail.length} rail path(s)`);
  console.error("Grid N-S: " + out.grid.ns.map((g) => g.name + "@x=" + g.c + " w" + g.w).join(", "));
  console.error("Grid E-W: " + out.grid.ew.map((g) => g.name + "@z=" + g.c + " w" + g.w).join(", "));
  const types = {};
  for (const s of streets) types[s.type || "?"] = (types[s.type || "?"] || 0) + 1;
  console.error("Thoroughfare types: " + JSON.stringify(types));
  const classes = {};
  for (const p of parcels) classes[p.k || "?"] = (classes[p.k || "?"] || 0) + 1;
  console.error("Parcel classes: " + JSON.stringify(classes));
  const both = parcels.filter((p) => p.m > 0);
  const diff = both.map((p) => p.m - p.f);
  console.error(`Measured floors: median ${both.map((p) => p.m).sort((a, b) => a - b)[Math.floor(both.length / 2)]}, max ${Math.max(...both.map((p) => p.m))}; mean (measured - value-estimate) = ${(diff.reduce((a, b) => a + b, 0) / Math.max(1, diff.length)).toFixed(2)} floors over ${both.length} parcels`);
  console.error("Tallest measured: " + [...both].sort((a, b) => b.m - a.m).slice(0, 8).map((p) => `${p.n} ${p.s} ${p.m}fl (${p.ba}m2)`).join(" | "));
  const fl = parcels.map((p) => p.f).sort((a, b) => a - b);
  console.error(`Floors: median ${fl[Math.floor(fl.length / 2)]}, p90 ${fl[Math.floor(fl.length * 0.9)]}, max ${fl[fl.length - 1]}`);
})().catch((e) => {
  console.error("ERROR", e.message);
  process.exit(1);
});
