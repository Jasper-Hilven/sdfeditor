#!/usr/bin/env python3
"""Normalizes a monster head (ticket 45) to the common size and the common height: meshes content/models/characters/monsters/<id>.sdf,
fits an ellipsoid to the mesh's volume (the same fit as SdfHeads.HeadFit: the volume's second moments, brought to its volume), and rewrites
the two lines at the top of content/parts/<id>_head.sdf,
    Translate translation=(0, DY, 0)
        Scale scale=(S, S, S)
so the fit's volume is the target (1.2 cubed times the mean of phase 1's Yeti and pumpkin, 0.1048 m3) and the head's underside is at y=-0.29
(1.525 m over the soles, the socket being at 1.815). The underside is the lowest point of the mesh within 12 cm of the neck's axis, or --ref
(the lowest point of the main mass as drawn, before scaling) for a head with something hanging below it (tentacles, a beard's tips).
The model's Detail is set to --detail divided by the scale, so the cell size follows the head's size. Prints the fit against the target.
Usage: tools/normalize-head.py <id> [--detail 0.9] [--ref -0.25] [--passes 2]"""
import math, os, re, struct, json, subprocess, sys, tempfile
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SDF = os.path.join(ROOT, "SDFPipeline.Cli/bin/Debug/net10.0/sdf")
P1 = {"yeti": ((0.2701, 0.2519, 0.2882), 0.08214), "pumpkin": ((0.2130, 0.2164, 0.2030), 0.03918)}
TARGET_R = [1.2 * (P1["yeti"][0][k] + P1["pumpkin"][0][k]) / 2 for k in range(3)]
TARGET_V = 1.2 ** 3 * (P1["yeti"][1] + P1["pumpkin"][1]) / 2

def load(path):
    f = open(path, "rb").read()
    n = struct.unpack("<I", f[12:16])[0]
    j = json.loads(f[20:20 + n]); off = 20 + n
    bl = struct.unpack("<I", f[off:off + 4])[0]
    return j, f[off + 8:off + 8 + bl]

def acc(j, b, i):
    a = j["accessors"][i]; bv = j["bufferViews"][a["bufferView"]]
    o = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    nc = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[a["type"]]
    fmt = {5126: "f", 5125: "I", 5123: "H", 5121: "B"}[a["componentType"]]
    stride = bv.get("byteStride", struct.calcsize(fmt) * nc)
    return [struct.unpack_from("<" + fmt * nc, b, o + k * stride) for k in range(a["count"])]

def mul(m, p):
    x, y, z = p
    return (m[0]*x + m[4]*y + m[8]*z + m[12], m[1]*x + m[5]*y + m[9]*z + m[13], m[2]*x + m[6]*y + m[10]*z + m[14])

def measure(path):
    j, b = load(path)
    V = 0.0; C = [0.0] * 3; S2 = [0.0] * 3; tris = 0; under = 1e9; lo = [1e9] * 3; hi = [-1e9] * 3
    for n in j["nodes"]:
        if "mesh" not in n or "_LOD" in n["name"]: continue
        m = n.get("matrix", [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1])
        for pr in j["meshes"][n["mesh"]]["primitives"]:
            Q = [mul(m, p) for p in acc(j, b, pr["attributes"]["POSITION"])]
            I = [x[0] for x in acc(j, b, pr["indices"])]
            for q in Q:
                for k in range(3): lo[k] = min(lo[k], q[k]); hi[k] = max(hi[k], q[k])
                if math.hypot(q[0], q[2]) <= 0.12: under = min(under, q[1])
            for t in range(0, len(I), 3):
                a, bb, c = Q[I[t]], Q[I[t+1]], Q[I[t+2]]; tris += 1
                v = (a[0]*(bb[1]*c[2]-bb[2]*c[1]) + a[1]*(bb[2]*c[0]-bb[0]*c[2]) + a[2]*(bb[0]*c[1]-bb[1]*c[0])) / 6
                V += v
                for k in range(3):
                    C[k] += v * (a[k] + bb[k] + c[k]) / 4
                    S2[k] += v / 10 * (a[k]**2 + bb[k]**2 + c[k]**2 + a[k]*bb[k] + a[k]*c[k] + bb[k]*c[k])
    sg = 1 if V > 0 else -1
    V *= sg; cen = [c * sg / V for c in C]
    var = [S2[k] * sg / V - cen[k] ** 2 for k in range(3)]
    r = [math.sqrt(5 * max(0, v)) for v in var]
    k = (V / (4 / 3 * math.pi * r[0] * r[1] * r[2])) ** (1 / 3)
    return dict(vol=V, centre=cen, radii=[x * k for x in r], underside=under, tris=tris, lo=lo, hi=hi)

def main():
    a = sys.argv[1:]
    id = a[0]
    opt = lambda name, d: float(a[a.index(name) + 1]) if name in a else d
    detail, ref, passes = opt("--detail", 0.9), (opt("--ref", None) if "--ref" in a else None), int(opt("--passes", 2))
    part = os.path.join(ROOT, f"content/parts/{id}_head.sdf"); model = os.path.join(ROOT, f"content/models/characters/monsters/{id}.sdf")
    t = open(part).read()
    m = re.search(r"^Translate translation=\(0, (-?[\d.]+), 0\)\n    Scale scale=\(([\d.]+), [\d.]+, [\d.]+\)\n", t, re.M)
    s, dy = float(m.group(2)), float(m.group(1))
    for p in range(passes + 1):
        mt = open(model).read()
        mt = re.sub(r"^Detail detail=[\d.]+", f"Detail detail={detail / s:.3f}", mt, flags=re.M)
        open(model, "w").write(mt)
        out = os.path.join(tempfile.gettempdir(), f"normalize-{id}.glb")
        subprocess.run([SDF, "mesh", model, out, "--resolution", "128"], check=True, stdout=subprocess.DEVNULL)
        r = measure(out)
        dev = [r["radii"][k] / TARGET_R[k] - 1 for k in range(3)] + [r["vol"] / TARGET_V - 1]
        print(f"pass {p}: scale {s:.4f} dy {dy:.4f}  vol {r['vol']:.4f} (target {TARGET_V:.4f})  radii {[round(x, 3) for x in r['radii']]} "
              f"dev x/y/z/vol {[f'{d:+.1%}' for d in dev]}  underside {r['underside']:.3f}  tris {r['tris']}  centre {[round(x, 3) for x in r['centre']]}")
        if p == passes: break
        k = (TARGET_V / r["vol"]) ** (1 / 3)
        dy = -0.29 - (ref * s * k if ref is not None else (r["underside"] - dy) * k)
        s *= k
        t = t[:m.start()] + f"Translate translation=(0, {dy:.4f}, 0)\n    Scale scale=({s:.4f}, {s:.4f}, {s:.4f})\n" + t[m.end():]
        open(part, "w").write(t); m = re.search(r"^Translate translation=\(0, (-?[\d.]+), 0\)\n    Scale scale=\(([\d.]+), [\d.]+, [\d.]+\)\n", t, re.M)
    print(f"final: scale {s:.4f}, dy {dy:.4f}, detail {detail / s:.3f}; target radii {[round(x, 4) for x in TARGET_R]}")
main()
