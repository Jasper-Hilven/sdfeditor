using System.Numerics;
using V3 = SDFProject.ProgramDefinition.V3;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public static class MushroomExamples
{
    private enum Shape { Classic, Fat, Tall, Bulbous, Twisted, Cluster, Giant }

    private enum EyeStyle { Normal, Angry, Wide, Crazy, Dead }

    private enum HandStyle { Normal, Big, Tiny, Claw }

    private sealed record Ramp(Vector3 Shadow, Vector3 Base, Vector3 Light);

    private static Vector3 Hex(string hex) => new(
        Convert.ToInt32(hex[1..3], 16) / 255f, Convert.ToInt32(hex[3..5], 16) / 255f, Convert.ToInt32(hex[5..7], 16) / 255f);

    private static readonly Ramp Red = new(Hex("#C03040"), Hex("#E04040"), Hex("#F07060"));
    private static readonly Ramp Brown = new(Hex("#907060"), Hex("#A08070"), Hex("#B0A080"));
    private static readonly Ramp Green = new(Hex("#508060"), Hex("#60A050"), Hex("#80C060"));
    private static readonly Ramp Yellow = new(Hex("#FFA030"), Hex("#FFC030"), Hex("#FFE060"));
    private static readonly Ramp StemCream = new(Hex("#A08070"), Hex("#B0A080"), Hex("#D0C0A0"));
    private static readonly Ramp StemGrey = new(Hex("#505060"), Hex("#707080"), Hex("#9090A0"));
    private static readonly Ramp Rot = new(Hex("#508060"), Hex("#40A070"), Hex("#60C080"));

    private static readonly Vector3 Bone = Hex("#E0E0C0");
    private static readonly Vector3 Sclera = Hex("#FFFFFF");
    private static readonly Vector3 Iris = Hex("#4090D0");
    private static readonly Vector3 DeadIris = Hex("#9090A0");
    private static readonly Vector3 Pupil = Hex("#202030");
    private static readonly Vector3 GunMetal = Hex("#707080");
    private static readonly Vector3 GunDark = Hex("#303040");
    private static readonly Vector3 GunAccent = Hex("#786058");

    private const float HumanHeight = 1.75f;
    private const float MaxHeight = 2.0f;
    private const float MinHeight = 0.6f;

    private static float ClampHeight(float height) => Math.Clamp(height, MinHeight, MaxHeight);

    private sealed record Archetype(string Name, string Description, Shape Shape, int Seed, Action<Parameters> Configure, bool HandAssembled = false);

    private static readonly Archetype[] Archetypes =
    [
        new("Grunt mushroom", "Classic fly agaric: the shape everything else is measured against.", Shape.Classic, 10006,
            p => p.CapRamp = Red),
        new("Tank mushroom", "Huge cap, short thick body, big arms.", Shape.Fat, 10001, p =>
        {
            p.Height = 1.55f; p.CapWidth = 3.1f; p.CapHeight = 0.85f; p.CapRoundness = 1.0f; p.StemHeight = 0.9f;
            p.StemThickness = 0.62f; p.StemBulb = 0.4f; p.HandStyle = HandStyle.Big; p.HandSize = 0.3f; p.ArmLength = 0.24f;
            p.EyeStyle = EyeStyle.Angry; p.EyeSize = 0.24f; p.CapRamp = Red; p.SpotCount = 12;
        }),
        new("Speed mushroom", "Lean and tall, huge eyes.", Shape.Tall, 10002, p =>
        {
            p.Height = 1.95f; p.StemHeight = 2.3f; p.StemThickness = 0.17f; p.StemCurvature = 0.16f; p.CapWidth = 1.2f;
            p.CapHeight = 0.6f; p.EyeStyle = EyeStyle.Wide; p.EyeSize = 0.2f; p.EyeSpacing = 0.26f; p.HandStyle = HandStyle.Tiny;
            p.HandSize = 0.13f; p.CapRamp = Yellow; p.SpotCount = 4; p.SpotRadius = 0.1f;
        }),
        new("Heavy mushroom", "Wide body, big cap, heavy build.", Shape.Bulbous, 10004, p =>
        {
            p.Height = 1.7f; p.CapWidth = 2.4f; p.CapHeight = 1.2f; p.CapRoundness = 1.3f; p.StemThickness = 0.5f;
            p.StemHeight = 1.15f; p.StemBulb = 0.5f; p.HandStyle = HandStyle.Big; p.HandSize = 0.27f; p.EyeStyle = EyeStyle.Angry;
            p.CapRamp = Brown; p.SpotCount = 7;
        }),
        new("Crazy mushroom", "Crooked cap, mismatched eyes, twitchy.", Shape.Twisted, 10005, p =>
        {
            p.Height = 1.8f; p.EyeStyle = EyeStyle.Crazy; p.EyeMismatch = 0.75f; p.EyeSize = 0.27f; p.HandStyle = HandStyle.Claw;
            p.CapTilt = 22f; p.CapAsymmetry = 0.32f; p.StemTwist = 55f; p.CapRamp = Green; p.SpotCount = 6;
        }),
        new("Rotten heavy mushroom", "Rotten, mouldy, still armed.", Shape.Bulbous, 10007, p =>
        {
            p.Height = 1.8f; p.CapWidth = 2.0f; p.CapHeight = 1.0f; p.CapUndersideDepth = 0.32f; p.CapTilt = 12f;
            p.CapAsymmetry = 0.2f; p.CapWaviness = 0.12f; p.StemHeight = 1.45f; p.StemThickness = 0.4f; p.StemCurvature = 0.16f;
            p.HandStyle = HandStyle.Big; p.EyeStyle = EyeStyle.Dead; p.CapRamp = Brown; p.StemRamp = StemGrey; p.Spots = false; p.Rot = 0.8f;
        }),
        new("Cluster mushroom", "Several small caps on one base.", Shape.Cluster, 10008, p =>
        {
            p.EyeStyle = EyeStyle.Wide; p.CapRamp = Brown;
        }),
        new("Giant mushroom", "Boss sized, oversized everything.", Shape.Giant, 10009, p =>
        {
            p.Height = MaxHeight; p.EyeStyle = EyeStyle.Angry; p.HandStyle = HandStyle.Big; p.CapRamp = Red;
        }),
        new("Big fat mushroom", "Hand assembled: stem, cap, eyes, hands, reference gun.", Shape.Fat, 182739, p =>
        {
            p.CapRamp = Red; p.StemRamp = StemCream; p.StemThickness = 0.62f; p.StemHeight = 1.1f; p.CapWidth = 2.6f;
            p.CapHeight = 0.72f; p.HandStyle = HandStyle.Big; p.SpotRadius = 0.22f; p.SpotCount = 10; p.Height = 1.6f;
        }, HandAssembled: true),
    ];

    public static IReadOnlyList<SDFExample> All { get; } = Archetypes
        .Select(a => new SDFExample(a.Name, $"{a.Description} Ported from the MM2 art pipeline (seed {a.Seed}).", s => Build(s, a)))
        .ToArray();

    private sealed class Parameters
    {
        public float Height = HumanHeight;
        public float CapWidth = 2.0f;
        public float CapHeight = 0.75f;
        public float CapRoundness = 0.75f;
        public float CapUndersideDepth = 0.42f;
        public float CapRimThickness = 0.08f;
        public float CapAsymmetry = 0.06f;
        public float CapWaviness = 0.04f;
        public int CapWaveLobes = 5;
        public float CapTilt;
        public Vector3 CapOffset = Vector3.Zero;
        public float StemHeight = 1.4f;
        public float StemThickness = 0.34f;
        public float StemTaper = 0.8f;
        public float StemBulb = 0.25f;
        public float StemCurvature = 0.08f;
        public float StemLean;
        public float StemTwist;
        public EyeStyle EyeStyle = EyeStyle.Normal;
        public float EyeSize = 0.26f;
        public float EyeSpacing = 0.38f;
        public float EyeHeight = 0.58f;
        public float EyeMismatch;
        public HandStyle HandStyle = HandStyle.Normal;
        public float HandSize = 0.22f;
        public float ArmLength = 0.18f;
        public float ArmHeight = 0.45f;
        public Ramp CapRamp = Red;
        public Ramp StemRamp = StemCream;
        public bool Spots = true;
        public int SpotCount = 9;
        public float SpotRadius = 0.17f;
        public float Rot;

        public Parameters Clone() => (Parameters)MemberwiseClone();

        private float NominalHeight =>
            MathF.Max(0.05f, StemHeight) + MathF.Max(0.05f, CapHeight) * (1f - Math.Clamp(CapUndersideDepth, 0f, 0.9f)) + CapOffset.Y;

        public Parameters ScaleLengths(float factor)
        {
            if (factor <= 0f || MathF.Abs(factor - 1f) < 1e-6f)
                return this;
            CapWidth *= factor;
            CapHeight *= factor;
            CapRimThickness *= factor;
            CapWaviness *= factor;
            CapOffset *= factor;
            StemHeight *= factor;
            StemThickness *= factor;
            EyeSize *= factor;
            EyeSpacing *= factor;
            HandSize *= factor;
            ArmLength *= factor;
            SpotRadius *= factor;
            return this;
        }

        public Parameters Sized()
        {
            var p = Clone();
            p.Height = ClampHeight(Height);
            return p.ScaleLengths(p.Height / p.NominalHeight);
        }

        public static Parameters ForShape(Shape shape)
        {
            var p = new Parameters();
            switch (shape)
            {
                case Shape.Fat:
                    p.Height = 1.5f; p.CapWidth = 2.6f; p.CapHeight = 0.72f; p.CapRoundness = 0.9f; p.StemHeight = 0.85f;
                    p.StemThickness = 0.5f; p.StemBulb = 0.35f; p.EyeSize = 0.2f; p.EyeHeight = 0.55f; p.HandSize = 0.26f;
                    break;
                case Shape.Tall:
                    p.Height = 1.95f; p.CapWidth = 1.35f; p.CapHeight = 0.7f; p.CapRoundness = 0.55f; p.StemHeight = 2.5f;
                    p.StemThickness = 0.2f; p.StemTaper = 0.75f; p.StemCurvature = 0.14f; p.EyeSize = 0.17f; p.EyeSpacing = 0.2f;
                    p.EyeHeight = 0.72f; p.HandSize = 0.15f; p.SpotCount = 5; p.SpotRadius = 0.11f;
                    break;
                case Shape.Bulbous:
                    p.Height = 1.6f; p.CapWidth = 1.9f; p.CapHeight = 1.15f; p.CapRoundness = 1.25f; p.CapUndersideDepth = 0.75f;
                    p.StemHeight = 1.0f; p.StemThickness = 0.42f; p.StemBulb = 0.5f; p.StemTaper = 0.7f;
                    break;
                case Shape.Twisted:
                    p.Height = 1.8f; p.CapWidth = 2.1f; p.CapHeight = 0.8f; p.CapAsymmetry = 0.26f; p.CapWaviness = 0.14f;
                    p.CapWaveLobes = 4; p.CapTilt = 16f; p.CapOffset = new Vector3(0.18f, 0, 0.1f); p.StemHeight = 1.5f;
                    p.StemCurvature = 0.3f; p.StemTwist = 35f; p.EyeMismatch = 0.45f; p.EyeStyle = EyeStyle.Crazy;
                    break;
                case Shape.Cluster:
                    p.Height = 1.35f; p.CapWidth = 1.1f; p.CapHeight = 0.45f; p.StemHeight = 1.0f; p.StemThickness = 0.22f;
                    p.StemBulb = 0.45f; p.SpotCount = 4; p.SpotRadius = 0.1f;
                    break;
                case Shape.Giant:
                    p.Height = MaxHeight; p.CapWidth = 4.0f; p.CapHeight = 1.5f; p.CapRoundness = 0.95f; p.StemHeight = 2.4f;
                    p.StemThickness = 0.72f; p.StemBulb = 0.3f; p.EyeSize = 0.34f; p.EyeSpacing = 0.5f; p.HandSize = 0.4f;
                    p.ArmLength = 0.3f; p.SpotCount = 14; p.SpotRadius = 0.3f;
                    break;
            }
            return p;
        }

        public Parameters Vary(int seed)
        {
            var p = Clone();
            var rng = new Rng(seed);
            p.Height = ClampHeight(p.Height * rng.Range(0.94f, 1.06f));
            p.CapWidth *= rng.Range(0.88f, 1.14f);
            p.CapHeight *= rng.Range(0.85f, 1.18f);
            p.CapRoundness = MathF.Max(0.1f, p.CapRoundness * rng.Range(0.9f, 1.12f));
            p.CapAsymmetry += rng.Range(0f, 0.08f);
            p.CapWaviness *= rng.Range(0.7f, 1.4f);
            p.CapWaveLobes = Math.Max(3, p.CapWaveLobes + rng.Range(-1, 2));
            p.CapTilt += rng.Symmetric(6f);
            p.StemHeight *= rng.Range(0.88f, 1.15f);
            p.StemThickness *= rng.Range(0.9f, 1.12f);
            p.StemCurvature += rng.Symmetric(0.06f);
            p.StemTwist += rng.Symmetric(10f);
            p.StemLean += rng.Symmetric(4f);
            p.EyeSize *= rng.Range(0.9f, 1.15f);
            p.EyeSpacing *= rng.Range(0.9f, 1.12f);
            p.EyeHeight = Math.Clamp(p.EyeHeight + rng.Symmetric(0.06f), 0.3f, 0.85f);
            p.SpotCount = Math.Max(0, p.SpotCount + rng.Range(-2, 3));
            p.SpotRadius *= rng.Range(0.85f, 1.2f);
            var x = rng.Symmetric(0.05f);
            var z = rng.Symmetric(0.05f);
            p.CapOffset += new Vector3(x, 0f, z);
            return p;
        }
    }

    private struct Rng
    {
        private ulong state;

        public Rng(int seed) => state = Mix(unchecked((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0xDEADBEEFUL));

        private static ulong Mix(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        private ulong NextULong()
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                var z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        private uint NextUInt() => (uint)(NextULong() >> 32);

        private float NextFloat() => (NextUInt() >> 8) * (1.0f / 16777216.0f);

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        public float Symmetric(float amount) => Range(-amount, amount);

        public int Range(int min, int max) => max <= min ? min : min + (int)(NextUInt() % (uint)(max - min));
    }

    private sealed record Stem(Vector3 Base, float Height, float Radius, float Taper, float Bulb, float BottomScale, float TopScale,
        float LeanDegrees, Vector3 Color)
    {
        private Quaternion Lean => RotationXYZ(0, 0, -LeanDegrees);

        public Vector3 Top => Base + Vector3.Transform(new Vector3(0, Height, 0), Lean);

        public float TopY => Base.Y + Height * MathF.Cos(LeanDegrees * MathF.PI / 180);

        public float RadiusAt(float t) =>
            Radius * (1f + (Taper - 1f) * t + Bulb * MathF.Exp(-MathF.Pow(t / 0.26f, 2f))) * (BottomScale + (TopScale - BottomScale) * t);

        private float T(float y) => Math.Clamp((y - Base.Y) / (TopY - Base.Y), 0f, 1f);

        private Vector3 Axis(float y) => Base + Vector3.Transform(new Vector3(0, T(y) * Height, 0), Lean);

        public Vector3 Front(float y) => Axis(y) + new Vector3(0, 0, RadiusAt(T(y)));

        public Vector3 Side(float y, float side) => Axis(y) + new Vector3(side * RadiusAt(T(y)), 0, 0);

        public Stem Scaled(float k) => this with { Base = Base * k, Height = Height * k, Radius = Radius * k };
    }

    private sealed record Spot(Vector3 Center, float Radius, Vector3 Color);

    private sealed record Cap(Vector3 Mount, float Radius, float Height, float Hollow, float Rim, float Tilt, Vector3 Color, Vector3 GillColor,
        IReadOnlyList<Spot> Spots)
    {
        public Vector3 DomeCenter => Mount + Vector3.Transform(new Vector3(0, -Hollow, 0), RotationXYZ(0, 0, Tilt));

        public float Bottom => Mount.Y - Hollow - Rim;

        public float TopY => DomeCenter.Y + Height;

        public Cap Scaled(float k) => this with
        {
            Mount = Mount * k, Radius = Radius * k, Height = Height * k, Hollow = Hollow * k, Rim = Rim * k,
            Spots = Spots.Select(s => s with { Center = s.Center * k, Radius = s.Radius * k }).ToArray(),
        };
    }

    private sealed record Eye(Vector3 Center, float Size, EyeStyle Style, float TurnY, Vector3 LidColor)
    {
        public Eye Scaled(float k) => this with { Center = Center * k, Size = Size * k };
    }

    private sealed record Hand(Vector3 Center, float Size, HandStyle Style, bool Left, Vector3 Color);

    private sealed record Gun(Vector3 Position, Vector3 RotationDegrees);

    private sealed record Mushroom(IReadOnlyList<Stem> Stems, IReadOnlyList<Cap> Caps, IReadOnlyList<Eye> Eyes, IReadOnlyList<Hand> Hands, Gun Gun);

    private static Quaternion RotationXYZ(float xDegrees, float yDegrees, float zDegrees)
    {
        const float toRadians = MathF.PI / 180;
        return Quaternion.Concatenate(
            Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, xDegrees * toRadians),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, yDegrees * toRadians)),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, zDegrees * toRadians));
    }

    private static readonly Vector3 RearGrip = new(0, -0.2f, -0.1f);
    private static readonly Vector3 ForeGrip = new(0, -0.19f, 0.42f);
    private const float GunTop = 0.24f;
    private const float GunBottom = -0.3f;

    private static Mushroom Layout(Archetype archetype)
    {
        var seed = archetype.Seed;
        Parameters p;
        if (archetype.HandAssembled)
        {
            p = Parameters.ForShape(archetype.Shape);
            archetype.Configure(p);
        }
        else
        {
            p = Parameters.ForShape(archetype.Shape).Vary(seed);
            archetype.Configure(p);
        }
        p = p.Sized();

        var rot = Math.Clamp(p.Rot, 0f, 1f) * 0.35f;
        var capColor = Vector3.Lerp(p.CapRamp.Base, Rot.Base, rot);
        var stemColor = Vector3.Lerp(p.StemRamp.Base, Rot.Base, rot);
        var lean = p.StemLean + MathF.Atan(p.StemCurvature) * 180 / MathF.PI;

        var stem = archetype.HandAssembled
            ? new Stem(Vector3.Zero, p.StemHeight, p.StemThickness, p.StemTaper, p.StemBulb, 1.15f, 0.78f, lean, stemColor)
            : new Stem(Vector3.Zero, p.StemHeight, p.StemThickness, p.StemTaper, p.StemBulb, 1f, 1f, lean, stemColor);
        var mount = archetype.HandAssembled ? stem.Top : stem.Top + p.CapOffset;
        var cap = BuildCap(p, mount, archetype.HandAssembled ? 0 : p.CapTilt, capColor, seed + 1, p.Spots ? p.SpotCount : 0, Bone);
        if (p.Rot > 0)
            cap = cap with { Spots = cap.Spots.Concat(BuildCap(p, mount, cap.Tilt, capColor, seed + 17, 6, Rot.Shadow).Spots).ToArray() };

        var stems = new List<Stem> { stem };
        var caps = new List<Cap> { cap };
        if (archetype.Shape == Shape.Cluster)
            AddClusterSiblings(p, seed, stems, caps, capColor, stemColor);

        Vector3 face;
        var eyeParameters = p;
        var underCap = cap.Bottom - p.EyeSize * 1.05f;
        if (archetype.HandAssembled)
            face = stem.Front(cap.Bottom - p.EyeSize * 1.1f);
        else if (underCap >= p.EyeSize * 1.15f)
            face = stem.Front(MathF.Min(stem.TopY * p.EyeHeight, underCap));
        else
        {
            face = SupportPoint(cap, Vector3.Normalize(new Vector3(0, 0.5f, 1)));
            eyeParameters = p.Clone();
            eyeParameters.EyeSize = MathF.Max(p.EyeSize, p.CapWidth * 0.13f);
            eyeParameters.EyeSpacing = MathF.Max(p.EyeSpacing, eyeParameters.EyeSize * 1.5f);
        }

        var eyes = archetype.HandAssembled
            ? HandAssembledEyes(p, face)
            : EyePair(eyeParameters, face, seed + 7, p.CapRamp.Shadow);

        var armY = archetype.HandAssembled
            ? face.Y - p.EyeSize * 1.4f
            : MathF.Max(p.HandSize, MathF.Min(MathF.Min(face.Y - p.EyeSize * 1.3f, cap.Bottom - p.HandSize * 0.6f), stem.TopY * p.ArmHeight));

        if (!archetype.HandAssembled)
        {
            var top = MathF.Max(caps.Max(c => c.TopY), eyes.Max(e => e.Center.Y + e.Size * 1.15f));
            var fit = ClampHeight(p.Height) / top;
            if (MathF.Abs(fit - 1f) > 1e-4f)
            {
                stems = stems.Select(s => s.Scaled(fit)).ToList();
                caps = caps.Select(c => c.Scaled(fit)).ToList();
                eyes = eyes.Select(e => e.Scaled(fit)).ToList();
                face *= fit;
                armY *= fit;
                p = p.Clone().ScaleLengths(fit);
                stem = stems[0];
            }
        }

        return Equip(archetype, p, stem, stems, caps, eyes, face, armY, seed + 13, stemColor);
    }

    private static Mushroom Equip(Archetype archetype, Parameters p, Stem stem, List<Stem> stems, List<Cap> caps, List<Eye> eyes,
        Vector3 face, float armY, int seed, Vector3 handColor)
    {
        Vector3 hold;
        var rotation = Vector3.Zero;
        if (archetype.HandAssembled)
        {
            hold = new Vector3(stem.Side(armY, 1f).X * 0.95f, armY, p.StemThickness * 0.6f);
        }
        else
        {
            var rng = new Rng(seed);
            hold = new Vector3(stem.Side(armY, 1f).X * 1.1f, armY, stem.Front(armY).Z * 0.5f);
            var x = rng.Symmetric(4f);
            var y = rng.Symmetric(6f);
            var z = rng.Symmetric(5f);
            rotation = new Vector3(x, y, z);
        }

        var turn = RotationXYZ(rotation.X, rotation.Y, rotation.Z);
        var position = hold - Vector3.Transform(RearGrip, turn);
        if (!archetype.HandAssembled)
        {
            var overlap = position.Y + GunTop - (face.Y - p.EyeSize * 0.7f);
            if (overlap > 0f)
                position.Y -= MathF.Min(overlap, MathF.Max(0f, position.Y + GunBottom));
        }

        var handSize = p.HandSize;
        var grip = new Vector3(0, 0, handSize * 0.75f);
        var right = position + Vector3.Transform(RearGrip, turn) - grip;
        var left = position + Vector3.Transform(ForeGrip, turn) - grip;
        var handHalfHeight = handSize * 0.6f * 1.15f * (p.HandStyle == HandStyle.Big ? 1.25f : 1f);
        var sunk = -MathF.Min(position.Y + GunBottom, MathF.Min(left.Y, right.Y) - handHalfHeight);
        if (sunk > 0f)
        {
            var lift = new Vector3(0, sunk, 0);
            position += lift;
            right += lift;
            left += lift;
        }

        Hand[] hands =
        [
            new(left, handSize, p.HandStyle, true, handColor),
            new(right, handSize, p.HandStyle, false, handColor),
        ];
        return new Mushroom(stems, caps, eyes, hands, new Gun(position, rotation));
    }

    private static Cap BuildCap(Parameters p, Vector3 mount, float tilt, Vector3 color, int seed, int spotCount, Vector3 spotColor)
    {
        var radius = MathF.Max(0.05f, p.CapWidth * 0.5f);
        var height = MathF.Max(0.05f, p.CapHeight);
        var rim = MathF.Max(0.01f, p.CapRimThickness);
        var hollow = height * Math.Clamp(p.CapUndersideDepth, 0f, 0.9f);

        var rng = new Rng(seed + 31);
        var threshold = -rim + 0.45f * (height + rim);
        var maxU = threshold > 0 ? MathF.Sqrt(MathF.Max(0f, 1f - threshold * threshold / (height * height))) : 0.95f;
        var spots = new List<Spot>();
        for (var i = 0; i < spotCount; i++)
        {
            var angle = rng.Range(0f, MathF.Tau);
            var u = maxU * MathF.Sqrt(rng.Range(0f, 1f));
            var point = new Vector3(radius * u * MathF.Cos(angle), height * MathF.Sqrt(1f - u * u), radius * u * MathF.Sin(angle));
            var normal = Vector3.Normalize(new Vector3(point.X / (radius * radius), point.Y / (height * height), point.Z / (radius * radius)));
            spots.Add(new Spot(point - normal * p.SpotRadius * 0.6f, p.SpotRadius, spotColor));
        }

        return new Cap(mount, radius, height, hollow, rim, tilt, color, p.StemRamp.Light, spots);
    }

    private static void AddClusterSiblings(Parameters p, int seed, List<Stem> stems, List<Cap> caps, Vector3 capColor, Vector3 stemColor)
    {
        var rng = new Rng(seed + 900);
        var count = rng.Range(2, 4);
        for (var i = 0; i < count; i++)
        {
            var child = p.Clone();
            child.StemHeight *= rng.Range(0.45f, 0.8f);
            child.StemThickness *= rng.Range(0.6f, 0.85f);
            child.CapWidth *= rng.Range(0.55f, 0.85f);
            child.CapHeight *= rng.Range(0.7f, 0.95f);
            child.StemLean = rng.Range(8f, 22f) * (i % 2 == 0 ? 1f : -1f);
            child.Spots = false;

            var angle = (i + 0.5f) / count * MathF.Tau + rng.Symmetric(0.4f);
            var offset = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (p.StemThickness * rng.Range(1.1f, 1.9f));

            var lean = child.StemLean + MathF.Atan(child.StemCurvature) * 180 / MathF.PI;
            var stem = new Stem(offset, child.StemHeight, child.StemThickness, child.StemTaper, child.StemBulb, 1f, 1f, lean, stemColor);
            stems.Add(stem);
            caps.Add(BuildCap(child, stem.Top + child.CapOffset, child.CapTilt, capColor, seed + 300 + i * 7 + 1, 0, Bone));
        }
    }

    private static Vector3 SupportPoint(Cap cap, Vector3 direction)
    {
        var r2 = cap.Radius * cap.Radius;
        var h2 = cap.Height * cap.Height;
        var scaled = new Vector3(r2 * direction.X, h2 * direction.Y, r2 * direction.Z);
        var length = MathF.Sqrt(r2 * direction.X * direction.X + h2 * direction.Y * direction.Y + r2 * direction.Z * direction.Z);
        return cap.DomeCenter + scaled / length;
    }

    private static List<Eye> EyePair(Parameters p, Vector3 face, int seed, Vector3 lidColor)
    {
        var rng = new Rng(seed);
        var mismatch = Math.Clamp(p.EyeMismatch, 0f, 1f);
        var leftSize = p.EyeSize * (1f + rng.Symmetric(0.35f * mismatch));
        var rightSize = p.EyeSize * (1f + rng.Symmetric(0.35f * mismatch));
        var half = p.EyeSpacing * 0.5f;
        var lift = rng.Symmetric(p.EyeSize * 0.4f * mismatch);
        float leftTurn = 0, rightTurn = 0;
        if (mismatch > 0.2f)
        {
            leftTurn = rng.Range(6f, 18f);
            rightTurn = -rng.Range(6f, 18f);
        }

        return
        [
            new(face + new Vector3(-half, lift, leftSize * 0.8f), leftSize, p.EyeStyle, leftTurn, lidColor),
            new(face + new Vector3(half, -lift, rightSize * 0.8f), rightSize, p.EyeStyle, rightTurn, lidColor),
        ];
    }

    private static List<Eye> HandAssembledEyes(Parameters p, Vector3 face) =>
    [
        new(face + new Vector3(-p.EyeSpacing * 0.5f, 0, p.EyeSize * 0.8f), p.EyeSize, EyeStyle.Normal, 0, Red.Shadow),
        new(face + new Vector3(p.EyeSpacing * 0.5f, 0, p.EyeSize * 0.8f), p.EyeSize, EyeStyle.Normal, 0, Red.Shadow),
    ];

    private static SDFExampleScript Build(SDFExampleScript s, Archetype archetype)
    {
        var mushroom = Layout(archetype);
        var first = true;
        foreach (var stem in mushroom.Stems)
            EmitStem(s, stem, ref first);
        foreach (var cap in mushroom.Caps)
            EmitCap(s, cap);
        foreach (var eye in mushroom.Eyes)
            EmitEye(s, eye);
        foreach (var hand in mushroom.Hands)
            EmitHand(s, hand);
        EmitGun(s, mushroom.Gun);
        return s;
    }

    private static V3 V(Vector3 v) => new(v.X, v.Y, v.Z);

    private static SDFExampleScript SetV(this SDFExampleScript s, string parameter, Vector3 v) => s.Set(parameter, v.X, v.Y, v.Z);

    private static SDFExampleScript MoveV(this SDFExampleScript s, Vector3 v) => s.Move(v.X, v.Y, v.Z);

    private static SDFExampleScript Start(SDFExampleScript s, ISDFPartialAction add) => s.Top().Add(add);

    private static void EmitStem(SDFExampleScript s, Stem stem, ref bool first)
    {
        var lean = RotationXYZ(0, 0, -stem.LeanDegrees);
        var bottom = stem.Radius * stem.BottomScale;
        var top = stem.Radius * stem.Taper * stem.TopScale;
        if (first)
            s.Replace(new ReplaceWithCappedCone());
        else
            Start(s, new AddCappedCone());
        first = false;

        s.Set("height", stem.Height / 2).Set("bottomRadius", bottom).Set("topRadius", top)
            .MoveV(stem.Base + Vector3.Transform(new Vector3(0, stem.Height / 2, 0), lean));
        if (stem.Bulb > 0.01f)
        {
            var bulb = stem.Radius * (1f + stem.Bulb) * stem.BottomScale;
            s.Add(new AddEllipsoid()).SetV("radii", new Vector3(bulb, stem.Height * 0.2f, bulb))
                .Move(0, -stem.Height * 0.3f, 0).Outer()
                .Parent().Smooth(stem.Height * 0.1f);
        }
        if (MathF.Abs(stem.LeanDegrees) > 0.05f)
            s.Turn(0, 0, -stem.LeanDegrees);
        s.PaintRgb(V(stem.Color));
    }

    private static void EmitCap(SDFExampleScript s, Cap cap)
    {
        var (r, h, hollow, rim) = (cap.Radius, cap.Height, cap.Hollow, cap.Rim);
        Start(s, new AddEllipsoid()).SetV("radii", new Vector3(r, h, r)).MoveV(cap.DomeCenter)
            .Intersect().Replace(new ReplaceWithBox()).SetV("size", new Vector3(r * 1.05f, (h + rim) / 2, r * 1.05f))
            .Move(0, (h - rim) / 2, 0).Outer().Parent()
            .Subtract().Replace(new ReplaceWithEllipsoid()).SetV("radii", new Vector3(r * 0.9f, hollow + rim, r * 0.9f))
            .Move(0, -rim, 0).Outer().Parent()
            .PaintRgb(V(cap.Color));

        var underside = hollow + rim;
        s.Add(new AddEllipsoid()).SetV("radii", new Vector3(r * 0.87f, underside * 0.9f, r * 0.87f)).Move(0, -rim, 0)
            .Intersect().Replace(new ReplaceWithBox()).SetV("size", new Vector3(r, underside / 2, r))
            .Move(0, underside / 2, 0).Outer().Parent()
            .PaintRgb(V(cap.GillColor)).Outer();

        foreach (var spot in cap.Spots)
            s.Add(new AddSphere()).Set("radius", spot.Radius).PaintRgb(V(spot.Color)).MoveV(spot.Center).Outer();

        s.Parent();
        if (MathF.Abs(cap.Tilt) > 0.05f)
            s.Turn(0, 0, cap.Tilt);
    }

    private static void EmitEye(SDFExampleScript s, Eye eye)
    {
        var size = eye.Size;
        var shape = eye.Style switch
        {
            EyeStyle.Wide => new Vector3(0.95f, 1.15f, 0.95f),
            EyeStyle.Crazy => new Vector3(1.05f, 0.95f, 1f),
            _ => Vector3.One,
        };
        var irisScale = eye.Style switch
        {
            EyeStyle.Angry => 0.52f,
            EyeStyle.Crazy => 0.34f,
            EyeStyle.Dead => 0.6f,
            EyeStyle.Wide => 0.46f,
            _ => 0.5f,
        };

        Start(s, new AddEllipsoid()).SetV("radii", shape * size).PaintRgb(V(Sclera)).MoveV(eye.Center);

        var iris = size * irisScale;
        s.Add(new AddEllipsoid()).SetV("radii", new Vector3(iris, iris, iris * 0.55f))
            .PaintRgb(V(eye.Style == EyeStyle.Dead ? DeadIris : Iris)).Move(0, 0, size * 0.85f).Outer();

        if (eye.Style == EyeStyle.Dead)
        {
            foreach (var angle in new[] { 35f, -35f })
                s.Add(new AddBox()).SetV("size", new Vector3(size * 0.35f, size * 0.06f, size * 0.06f)).PaintRgb(V(Pupil))
                    .Turn(0, 0, angle).Move(0, 0, size * 1.1f).Outer();
        }
        else
        {
            var pupil = iris * (eye.Style == EyeStyle.Crazy ? 0.5f : 0.55f);
            s.Add(new AddEllipsoid()).SetV("radii", new Vector3(pupil, pupil, pupil * 0.5f)).PaintRgb(V(Pupil))
                .Move(0, 0, size * 1.06f).Outer();
        }

        var (coverage, tilt) = eye.Style switch
        {
            EyeStyle.Angry => (0.42f, 32f),
            EyeStyle.Dead => (0.55f, 0f),
            EyeStyle.Crazy => (0.2f, -18f),
            EyeStyle.Normal => (0.22f, 8f),
            _ => (0f, 0f),
        };
        if (coverage > 0f)
        {
            var shell = size * 1.13f;
            var cut = size * 1.06f * MathF.Cos(coverage * MathF.PI);
            s.Add(new AddSphere()).Set("radius", shell)
                .Intersect().Replace(new ReplaceWithBox()).SetV("size", new Vector3(shell * 1.1f, shell, shell * 1.1f))
                .Move(0, cut + shell, 0).Outer().Parent();
            if (MathF.Abs(tilt) > 0.05f)
                s.Turn(tilt, 0, 0);
            s.PaintRgb(V(eye.LidColor)).Outer();
        }

        if (MathF.Abs(eye.TurnY) > 0.05f)
            s.Parent().Turn(0, eye.TurnY, 0);
    }

    private static void EmitHand(SDFExampleScript s, Hand hand)
    {
        var scale = hand.Style == HandStyle.Big ? 1.25f : 1f;
        var size = hand.Size * scale;
        var mirror = hand.Left ? -1f : 1f;
        var fingerLength = hand.Size * scale * hand.Style switch
        {
            HandStyle.Claw => 1.2f,
            HandStyle.Tiny => 0.6f,
            HandStyle.Big => 0.9f,
            _ => 0.8f,
        };
        var fingerRadius = size * (hand.Style == HandStyle.Claw ? 0.15f : 0.21f);

        Start(s, new AddEllipsoid()).SetV("radii", new Vector3(0.72f, 1.15f, 0.95f) * (size * 0.6f)).MoveV(hand.Center);
        for (var i = 0; i < 3; i++)
        {
            var root = new Vector3(0, (i - 1) * fingerRadius * 2.15f, size * 0.38f);
            var tip = root + new Vector3(0, hand.Style == HandStyle.Claw ? -size * 0.18f : 0, fingerLength);
            s.Add(new AddCapsule()).SetV("a", root).SetV("b", tip).Set("radius", fingerRadius);
        }

        var thumbRoot = new Vector3(size * 0.26f * mirror, size * 0.3f, size * 0.1f);
        var thumbDirection = Vector3.Transform(Vector3.UnitY, RotationXYZ(-60f, 0f, 35f * mirror));
        s.Add(new AddCapsule()).SetV("a", thumbRoot).SetV("b", thumbRoot + thumbDirection * fingerLength * 0.7f)
            .Set("radius", fingerRadius * 1.05f)
            .Parent().PaintRgb(V(hand.Color));
    }

    private static void EmitGun(SDFExampleScript s, Gun gun)
    {
        Start(s, new AddArbitraryCappedCylinder()).Set("a", 0, 0, -0.02f).Set("b", 0, 0, 0.4f).Set("radius", 0.221f)
            .PaintRgb(V(GunMetal)).MoveV(gun.Position);

        for (var i = 0; i < 6; i++)
        {
            var angle = i / 6f * MathF.Tau;
            var (x, y) = (0.13f * MathF.Cos(angle), 0.13f * MathF.Sin(angle));
            s.Add(new AddArbitraryCappedCylinder()).Set("a", x, y, 0.35f).Set("b", x, y, 1.45f).Set("radius", 0.05f).PaintRgb(V(GunMetal));
        }

        foreach (var z in new[] { 0.35f + 1.1f * 0.97f, 0.35f + 1.1f * 0.45f })
            s.Add(new AddTorus()).Set("innerRadius", 0.0925f).Set("outerRadius", 0.1675f).PaintRgb(V(GunDark))
                .Turn(90, 0, 0).Move(0, 0, z).Outer();

        s.Add(new AddCappedCylinder()).Set("radius", 0.24f).Set("height", 0.1f).PaintRgb(V(GunDark))
            .Turn(0, 0, 90).Move(-0.3f, -0.06f, 0.06f).Outer()
            .Add(new AddBox()).Set("size", 0.11f, 0.05f, 0.07f).PaintRgb(V(GunAccent)).Move(-0.16f, 0.02f, 0.06f).Outer()
            .Add(new AddBox()).Set("size", 0.0375f, 0.13f, 0.05f).PaintRgb(V(GunAccent)).Turn(12, 0, 0).Move(0, -0.16f, -0.12f).Outer()
            .Add(new AddBox()).Set("size", 0.032f, 0.11f, 0.0425f).PaintRgb(V(GunAccent)).Turn(-8, 0, 0).Move(0, -0.15f, 0.42f).Outer()
            .Parent();

        if (gun.RotationDegrees.Length() > 0.05f)
            s.Turn(gun.RotationDegrees.X, gun.RotationDegrees.Y, gun.RotationDegrees.Z);
    }
}
