using System.Globalization;

namespace SDFPipeline.Meshing;

/// <summary>
/// Counts finished items against a total known up front and words each as a plain progress line, such as
/// <c>[ 37%] 12/32 meshed: castle0 (3 LODs)</c>, safe to call from the threads meshing in parallel. The lines are plain text, not
/// redrawn in place, so they read the same in a terminal and in an IDE's run console.
/// </summary>
public sealed class ProgressCounter(int total)
{
    private readonly Lock gate = new();
    private int done;

    public int Total => total;

    /// <summary>Counts one more item as finished and returns the line for it, <paramref name="what"/> being "meshed" or the like.</summary>
    public string Finish(string what, string name)
    {
        lock (gate)
            return Line(++done, total, what, name);
    }

    /// <summary>The line for the <paramref name="done"/>th of <paramref name="total"/> items, its percentage rounded down so 100% means all of them.</summary>
    public static string Line(int done, int total, string what, string name)
    {
        var percent = total <= 0 ? 100 : (int)(done * 100L / total);
        var width = total.ToString(CultureInfo.InvariantCulture).Length;
        return string.Create(CultureInfo.InvariantCulture, $"[{percent,3}%] {done.ToString(CultureInfo.InvariantCulture).PadLeft(width)}/{total} {what}: {name}");
    }
}
