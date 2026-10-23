namespace SDFPipeline.Gpu;

/// <summary>Notes about the GPU path (such as a model it fell back from) printed once each, to the error stream.</summary>
internal static class GpuNotes
{
    private static readonly HashSet<string> told = [];

    public static void Once(string note)
    {
        lock (told)
            if (told.Add(note))
                Console.Error.WriteLine(note);
    }
}
