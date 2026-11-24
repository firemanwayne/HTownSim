// Writes the local and major street centre lines south of the gap (any point at z < -2410) into Assets/HTownSim/Data/SouthRoads.txt,
// from Blender/HTown/data/htown_map.json. One line per road: R <TAB> class <TAB> x,z;x,z;...  Used by BuildRealMidtown.AddSouthRoads.
const fs = require("fs");
const path = require("path");

const SOURCE = path.join(__dirname, "..", "Blender", "HTown", "data", "htown_map.json");
const OUT = path.join(__dirname, "..", "Assets", "HTownSim", "Data", "SouthRoads.txt");
const map = JSON.parse(fs.readFileSync(SOURCE, "utf8"));
const lines = [];
for (const road of map.roads) {
  if ((road.k !== "LOCAL" && road.k !== "MAJOR") || !road.p.some((q) => q[1] < -2410)) continue;
  lines.push(["R", road.k, road.p.map((q) => q[0].toFixed(1) + "," + q[1].toFixed(1)).join(";")].join(String.fromCharCode(9)));
}
fs.writeFileSync(OUT, lines.join(String.fromCharCode(10)) + String.fromCharCode(10));
console.log("Wrote " + OUT + ": " + lines.length + " roads");
