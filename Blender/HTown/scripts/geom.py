"""Shared 2D/3D geometry helpers for the HTown Blender passes (ground, trees).

Coordinates: Blender (x, y, z-up) with Blender x,y = Unity x,z. write_unity_text converts to Unity (x, y-up, z) and
reverses triangle winding (swapping two axes mirrors space), in the tab-separated format UpgradeBuildings and
UpgradeGround read:   B <key> <name>   then   M <material> <x,y,z,...> <u,v,...>
"""
import math

import bmesh
from mathutils import Vector


def area(poly):
    return sum(poly[i][0] * poly[(i + 1) % len(poly)][1] - poly[(i + 1) % len(poly)][0] * poly[i][1] for i in range(len(poly))) / 2


def ccw(poly, tol=0.15):
    """Drops repeated and near-collinear points and returns the ring counter-clockwise."""
    out = []
    for p in poly:
        p = (float(p[0]), float(p[1]))
        if not out or math.dist(out[-1], p) > tol:
            out.append(p)
    if len(out) > 2 and math.dist(out[0], out[-1]) < tol:
        out.pop()
    changed = True
    while changed and len(out) > 3:
        changed = False
        for i in range(len(out)):
            a, b, c = out[i - 1], out[i], out[(i + 1) % len(out)]
            cross = (b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0])
            if abs(cross) < 0.02 * math.dist(a, b) * math.dist(b, c) + 1e-6:
                out.pop(i)
                changed = True
                break
    return out if area(out) > 0 else out[::-1]


def point_in(poly, x, y):
    c = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            c = not c
        j = i
    return c


def dist_to_poly_edges(poly, x, y):
    best = float("inf")
    for (x0, y0), (x1, y1) in zip(poly, poly[1:] + poly[:1]):
        dx, dy = x1 - x0, y1 - y0
        t = max(0.0, min(1.0, ((x - x0) * dx + (y - y0) * dy) / (dx * dx + dy * dy or 1)))
        best = min(best, math.hypot(x0 + t * dx - x, y0 + t * dy - y))
    return best


def offset_poly(poly, d):
    """Miter offset (positive = outward for a CCW ring), miters clamped so sharp corners do not spike."""
    n = len(poly)
    out = []
    for i in range(n):
        p0, p1, p2 = Vector(poly[i - 1]), Vector(poly[i]), Vector(poly[(i + 1) % n])
        e0 = (p1 - p0).normalized()
        e1 = (p2 - p1).normalized()
        n0 = Vector((e0.y, -e0.x))
        n1 = Vector((e1.y, -e1.x))
        m = n0 + n1
        if m.length < 1e-4:
            m = n0
        m.normalize()
        k = d / max(0.35, m.dot(n1))
        q = p1 + m * k
        out.append((q.x, q.y))
    return out


def edge_normal(p, q):
    """Outward normal of edge p->q of a CCW ring."""
    length = math.dist(p, q) or 1
    return ((q[1] - p[1]) / length, -(q[0] - p[0]) / length)


def centroid(poly):
    return (sum(p[0] for p in poly) / len(poly), sum(p[1] for p in poly) / len(poly))


class Grid:
    """Uniform grid over polygons' bounding boxes for fast 'which polygons are near this point'."""

    def __init__(self, polys, cell=40.0):
        self.cell = cell
        self.cells = {}
        for i, poly in enumerate(polys):
            xs, ys = [p[0] for p in poly], [p[1] for p in poly]
            for gx in range(int(min(xs) // cell), int(max(xs) // cell) + 1):
                for gy in range(int(min(ys) // cell), int(max(ys) // cell) + 1):
                    self.cells.setdefault((gx, gy), []).append(i)

    def near(self, x, y):
        return self.cells.get((int(x // self.cell), int(y // self.cell)), [])


class Parts:
    """Triangles per material in Blender space, with UVs (metres / uv_scale on flat faces)."""

    def __init__(self):
        self.mats = {}

    def tri_list(self, mat):
        return self.mats.setdefault(mat, {"v": [], "uv": []})

    def quad(self, mat, a, b, c, d, uvs=None):
        t = self.tri_list(mat)
        uvs = uvs or [(0, 0)] * 4
        for i in (0, 1, 2, 0, 2, 3):
            t["v"].append((a, b, c, d)[i])
            t["uv"].append(uvs[i])

    def polygon(self, mat, poly, z, uv_scale=4.0):
        """Upward-facing flat polygon, concave-safe (bmesh triangulation)."""
        if len(poly) < 3:
            return
        bm = bmesh.new()
        verts = [bm.verts.new((x, y, z)) for x, y in poly]
        try:
            face = bm.faces.new(verts)
        except ValueError:
            bm.free()
            return
        bmesh.ops.triangulate(bm, faces=[face])
        t = self.tri_list(mat)
        for f in bm.faces:
            vs = [tuple(v.co) for v in f.verts]
            # Keep every triangle facing up.
            (x1, y1, _), (x2, y2, _), (x3, y3, _) = vs
            if (x2 - x1) * (y3 - y1) - (y2 - y1) * (x3 - x1) < 0:
                vs = vs[::-1]
            for v in vs:
                t["v"].append(v)
                t["uv"].append((v[0] / uv_scale, v[1] / uv_scale))
        bm.free()

    def walls(self, mat, poly, z0, z1, closed=True):
        """Outward-facing vertical quads along a ring (or an open polyline when closed=False)."""
        pairs = list(zip(poly, poly[1:] + poly[:1])) if closed else list(zip(poly, poly[1:]))
        for p, q in pairs:
            length = math.dist(p, q)
            if length < 0.05:
                continue
            uvs = [(0, z0), (length, z0), (length, z1), (0, z1)]
            self.quad(mat, (p[0], p[1], z0), (q[0], q[1], z0), (q[0], q[1], z1), (p[0], p[1], z1), uvs)

    def panel(self, mat, p, q, z0, z1):
        """Two-sided vertical panel from p to q (fences)."""
        length = math.dist(p, q)
        uvs = [(0, z0), (length, z0), (length, z1), (0, z1)]
        self.quad(mat, (p[0], p[1], z0), (q[0], q[1], z0), (q[0], q[1], z1), (p[0], p[1], z1), uvs)
        self.quad(mat, (q[0], q[1], z0), (p[0], p[1], z0), (p[0], p[1], z1), (q[0], q[1], z1), uvs)

    def prism(self, mat, poly, z0, z1, top_mat=None):
        self.walls(mat, poly, z0, z1)
        self.polygon(top_mat or mat, poly, z1)

    def blob(self, mat, x, y, z, rx, rz, seg=7, seed=0.0):
        """Low-poly rounded shrub/canopy: an octagonal-ish dome-capped lump, flat-shaded."""
        rings = [(0.0, 0.55), (0.45, 1.0), (0.85, 0.75), (1.0, 0.0)]  # (height fraction, radius fraction)
        pts = []
        for hf, rf in rings:
            ring = []
            for i in range(seg):
                a = 2 * math.pi * (i + 0.5 * (hf > 0.5)) / seg + seed
                wob = 1.0 + 0.12 * math.sin(a * 3 + seed * 5)
                ring.append((x + math.cos(a) * rx * rf * wob, y + math.sin(a) * rx * rf * wob, z + hf * rz))
            pts.append(ring)
        t = self.tri_list(mat)
        for r0, r1 in zip(pts, pts[1:]):
            for i in range(seg):
                a, b = r0[i], r0[(i + 1) % seg]
                c, d = r1[(i + 1) % seg], r1[i]
                if math.dist(c, d) < 1e-6:
                    tris = [(a, b, c)]
                else:
                    tris = [(a, b, c), (a, c, d)]
                for tri in tris:
                    for v in tri:
                        t["v"].append(v)
                        t["uv"].append((v[0] / 2, v[2] / 2))
        # Bottom cap (rarely seen, keeps the lump closed for shadows).
        ring = pts[0]
        for i in range(1, seg - 1):
            for v in (ring[0], ring[i + 1], ring[i]):
                t["v"].append(v)
                t["uv"].append((0, 0))


def write_unity_text(path, entries):
    """entries: list of (key, name, Parts). Writes Unity-space triangle lists (see module docstring)."""
    with open(path, "w", encoding="utf8") as fh:
        for key, name, parts in entries:
            fh.write(f"B\t{key}\t{name}\n")
            for mat, t in parts.mats.items():
                v, uv = t["v"], t["uv"]
                verts, uvs = [], []
                for i in range(0, len(v) - 2, 3):
                    for k in (0, 2, 1):
                        x, y, z = v[i + k]
                        verts.extend((round(x, 3), round(z, 3), round(y, 3)))
                        uvs.extend((round(uv[i + k][0], 4), round(uv[i + k][1], 4)))
                if verts:
                    fh.write("M\t" + mat + "\t" + ",".join(f"{x:g}" for x in verts) + "\t" + ",".join(f"{x:g}" for x in uvs) + "\n")
