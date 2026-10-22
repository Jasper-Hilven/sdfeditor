using SdfBattle;
using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// A train on a closed path and a fixed timetable (ticket 27.3, the level file's <c>mover</c>): every frame it places each car where
    /// <see cref="PathTimetable"/> puts it for the round clock (<see cref="Clock"/>), so a car's place is a function of the clock alone and
    /// a respawn or a new round saves nothing. The cars are kinematic bodies with convex colliders, so the physics sees them move and a
    /// player standing on one is carried (<see cref="FirstPersonController"/>, through <see cref="PathMoverCar.Carry"/>).
    /// <para>
    /// The path, heights and car lengths are in Unity's coordinates (the level builder mirrors x as it does for placements); the cars are
    /// children of this object, model forward +z, origin at the middle of the underside, in train order.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class PathMover : MonoBehaviour
    {
        [Tooltip("The path's corners (x, z) in metres, closed by a leg back to the first")]
        public Vector2[] path;
        [Tooltip("(arc length, y) keys of the cars' height along the path; none for flat")]
        public Vector2[] heights;
        [Tooltip("Each car's length along the path, the first car ahead")]
        public float[] carLengths;
        public float speed = 7;
        [Tooltip("Metres between a car's rear and the next car's front")]
        public float gap = 0.5f;
        [Tooltip("Seconds the timetable is ahead at clock 0")]
        public float phase;

        private PathTimetable timetable;
        private PathMoverCar[] cars;

        /// <summary>The cars, the children of this object that are <see cref="PathMoverCar"/>s, in train order (the first ahead).</summary>
        public PathMoverCar[] Cars
        {
            get
            {
                if (cars == null || cars.Length != transform.childCount)
                {
                    var found = new System.Collections.Generic.List<PathMoverCar>();
                    foreach (Transform child in transform)
                        if (child.TryGetComponent<PathMoverCar>(out var car))
                            found.Add(car);
                    cars = found.ToArray();
                }
                return cars;
            }
        }

        /// <summary>The timetable of this mover; built on first use.</summary>
        public PathTimetable Timetable => timetable ??= new PathTimetable(new ClosedPath(path, heights), speed, phase, gap, carLengths);

        /// <summary>
        /// The round clock in seconds: the match's (<see cref="MatchState.RoundClock"/>, from 0 at each round's start) while one runs,
        /// else <see cref="Time.time"/>.
        /// </summary>
        public static float Clock
        {
            get
            {
                var state = Match.Current != null ? Match.Current.State : null;
                return state != null && state.Started ? state.RoundClock : Time.time;
            }
        }

        private void OnEnable()
        {
            timetable = null;
            cars = null;
        }

        private void Update() => Place(Clock);

        /// <summary>Puts every car where the timetable has it at <paramref name="clock"/>, remembering the step each took.</summary>
        public void Place(float clock)
        {
            if (path == null || carLengths == null)
                return;
            var moved = false;
            var cars = Cars;
            for (var i = 0; i < cars.Length && i < Timetable.CarCount; i++)
            {
                cars[i].MoveTo(Timetable.PoseOf(i, clock));
                moved = true;
            }
            // The colliders have to be where the cars are before anything this frame moves (the player's controller)
            if (moved)
                Physics.SyncTransforms();
        }
    }
}
