#!/usr/bin/env python3
"""Writes the bursting pieces of every monster head (ticket 51): content/models/characters/head-pieces/<id>-head-piece<N>.sdf.
A head is the part content/parts/<id>_head.sdf (frame: origin at the soldier's head socket, +Z forward). Each piece is the head intersected
with one Voronoi cell of a fixed set of seeds (the cells split all of space, so the pieces fit together exactly and together are the head),
the cut faces in the material head_inner. A new head needs only its part: run this, then
  sdf mesh content/models/characters/head-pieces --into Unity/Assets/Mushrooms/Generated/characters/head-pieces --resolution 128 --lods 1
The plane convention is the pipeline's: inside where dot(normal, p) + height <= 0."""
import math, os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SEEDS = [  # name, seed in the head's frame
    ("crown", (0, 0.13, 0)), ("front left", (0.11, -0.02, 0.12)), ("front right", (-0.11, -0.02, 0.12)),
    ("back left", (0.11, -0.02, -0.12)), ("back right", (-0.11, -0.02, -0.12)),
    ("chin", (0, -0.14, 0.10)), ("underside", (0, -0.17, -0.08)),
]
def piece(id, i):
    name, s = SEEDS[i]
    planes = []
    for j, (_, t) in enumerate(SEEDS):
        if j == i: continue
        d = [t[k] - s[k] for k in range(3)]
        n = math.sqrt(sum(x * x for x in d)); d = [x / n for x in d]
        mid = [(s[k] + t[k]) / 2 for k in range(3)]
        h = -sum(d[k] * mid[k] for k in range(3))
        planes.append(f"                Plane normal=({d[0]:.4f}, {d[1]:.4f}, {d[2]:.4f}) height={h:.4f}")
    return f"""# The {id} head, bursting: the {name} piece ({i + 1} of {len(SEEDS)}). Made by tools/make-head-pieces.py (do not edit by hand): the head
# (the {id}_head part, so the pieces follow its edits) intersected with the Voronoi cell of one seed; the cells split all of space with
# no gap or overlap, so the pieces together are the head. The cut faces are the head_inner material. Low detail: debris lives seconds.
format 1
collider none
surface part={id}_head

Detail detail=0.5
    Intersection
        Part part={id}_head
        Material material=head_inner
            Intersection
{chr(10).join(planes)}
"""
def main():
    out = os.path.join(ROOT, "content/models/characters/head-pieces"); os.makedirs(out, exist_ok=True)
    for f in sorted(os.listdir(os.path.join(ROOT, "content/parts"))):
        if f.endswith("_head.sdf"):
            id = f[:-len("_head.sdf")]
            for i in range(len(SEEDS)):
                open(os.path.join(out, f"{id}-head-piece{i}.sdf"), "w").write(piece(id, i))
            print(id, len(SEEDS))
main()
