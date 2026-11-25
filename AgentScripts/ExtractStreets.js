// Writes the City's local and major street centre lines (scene frame, metres) to Assets/HTownSim/Data/CityStreets.txt, from
// Blender/HTown/data/htown_map.json. Used by the gap kit's street painter (HTownSim > Gap > Paint Gap Streets).
//
//   node AgentScripts/ExtractStreets.js
//
// One line per street piece: R <TAB> class <TAB> name <TAB> x,z;x,z;...   (class is MAJOR or LOCAL)
const fs = require("fs");
const path = require("path");

const SOURCE = path.join(__dirname, "..", "Blender", "HTown", "data", "htown_map.json");
const OUT = path.join(__dirname, "..", "Assets", "HTownSim", "Data", "CityStreets.txt");
const TAB = String.fromCharCode(9);
const LF = String.fromCharCode(10);

const map = JSON.parse(fs.readFileSync(SOURCE, "utf8"));
const lines = [];
for (const road of map.roads) {
  if ((road.k !== "MAJOR" && road.k !== "LOCAL") || road.p.length < 2) continue;
  lines.push(["R", road.k, road.n, road.p.map((q) => q[0].toFixed(1) + "," + q[1].toFixed(1)).join(";")].join(TAB));
}
fs.writeFileSync(OUT, lines.join(LF) + LF);
console.log("Wrote " + OUT + ": " + lines.length + " street pieces");
