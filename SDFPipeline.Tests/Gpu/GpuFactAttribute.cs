using SDFPipeline.Gpu;

namespace SDFPipeline.Tests.Gpu;

/// <summary>A test that needs a GPU: skipped, with the reason, where there is none (a CI machine).</summary>
public sealed class GpuFactAttribute : FactAttribute
{
    public GpuFactAttribute()
    {
        if (GpuContext.Shared is null)
            Skip = $"no usable GPU: {GpuContext.WhyNot}";
    }
}
