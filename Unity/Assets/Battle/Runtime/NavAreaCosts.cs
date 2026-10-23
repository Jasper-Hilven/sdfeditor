using UnityEngine;
using UnityEngine.AI;

namespace SdfBattle
{
    /// <summary>
    /// Runtime costs of navmesh areas for the bots' and tanks' path queries (ticket 28.10: a warming or hot burner ring costs 50 times as
    /// much to cross). The queries are plain <see cref="NavMesh.CalculatePath(Vector3, Vector3, NavMeshQueryFilter, NavMeshPath)"/> calls
    /// with a filter, which takes its costs from itself, not from <see cref="NavMesh.SetAreaCost"/>; so every filter is made here
    /// (<see cref="Filter"/>). The filters share one cost table, so a change shows in the next query of every one. With no cost set the
    /// filter is the plain one (all areas allowed, the project's costs).
    /// </summary>
    public static class NavAreaCosts
    {
        private static NavMeshQueryFilter table;
        private static bool made;

        /// <summary>Whether any area has a cost of its own.</summary>
        public static bool Any => made;

        /// <summary>Sets the cost of an area (an index, 0 to 31) for every filter made here; 1 is the plain cost.</summary>
        public static void Set(int area, float cost)
        {
            if (area < 0 || area >= 32)
                return;
            if (!made)
            {
                table = new NavMeshQueryFilter();
                // Everything else keeps the project's cost (the jump area's is 2)
                for (var i = 0; i < 32; i++)
                    table.SetAreaCost(i, NavMesh.GetAreaCost(i));
                made = true;
            }
            table.SetAreaCost(area, cost);
        }

        /// <summary>The cost an area has in the filters made here.</summary>
        public static float Get(int area) => made && area >= 0 && area < 32 ? table.GetAreaCost(area) : area >= 0 && area < 32 ? NavMesh.GetAreaCost(area) : 1;

        /// <summary>Back to the project's costs for every area.</summary>
        public static void Reset()
        {
            made = false;
            table = default;
        }

        /// <summary>A query filter for the agent type: all areas allowed, with the costs set here.</summary>
        public static NavMeshQueryFilter Filter(int agentType)
        {
            var filter = made ? table : new NavMeshQueryFilter();
            filter.agentTypeID = agentType;
            filter.areaMask = NavMesh.AllAreas;
            return filter;
        }
    }
}
