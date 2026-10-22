using System;

namespace SdfBattle
{
    /// <summary>How one named part of a model turns (ticket 30.5): about which axis, how fast and from which angle. The axis is in the model's own space.</summary>
    public readonly struct SpinRule
    {
        public readonly float axisX, axisY, axisZ;
        public readonly float degreesPerSecond;
        public readonly float phaseDegrees;

        public SpinRule(float axisX, float axisY, float axisZ, float degreesPerSecond, float phaseDegrees = 0)
        {
            this.axisX = axisX;
            this.axisY = axisY;
            this.axisZ = axisZ;
            this.degreesPerSecond = degreesPerSecond;
            this.phaseDegrees = phaseDegrees;
        }
    }

    /// <summary>
    /// The naming rule for parts that spin on their own, with no physics (ticket 30.5): a part of a model that is named <c>Blades</c> (a case fan,
    /// turning about its z axis, the way the fan faces), <c>FanWest</c> and <c>FanEast</c> (the cooler's fans, about x) or <c>Fan0</c> to
    /// <c>Fan2</c> (a graphics card's fans, about z) is turned by a <c>Spinner</c>, slowly. The meshes of the coarser levels of detail of a part
    /// are named <c>Blades_LOD1</c> and so on and turn with it. The angle is a function of the clock alone, so the levels of detail of one part always
    /// agree. Plain C#, so the rule is tested without a scene.
    /// </summary>
    public static class SpinRules
    {
        private const string LodMarker = "_LOD";

        /// <summary>The part's name without the suffix a coarser level of detail adds ("Blades_LOD2" is "Blades").</summary>
        public static string PartName(string objectName)
        {
            var at = objectName.LastIndexOf(LodMarker, StringComparison.Ordinal);
            if (at < 0 || at + LodMarker.Length >= objectName.Length)
                return objectName;
            for (var i = at + LodMarker.Length; i < objectName.Length; i++)
                if (objectName[i] < '0' || objectName[i] > '9')
                    return objectName;
            return objectName.Substring(0, at);
        }

        /// <summary>How the object of this name spins; false when it is not one of the spinning parts.</summary>
        public static bool TryFind(string objectName, out SpinRule rule)
        {
            switch (PartName(objectName))
            {
                case "Blades": rule = new SpinRule(0, 0, 1, 45); return true;
                case "FanWest": rule = new SpinRule(1, 0, 0, 60); return true;
                case "FanEast": rule = new SpinRule(1, 0, 0, 60, 25); return true;
                case "Fan0": rule = new SpinRule(0, 0, 1, 50); return true;
                case "Fan1": rule = new SpinRule(0, 0, 1, 55, 10); return true;
                case "Fan2": rule = new SpinRule(0, 0, 1, 60, 20); return true;
                default: rule = default; return false;
            }
        }

        /// <summary>The angle in degrees, 0 up to 360, the part is turned by at this time: a steady turn from its phase.</summary>
        public static float Angle(SpinRule rule, float seconds)
        {
            var angle = (rule.phaseDegrees + rule.degreesPerSecond * seconds) % 360f;
            return angle < 0 ? angle + 360f : angle;
        }
    }
}
