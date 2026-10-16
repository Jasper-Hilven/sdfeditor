namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public sealed record SDFPartialHistoryGraph(int[] Lanes, (int Parent, int Child)[] Edges)
{
    public int LaneCount => Lanes.Length == 0 ? 0 : Lanes.Max() + 1;

    public static int Row(int? entry) => entry is { } index ? index + 1 : 0;

    public static SDFPartialHistoryGraph Of(SDFPartialActionHistory history)
    {
        var rows = history.Entries.Count + 1;
        var lanes = new int[rows];
        var edges = history.Entries.Select((entry, i) => (Row(entry.Parent), Row(i))).ToArray();

        var chains = new List<List<int>>();
        var chainOf = new int[rows];
        chains.Add([0]);
        for (var i = 0; i < history.Entries.Count; i++)
        {
            var parent = history.Entries[i].Parent;
            var row = Row(i);
            if (history.Children(parent)[0] == i)
            {
                chainOf[row] = chainOf[Row(parent)];
                chains[chainOf[row]].Add(row);
            }
            else
            {
                chainOf[row] = chains.Count;
                chains.Add([row]);
            }
        }

        var occupied = new List<(int Lane, int From, int To)>();
        for (var c = 0; c < chains.Count; c++)
        {
            var chain = chains[c];
            var from = c == 0 ? 0 : Row(history.Entries[chain[0] - 1].Parent);
            var to = chain[^1];
            var lane = 0;
            while (occupied.Any(o => o.Lane == lane && o.From <= to && from <= o.To))
                lane++;
            occupied.Add((lane, from, to));
            foreach (var row in chain)
                lanes[row] = lane;
        }

        return new SDFPartialHistoryGraph(lanes, edges);
    }
}
