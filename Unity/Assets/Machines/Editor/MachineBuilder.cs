using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;

namespace SdfMachines.Editor
{
    /// <summary>
    /// Builds the six machine prefabs (ticket 24.1), Assets/Machines/Prefabs/{tank,helicopter,turret}-{red,blue}.prefab, from the
    /// prefabs of their pieces, content/models/machines/&lt;machine&gt;-&lt;team&gt;-&lt;piece&gt;.sdf: one model per moving piece, each
    /// meshed with its origin at its pivot. Each piece is an empty pivot object, placed where the piece hinges on its parent, with the
    /// piece's prefab under it as "Model", so turning the pivot turns the piece about its own hinge. A tank reads, from the root down:
    /// <code>
    /// tank-red            MachineRig (body, turn = Turret, tilt = Barrel, the two eyes)
    ///   Body              at the root's origin (on the ground, under the middle)
    ///     Model           tank-red-body prefab, its convex collider
    ///     Turret          (0, 0.3, 0) in the body: turns about local y
    ///       Model         tank-red-turret prefab, with the eyes eye_machine.0 and eye_machine.1
    ///       Barrel        (0, 0.05, 0.13) in the turret: tilts about local x
    ///         Model       tank-red-barrel prefab
    /// </code>
    /// The helicopter: Body (eyes), Body/Rotor (0, 0.56, 0) spinning about y, Body/TailRotor (-0.025, 0.4, -0.35) spinning about x.
    /// The turret: Foot, Foot/Head (0, 1.5, 0) turning about y (eyes), Foot/Head/Barrel (0, 0, 0.3) tilting about x. The pivots are the files' and
    /// the ticket's (SDF/glTF x negated, as the meshes are: the tail rotor is on the +x side in the .sdf, on -x in Unity).
    /// <para>
    /// A machine is rebuilt when it is missing, older than one of its pieces' prefabs, or built by an older version of this code;
    /// that is checked when the editor loads and whenever <see cref="MushroomBuilder"/> built prefabs. By hand: Tools > SDF Machines >
    /// Rebuild Machines. In a batch:
    /// <c>Unity -batchmode -quit -projectPath Unity -executeMethod SdfMachines.Editor.MachineBuilder.BuildAllInBatch</c>.
    /// </para>
    /// </summary>
    public static class MachineBuilder
    {
        public const string PrefabFolder = "Assets/Machines/Prefabs";

        /// <summary>Raise when the way machines are built changes, so every machine is rebuilt when the editor loads.</summary>
        public const int BuilderVersion = 2;

        public static readonly string[] Teams = { "red", "blue" };

        /// <summary>A moving piece: its pivot object's name, the model's piece name, its pivot in the parent's space (Unity's), its children.</summary>
        public sealed class Piece
        {
            public readonly string Name, Model;
            public readonly Vector3 Pivot;
            public readonly Piece[] Children;

            public Piece(string name, string model, Vector3 pivot, params Piece[] children)
            {
                Name = name;
                Model = model;
                Pivot = pivot;
                Children = children;
            }

            public IEnumerable<Piece> All() => Children.SelectMany(c => c.All()).Prepend(this);
        }

        /// <summary>Each machine's pieces from the root: the still piece, then the one that turns (turn) and the one that tilts (tilt).</summary>
        public static readonly IReadOnlyDictionary<string, (Piece Root, string Turn, string Tilt, string Eyes)> Machines =
            new Dictionary<string, (Piece, string, string, string)>
            {
                ["tank"] = (new Piece("Body", "body", Vector3.zero,
                    new Piece("Turret", "turret", new Vector3(0, 0.3f, 0),
                        new Piece("Barrel", "barrel", new Vector3(0, 0.05f, 0.13f)))), "Turret", "Barrel", "Turret"),
                ["helicopter"] = (new Piece("Body", "body", Vector3.zero,
                    new Piece("Rotor", "rotor", new Vector3(0, 0.56f, 0)),
                    new Piece("TailRotor", "tail-rotor", new Vector3(-0.025f, 0.4f, -0.35f))), "Rotor", "TailRotor", "Body"),
                ["turret"] = (new Piece("Foot", "foot", Vector3.zero,
                    new Piece("Head", "head", new Vector3(0, 1.5f, 0),
                        new Piece("Barrel", "barrel", new Vector3(0, 0, 0.3f)))), "Head", "Barrel", "Head"),
            };

        private static string Stamp => $"sdf-machine {BuilderVersion}";

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            MushroomBuilder.Built += _ => EditorApplication.delayCall += BuildStale;
            EditorApplication.delayCall += BuildStale;
        }

        public static string ModelPrefabOf(string machine, string team, string piece) =>
            $"{MushroomBuilder.PrefabFolder}/{machine}-{team}-{piece}.prefab";

        public static string MachinePrefabOf(string machine, string team) => $"{PrefabFolder}/{machine}-{team}.prefab";

        private static IEnumerable<(string Machine, string Team)> AllMachines() =>
            Machines.Keys.SelectMany(machine => Teams.Select(team => (machine, team)));

        [MenuItem("Tools/SDF Machines/Rebuild Machines")]
        public static void BuildAll() => Build(AllMachines().ToArray());

        /// <summary>For batch mode: every machine, after the prefabs of models meshed since the last build.</summary>
        public static void BuildAllInBatch()
        {
            MushroomBuilder.BuildStale();
            BuildAll();
        }

        public static void BuildStale() => Build(AllMachines().Where(m => IsStale(m.Machine, m.Team)).ToArray());

        private static bool IsStale(string machine, string team)
        {
            var path = MachinePrefabOf(machine, team);
            if (!File.Exists(path) || AssetImporter.GetAtPath(path)?.userData != Stamp)
                return true;
            var built = File.GetLastWriteTimeUtc(path);
            return Machines[machine].Root.All().Select(p => ModelPrefabOf(machine, team, p.Model))
                .Any(source => File.Exists(source) && File.GetLastWriteTimeUtc(source) > built);
        }

        public static void Build(IReadOnlyList<(string Machine, string Team)> machines)
        {
            if (machines.Count == 0)
                return;
            Directory.CreateDirectory(PrefabFolder);
            var built = new List<string>();
            foreach (var (machine, team) in machines)
            {
                var missing = Machines[machine].Root.All().Select(p => ModelPrefabOf(machine, team, p.Model))
                    .FirstOrDefault(path => AssetDatabase.LoadAssetAtPath<GameObject>(path) == null);
                if (missing != null)
                {
                    Debug.LogWarning($"SDF machines: no prefab at {missing} yet (run sdf mesh on content/models/machines), {machine}-{team} not built");
                    continue;
                }
                try
                {
                    built.Add(BuildOne(machine, team));
                }
                catch (System.Exception exception)
                {
                    Debug.LogError($"SDF machines: could not build {machine}-{team}: {exception.Message}\n{exception.StackTrace}");
                }
            }
            AssetDatabase.SaveAssets();
            if (built.Count > 0)
                Debug.Log($"SDF machines: built {string.Join(", ", built)}");
        }

        private static string BuildOne(string machine, string team)
        {
            var (rootPiece, turn, tilt, eyesOn) = Machines[machine];
            var root = new GameObject($"{machine}-{team}");
            try
            {
                var pivots = new Dictionary<string, Transform>();
                void Add(Piece piece, Transform parent)
                {
                    var pivot = new GameObject(piece.Name).transform;
                    pivot.SetParent(parent, false);
                    pivot.localPosition = piece.Pivot;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabOf(machine, team, piece.Model));
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pivot);
                    model.name = "Model";
                    pivots[piece.Name] = pivot;
                    foreach (var child in piece.Children)
                        Add(child, pivot);
                }
                Add(rootPiece, root.transform);

                var rig = root.AddComponent<MachineRig>();
                rig.machine = machine;
                rig.team = team;
                rig.body = pivots[rootPiece.Name];
                rig.turn = pivots[turn];
                rig.tilt = pivots[tilt];
                // The eyes are the eye part's uses in the model of the piece they turn with, not under its child pieces
                var eyeModel = pivots[eyesOn].Find("Model");
                rig.eyes = new[] { "eye_machine.0", "eye_machine.1" }.Select(name => eyeModel.Find(name)).Where(eye => eye != null).ToArray();
                if (rig.eyes.Length != 2)
                    Debug.LogWarning($"SDF machines: {machine}-{team} has {rig.eyes.Length} eyes under {eyesOn}, not 2");

                var path = MachinePrefabOf(machine, team);
                PrefabUtility.SaveAsPrefabAsset(root, path, out var saved);
                if (!saved)
                    throw new System.InvalidOperationException($"Could not save {path}");
                var importer = AssetImporter.GetAtPath(path);
                if (importer != null && importer.userData != Stamp)
                {
                    importer.userData = Stamp;
                    importer.SaveAndReimport();
                }
                return path;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
