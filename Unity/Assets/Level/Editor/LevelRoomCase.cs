using System;
using System.IO;
using Newtonsoft.Json.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;

namespace SdfLevel.Editor
{
    /// <summary>
    /// The computer case room look (ticket 30.2): steel walls in panels with honeycomb vents on two of them, and a smoked glass lid with a
    /// reflection streak and a warm glow patch, drawn by SdfRoomCase.shader. <see cref="LevelRoom.Looks"/> gives a material name a
    /// <see cref="CaseLook"/> to use this shader in place of the planks one; this file builds the face material from it.
    /// </summary>
    public static class LevelRoomCase
    {
        public const string ShaderPath = "Assets/Level/Shaders/SdfRoomCase.shader";

        /// <summary>What SdfRoomCase.shader draws on a face (see the shader's top); anything with a zero size is left out.</summary>
        public sealed class CaseLook
        {
            /// <summary>The steel panels' width and height in metres, their tone step (fraction either way) and the seams' half width.</summary>
            public Vector4 Panels = new Vector4(16, 16, 0.06f, 0.08f);
            /// <summary>The vents' rectangle: bottom and top (metres up), start and end as fractions of the face's length along u; none if top is not above bottom.</summary>
            public Vector4 Vent;
            /// <summary>The faces that have the vents.</summary>
            public string[] VentFaces = Array.Empty<string>();
            /// <summary>The vents' hexagon width and frame width in metres.</summary>
            public Vector2 VentCell = new Vector2(4, 0.6f);
            /// <summary>Whether this is the glass lid, the frame's width, and the reflection streak's place along the diagonal and its width (metres).</summary>
            public bool Lid;
            public float Frame = 1.2f;
            public Vector2 Streak = new Vector2(0, 8);
            /// <summary>The glow patch: the fractions of the face it is centred on, its diameter in metres and its strength; its colour (sRGB).</summary>
            public Vector4 Patch;
            public Color PatchColor = Color.white;
        }

        /// <summary>The face's material on SdfRoomCase.shader: the ground's toon settings, the face's place and size, its look and its paint.</summary>
        public static Material Material(string name, LevelRoom.Face face, string materialName, CaseLook look, JObject paint, float lightLean)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath) ?? throw new InvalidOperationException($"The shader {ShaderPath} is missing");
            var parameters = new SdfGlb.Material { Name = materialName, Parameters = (JObject)paint["parameters"] };

            var path = $"{LevelBuilder.MaterialFolder}/{name} Room {face.Name.Replace("+", "pos").Replace("-", "neg")}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                MushroomBuilder.EnsureFolder(LevelBuilder.MaterialFolder);
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            MushroomStyle.Load().ApplyToSurface(material);
            material.SetVector("_ToonRim", Vector4.zero);
            material.SetVector("_RoomOrigin", face.Origin);
            material.SetVector("_RoomU", face.U);
            material.SetVector("_RoomV", face.V);
            material.SetVector("_RoomExtent", new Vector4(face.Size.x, face.Size.y, 1, lightLean));
            material.SetVector("_MetalBase", LevelBuilder.Color(parameters, "baseColor", 0));
            material.SetVector("_MetalVariation", LevelBuilder.Color(parameters, "variationColor", parameters.Number("variationScale")));
            material.SetVector("_MetalSurface", LevelBuilder.Surface(parameters));
            material.SetVector("_MetalSpeckle", LevelBuilder.Color(parameters, "speckleColor", parameters.Number("speckleScale")));
            material.SetVector("_MetalPattern", new Vector4(parameters.Number("speckleDensity"), parameters.Number("speckleSize"), 0, 0));
            material.SetVector("_MetalEdge", LevelBuilder.Color(parameters, "edgeColor", 0));
            material.SetVector("_Panels", new Vector4(look.Panels.x, look.Panels.y, look.Panels.z, Mathf.Max(look.Panels.w, parameters.Number("edgeThickness") / 2)));

            var vents = Array.IndexOf(look.VentFaces, face.Name) >= 0 && look.Vent.y > look.Vent.x;
            material.SetVector("_Vent", vents ? new Vector4(look.Vent.x, look.Vent.y, look.Vent.z * face.Size.x, look.Vent.w * face.Size.x) : Vector4.zero);
            material.SetVector("_VentCell", new Vector4(look.VentCell.x, look.VentCell.y, 0, 0));
            material.SetVector("_Lid", new Vector4(look.Lid ? 1 : 0, look.Frame, look.Streak.x, look.Streak.y));
            material.SetVector("_Patch", look.Patch);
            material.SetVector("_PatchColor", look.PatchColor.linear);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
