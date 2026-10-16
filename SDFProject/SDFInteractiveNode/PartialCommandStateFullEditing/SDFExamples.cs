using System.Collections.Immutable;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public sealed record SDFExample(string Name, string Description, Func<SDFExampleScript, SDFExampleScript> Script)
{
    public ImmutableList<SDFPartialHistoryEntry> Entries(SDFPartialCommandNodeProgram initial) => Script(new SDFExampleScript(initial)).Entries;

    public SDFPartialActionHistory Load(SDFPartialCommandNodeProgram initial)
    {
        var entries = Entries(initial);
        return new SDFPartialActionHistory(initial, entries, null, initial).GoTo(entries.Count - 1);
    }
}

public static class SDFExamples
{
    public static readonly IReadOnlyList<SDFExample> All =
    [
        new("Snowman",
            "Three smoothly melted snowballs with a carrot nose, coal eyes and buttons, stick arms, a scarf and a top hat.",
            s => s
                .Replace(new ReplaceWithSphere()).Set("radius", 0.6f).Paint("white")
                .Add(new AddSphere()).Set("radius", 0.42f).Paint("white").Move(0, 0.85f, 0).Outer()
                .Add(new AddSphere()).Set("radius", 0.3f).Paint("white").Move(0, 1.5f, 0).Outer()
                .Parent().Smooth(0.12f)
                .Add(new AddArbitraryRoundCone()).Set("a", 0, 1.5f, 0.2f).Set("b", 0, 1.48f, 0.62f).Set("radiusA", 0.07f).Set("radiusB", 0.012f).Paint("orange")
                .Add(new AddSphere()).Set("radius", 0.045f).Paint("black").Move(-0.11f, 1.6f, 0.26f).Outer()
                .Add(new AddSphere()).Set("radius", 0.045f).Paint("black").Move(0.11f, 1.6f, 0.26f).Outer()
                .Add(new AddSphere()).Set("radius", 0.04f).Paint("black").Move(0, 1f, 0.39f).Outer()
                .Add(new AddSphere()).Set("radius", 0.04f).Paint("black").Move(0, 0.85f, 0.42f).Outer()
                .Add(new AddSphere()).Set("radius", 0.04f).Paint("black").Move(0, 0.7f, 0.39f).Outer()
                .Add(new AddCapsule()).Set("a", 0.32f, 0.95f, 0).Set("b", 0.85f, 1.3f, 0).Set("radius", 0.03f).Paint("brown")
                .Add(new AddCapsule()).Set("a", -0.32f, 0.95f, 0).Set("b", -0.8f, 1.25f, 0.1f).Set("radius", 0.03f).Paint("brown")
                .Add(new AddTorus()).Set("innerRadius", 0.21f).Set("outerRadius", 0.33f).Paint("red").Move(0, 1.24f, 0).Outer()
                .Add(new AddCappedCylinder()).Set("radius", 0.33f).Set("height", 0.015f).Paint("black").Move(0, 1.76f, 0).Outer()
                .Add(new AddCappedCylinder()).Set("radius", 0.21f).Set("height", 0.16f).Paint("black").Move(0, 1.93f, 0).Outer()
                .Add(new AddPlane()).Set("height", 0.6f).Paint("grey")),

        new("Mushrooms",
            "A red toadstool with white spots, its stem smoothly grown into the cap, and a small brown mushroom beside it on the grass.",
            s => s
                .Replace(new ReplaceWithCappedCylinder()).Set("radius", 0.16f).Set("height", 0.4f).Paint("white").Move(0, 0.4f, 0).Outer()
                .Add(new AddCutSphere()).Set("radius", 0.7f).Set("cutHeight", 0.15f).Paint("red").Move(0, 0.6f, 0).Outer()
                .Parent().Smooth(0.08f)
                .Add(new AddSphere()).Set("radius", 0.09f).Paint("white").Move(0, 1.3f, 0).Outer()
                .Add(new AddSphere()).Set("radius", 0.08f).Paint("white").Move(0.42f, 1.125f, 0.195f).Outer()
                .Add(new AddSphere()).Set("radius", 0.07f).Paint("white").Move(-0.35f, 1.09f, 0.35f).Outer()
                .Add(new AddSphere()).Set("radius", 0.08f).Paint("white").Move(0.07f, 1.09f, -0.49f).Outer()
                .Add(new AddSphere()).Set("radius", 0.06f).Paint("white").Move(-0.455f, 1.125f, -0.07f).Outer()
                .Add(new AddCappedCylinder()).Set("radius", 0.08f).Set("height", 0.2f).Paint("tan").Move(0.75f, 0.2f, 0.35f).Outer()
                .Add(new AddCutSphere()).Set("radius", 0.32f).Set("cutHeight", 0.07f).Paint("brown").Move(0.75f, 0.3f, 0.35f).Outer()
                .Add(new AddPlane()).Paint("green")),

        new("Coffee mug",
            "A mug hollowed out by subtraction, with a smoothly attached handle, coffee in a custom colour, a spoon and a saucer.",
            s => s
                .Replace(new ReplaceWithCappedCylinder()).Set("radius", 0.5f).Set("height", 0.6f)
                .Add(new AddTorus()).Set("innerRadius", 0.2f).Set("outerRadius", 0.34f).Turn(90, 0, 0).Move(0.5f, 0.05f, 0).Outer()
                .Parent().Smooth(0.05f)
                .Subtract().Replace(new ReplaceWithCappedCylinder()).Set("radius", 0.43f).Set("height", 0.6f).Move(0, 0.1f, 0).Outer()
                .Parent().Paint("white")
                .Add(new AddCappedCylinder()).Set("radius", 0.43f).Set("height", 0.01f).Paint("brown", 0.3f, 0.17f, 0.08f).Move(0, 0.35f, 0).Outer()
                .Add(new AddCapsule()).Set("a", 0.1f, 0.2f, 0.15f).Set("b", 0.45f, 1f, 0.35f).Set("radius", 0.025f).Paint("grey")
                .Add(new AddRoundedCylinder()).Set("radius", 0.8f).Set("roundness", 0.03f).Set("height", 0.02f).Paint("white").Move(0, -0.65f, 0).Outer()),

        new("Rocket",
            "A white rocket with a red nose cone, four fins, a framed porthole and an orange exhaust flame.",
            s => s
                .Replace(new ReplaceWithCappedCylinder()).Set("radius", 0.3f).Set("height", 0.6f).Paint("white").Move(0, 0.6f, 0).Outer()
                .Add(new AddCappedCone()).Set("height", 0.3f).Set("bottomRadius", 0.3f).Set("topRadius", 0.02f).Paint("red").Move(0, 1.5f, 0).Outer()
                .Parent().Smooth(0.05f)
                .Add(new AddTriPrism()).Set("size", 0.4f, 0.02f).Paint("red").Move(0.38f, 0.1f, 0).Outer()
                .Add(new AddTriPrism()).Set("size", 0.4f, 0.02f).Paint("red").Move(-0.38f, 0.1f, 0).Outer()
                .Add(new AddTriPrism()).Set("size", 0.4f, 0.02f).Paint("red").Turn(0, 90, 0).Move(0, 0.1f, 0.38f).Outer()
                .Add(new AddTriPrism()).Set("size", 0.4f, 0.02f).Paint("red").Turn(0, 90, 0).Move(0, 0.1f, -0.38f).Outer()
                .Add(new AddSphere()).Set("radius", 0.12f).Paint("blue").Move(0, 0.85f, 0.22f).Outer()
                .Add(new AddTorus()).Set("innerRadius", 0.1f).Set("outerRadius", 0.16f).Paint("grey").Turn(90, 0, 0).Move(0, 0.85f, 0.29f).Outer()
                .Add(new AddCappedCone()).Set("height", 0.2f).Set("bottomRadius", 0.03f).Set("topRadius", 0.2f).Paint("orange").Move(0, -0.2f, 0).Outer()),

        new("Die",
            "A red die: a box intersected with a sphere for rounded corners, with six pips carved out by a smooth subtraction.",
            s => s
                .Intersect().Set("radius", 0.7f)
                .Parent().Subtract().Set("radius", 0.1f).Move(0, 0.53f, 0).Outer()
                .Add(new AddSphere()).Set("radius", 0.1f).Move(-0.2f, 0.2f, 0.53f).Outer()
                .Add(new AddSphere()).Set("radius", 0.1f).Move(0.2f, -0.2f, 0.53f).Outer()
                .Add(new AddSphere()).Set("radius", 0.1f).Move(0.53f, 0.22f, 0.22f).Outer()
                .Add(new AddSphere()).Set("radius", 0.1f).Move(0.53f, 0, 0).Outer()
                .Add(new AddSphere()).Set("radius", 0.1f).Move(0.53f, -0.22f, -0.22f).Outer()
                .Parent(2).Smooth(0.02f).Paint("red")
                .Turn(0, 15, 0)),

        new("Chess pawn",
            "A black pawn turned from a rounded base, a ring, a tapered body, a collar and a head, all smoothly merged and stored as a variable, on a white board.",
            s => s
                .Replace(new ReplaceWithRoundedCylinder()).Set("radius", 0.42f).Set("roundness", 0.04f).Set("height", 0.06f).Move(0, 0.1f, 0).Outer()
                .Add(new AddTorus()).Set("innerRadius", 0.22f).Set("outerRadius", 0.38f).Move(0, 0.2f, 0).Outer()
                .Add(new AddCappedCone()).Set("height", 0.35f).Set("bottomRadius", 0.28f).Set("topRadius", 0.1f).Move(0, 0.55f, 0).Outer()
                .Add(new AddRoundedCylinder()).Set("radius", 0.2f).Set("roundness", 0.03f).Set("height", 0.02f).Move(0, 0.92f, 0).Outer()
                .Add(new AddSphere()).Set("radius", 0.22f).Move(0, 1.12f, 0).Outer()
                .Parent().Smooth(0.06f).Paint("black")
                .Add(new AddBox()).Set("size", 0.7f, 0.05f, 0.7f).Paint("white").Move(0, -0.05f, 0).Outer()
                .Parent().Child().Extract("pawn")),

        new("Ringed planet",
            "A blue planet with a tilted ring (a disc minus a disc, stored as a variable) and two moons, one of them cratered.",
            s => s
                .Replace(new ReplaceWithCappedCylinder()).Set("radius", 1.3f).Set("height", 0.015f)
                .Subtract().Replace(new ReplaceWithCappedCylinder()).Set("radius", 0.95f).Set("height", 0.1f)
                .Parent().Paint("tan").Turn(20, 0, 15).Outer()
                .Add(new AddSphere()).Set("radius", 0.7f).Paint("blue")
                .Add(new AddSphere()).Set("radius", 0.18f)
                .Subtract().Set("radius", 0.08f).Move(0.12f, 0.1f, 0.12f).Outer()
                .Parent().Paint("grey").Move(1.6f, 0.5f, 0.3f).Outer()
                .Add(new AddSphere()).Set("radius", 0.09f).Paint("white").Move(-1.3f, -0.55f, 0.8f).Outer()
                .Parent().Child().Extract("ring")),

        new("Castle tower",
            "A stone tower with a carved door and windows, a crenellated crown cut by four rotated boxes, a red roof and a flag.",
            s => s
                .Replace(new ReplaceWithCappedCylinder()).Set("radius", 0.5f).Set("height", 0.8f)
                .Subtract().Replace(new ReplaceWithBox()).Set("size", 0.14f, 0.22f, 0.3f).Move(0, -0.58f, 0.5f).Outer()
                .Add(new AddBox()).Set("size", 0.06f, 0.12f, 0.3f).Move(0, 0.25f, 0.5f).Outer()
                .Add(new AddBox()).Set("size", 0.3f, 0.12f, 0.06f).Move(0.5f, 0.25f, 0).Outer()
                .Add(new AddBox()).Set("size", 0.3f, 0.12f, 0.06f).Move(-0.5f, -0.1f, 0).Outer()
                .Parent(2).Paint("grey").Move(0, 0.8f, 0).Outer()
                .Add(new AddCappedCylinder()).Set("radius", 0.6f).Set("height", 0.12f)
                .Subtract().Replace(new ReplaceWithBox()).Set("size", 0.06f, 0.08f, 0.7f).Move(0, 0.1f, 0).Outer()
                .Add(new AddBox()).Set("size", 0.7f, 0.08f, 0.06f).Move(0, 0.1f, 0).Outer()
                .Add(new AddBox()).Set("size", 0.06f, 0.08f, 0.7f).Turn(0, 45, 0).Move(0, 0.1f, 0).Outer()
                .Add(new AddBox()).Set("size", 0.06f, 0.08f, 0.7f).Turn(0, -45, 0).Move(0, 0.1f, 0).Outer()
                .Parent(2).Paint("grey").Move(0, 1.72f, 0).Outer()
                .Add(new AddCappedCone()).Set("height", 0.35f).Set("bottomRadius", 0.5f).Set("topRadius", 0.01f).Paint("red").Move(0, 2.15f, 0).Outer()
                .Add(new AddCapsule()).Set("a", 0, 2.45f, 0).Set("b", 0, 2.85f, 0).Set("radius", 0.015f).Paint("grey")
                .Add(new AddBox()).Set("size", 0.15f, 0.08f, 0.01f).Paint("yellow").Move(0.15f, 2.77f, 0).Outer()
                .Add(new AddPlane()).Paint("green")),

        new("House",
            "A cottage with a prism roof, a door, windows on two sides and a chimney, next to a round tree and a pine on a lawn with a path.",
            s => s
                .Set("size", 0.8f, 0.5f, 0.6f).Paint("white").Move(0, 0.5f, 0).Outer()
                .Add(new AddTriPrism()).Set("size", 1.05f, 0.68f).Paint("red").Resize(1, 0.5f, 1).Move(0, 1.26f, 0).Outer()
                .Add(new AddBox()).Set("size", 0.15f, 0.25f, 0.02f).Paint("brown").Move(0, 0.25f, 0.6f).Outer()
                .Add(new AddBox()).Set("size", 0.14f, 0.12f, 0.02f).Paint("blue").Move(-0.45f, 0.6f, 0.6f).Outer()
                .Add(new AddBox()).Set("size", 0.14f, 0.12f, 0.02f).Paint("blue").Move(0.45f, 0.6f, 0.6f).Outer()
                .Add(new AddBox()).Set("size", 0.02f, 0.12f, 0.14f).Paint("blue").Move(0.8f, 0.6f, 0).Outer()
                .Add(new AddBox()).Set("size", 0.08f, 0.25f, 0.08f).Paint("grey").Move(0.45f, 1.4f, -0.2f).Outer()
                .Add(new AddCappedCylinder()).Set("radius", 0.07f).Set("height", 0.3f).Paint("brown").Move(1.5f, 0.3f, 0.4f).Outer()
                .Add(new AddSphere()).Set("radius", 0.4f).Paint("green").Move(1.5f, 0.9f, 0.4f).Outer()
                .Add(new AddCappedCylinder()).Set("radius", 0.06f).Set("height", 0.2f).Paint("brown").Move(-1.5f, 0.2f, -0.2f).Outer()
                .Add(new AddCappedCone()).Set("height", 0.4f).Set("bottomRadius", 0.35f).Set("topRadius", 0.02f).Paint("green").Move(-1.5f, 0.8f, -0.2f).Outer()
                .Add(new AddBox()).Set("size", 0.15f, 0.01f, 0.5f).Paint("grey").Move(0, 0.01f, 1.1f).Outer()
                .Add(new AddPlane()).Paint("lime")),

        new("Rubber duck",
            "A yellow duck made of smoothly blended ellipsoids for body, wings and tail, a sphere head, an orange beak and black eyes, floating on water.",
            s => s
                .Replace(new ReplaceWithEllipsoid()).Set("radii", 0.6f, 0.38f, 0.45f).Move(0, 0.4f, 0).Outer()
                .Add(new AddSphere()).Set("radius", 0.28f).Move(0.32f, 0.95f, 0).Outer()
                .Add(new AddEllipsoid()).Set("radii", 0.22f, 0.1f, 0.16f).Turn(0, 0, -35).Move(-0.55f, 0.6f, 0).Outer()
                .Add(new AddEllipsoid()).Set("radii", 0.3f, 0.14f, 0.08f).Turn(0, 0, -10).Move(-0.05f, 0.5f, 0.42f).Outer()
                .Add(new AddEllipsoid()).Set("radii", 0.3f, 0.14f, 0.08f).Turn(0, 0, -10).Move(-0.05f, 0.5f, -0.42f).Outer()
                .Parent().Smooth(0.15f).Paint("yellow")
                .Add(new AddEllipsoid()).Set("radii", 0.16f, 0.05f, 0.13f).Paint("orange").Move(0.6f, 0.9f, 0).Outer()
                .Add(new AddSphere()).Set("radius", 0.045f).Paint("black").Move(0.52f, 1.05f, 0.16f).Outer()
                .Add(new AddSphere()).Set("radius", 0.045f).Paint("black").Move(0.52f, 1.05f, -0.16f).Outer()
                .Add(new AddPlane()).Set("height", -0.15f).Paint("teal")),
        .. MushroomExamples.All,
    ];
}
