using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SDFPipeline.Gpu;

/// <summary>A GPU buffer, released when disposed.</summary>
public sealed unsafe class GpuBuffer : IDisposable
{
    internal GpuBuffer(GpuContext context, WgpuBuffer* handle, long size)
    {
        Context = context;
        Handle = handle;
        Size = size;
    }

    internal GpuContext Context { get; }
    internal WgpuBuffer* Handle { get; private set; }
    public long Size { get; }

    public void Dispose()
    {
        if (Handle is null)
            return;
        lock (Context.Gate)
            Context.Api.BufferRelease(Handle);
        Handle = null;
    }
}

/// <summary>A compute shader compiled for the GPU, with <see cref="StorageBuffers"/> storage buffers at bindings 1.. and the uniform block at 0.</summary>
public sealed unsafe class GpuKernel
{
    internal GpuKernel(ComputePipeline* pipeline, BindGroupLayout* layout, int storageBuffers)
    {
        Pipeline = pipeline;
        Layout = layout;
        StorageBuffers = storageBuffers;
    }

    internal ComputePipeline* Pipeline { get; }
    internal BindGroupLayout* Layout { get; }
    public int StorageBuffers { get; }
}

public sealed class GpuException(string message) : Exception(message);

/// <summary>
/// The GPU the pipeline computes on, through WebGPU over wgpu-native (Metal on a Mac): a device, its queue, and the few operations the
/// SDF evaluators need: compile a GLSL compute shader, put data on the GPU, run a kernel over a range of items, read the results back.
/// There is one for the process, found on first use; <see cref="Shared"/> is null when there is no GPU or it was turned off with the
/// environment variable <c>SDF_NO_GPU</c>. All calls are serialised on <see cref="Gate"/>: the device has one queue.
/// </summary>
public sealed unsafe class GpuContext
{
    /// <summary>Items per workgroup in every kernel (the shaders declare the same).</summary>
    public const int WorkgroupSize = 64;

    /// <summary>The most items one dispatch covers (65,535 workgroups in one dimension).</summary>
    public const int MaxItemsPerDispatch = 65535 * WorkgroupSize;

    private static readonly Lazy<GpuContext?> shared = new(TryCreate);

    /// <summary>The process's GPU context, or null when there is no usable GPU (or SDF_NO_GPU is set).</summary>
    public static GpuContext? Shared => shared.Value;

    /// <summary>Why <see cref="Shared"/> is null, if it is.</summary>
    public static string? WhyNot { get; private set; }

    internal readonly object Gate = new();
    internal WebGPU Api { get; }
    private readonly Wgpu extension;
    private readonly Instance* instance;
    private readonly Adapter* adapter;
    private readonly Device* device;
    private readonly Queue* queue;

    /// <summary>Errors the device reported since the last time they were taken, such as a shader that did not compile.</summary>
    private readonly List<string> errors = [];

    private static readonly PfnErrorCallback errorCallback = new(OnError);
    private static GpuContext? current;

    public string AdapterName { get; }
    public string BackendName { get; }

    private GpuContext(WebGPU api, Wgpu extension, Instance* instance, Adapter* adapter, Device* device, string name, string backend)
    {
        Api = api;
        this.extension = extension;
        this.instance = instance;
        this.adapter = adapter;
        this.device = device;
        queue = api.DeviceGetQueue(device);
        AdapterName = name;
        BackendName = backend;
    }

    private static void OnError(ErrorType type, byte* message, void* user)
    {
        var text = $"{type}: {Marshal.PtrToStringUTF8((nint)message)}";
        if (current is { } context)
            lock (context.errors)
                context.errors.Add(text);
        else
            Console.Error.WriteLine($"GPU error: {text}");
    }

    private static GpuContext? TryCreate()
    {
        if (Environment.GetEnvironmentVariable("SDF_NO_GPU") is { Length: > 0 })
        {
            WhyNot = "SDF_NO_GPU is set";
            return null;
        }
        try
        {
            var api = WebGPU.GetApi();
            if (!api.TryGetDeviceExtension<Wgpu>(null, out var extension))
            {
                WhyNot = "the wgpu extension is missing";
                return null;
            }
            var instanceDescriptor = new InstanceDescriptor();
            var instance = api.CreateInstance(&instanceDescriptor);
            Adapter* adapter = null;
            var options = new RequestAdapterOptions { PowerPreference = PowerPreference.HighPerformance };
            api.InstanceRequestAdapter(instance, &options, new PfnRequestAdapterCallback((status, found, message, user) => adapter = found), null);
            if (adapter is null)
            {
                WhyNot = "no GPU adapter found";
                return null;
            }

            AdapterProperties properties;
            api.AdapterGetProperties(adapter, &properties);
            var name = Marshal.PtrToStringUTF8((nint)properties.Name) ?? "GPU";

            // The adapter's own limits, so large buffers are possible
            SupportedLimits supported;
            api.AdapterGetLimits(adapter, &supported);
            var required = new RequiredLimits { Limits = supported.Limits };
            Device* device = null;
            var descriptor = new DeviceDescriptor { RequiredLimits = &required };
            api.AdapterRequestDevice(adapter, &descriptor, new PfnRequestDeviceCallback((status, created, message, user) => device = created), null);
            if (device is null)
            {
                WhyNot = "the GPU adapter gave no device";
                return null;
            }
            api.DeviceSetUncapturedErrorCallback(device, errorCallback, null);
            var context = new GpuContext(api, extension, instance, adapter, device, name, properties.BackendType.ToString());
            current = context;
            return context;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                                              or InvalidOperationException or NotSupportedException)
        {
            WhyNot = exception.Message;
            return null;
        }
    }

    /// <summary>Returns and clears the errors the device reported.</summary>
    internal string? TakeErrors()
    {
        lock (errors)
        {
            if (errors.Count == 0)
                return null;
            var text = string.Join("\n", errors);
            errors.Clear();
            return text;
        }
    }

    /// <summary>Compiles a GLSL 450 compute shader (its entry point is <c>main</c>); throws <see cref="GpuException"/> if it does not compile.</summary>
    public GpuKernel Compile(string glsl, int storageBuffers, string label)
    {
        lock (Gate)
        {
            TakeErrors();
            var code = SilkMarshal.StringToPtr(glsl);
            var entry = SilkMarshal.StringToPtr("main");
            try
            {
                var glslDescriptor = new ShaderModuleGLSLDescriptor
                {
                    Chain = new ChainedStruct { SType = (SType)NativeSType.STypeShaderModuleGlsldescriptor },
                    Stage = ShaderStage.Compute,
                    Code = (byte*)code,
                    DefineCount = 0,
                };
                var moduleDescriptor = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&glslDescriptor };
                var module = Api.DeviceCreateShaderModule(device, &moduleDescriptor);
                var moduleError = TakeErrors();
                if (moduleError is not null || module is null)
                    throw new GpuException($"{label}: shader module failed: {moduleError}");
                var pipelineDescriptor = new ComputePipelineDescriptor
                {
                    Compute = new ProgrammableStageDescriptor { Module = module, EntryPoint = (byte*)entry },
                };
                var pipeline = Api.DeviceCreateComputePipeline(device, &pipelineDescriptor);
                var pipelineError = TakeErrors();
                if (pipelineError is not null || pipeline is null)
                    throw new GpuException($"{label}: pipeline failed: {pipelineError}");
                Api.ShaderModuleRelease(module);
                return new GpuKernel(pipeline, Api.ComputePipelineGetBindGroupLayout(pipeline, 0), storageBuffers);
            }
            finally
            {
                SilkMarshal.Free(code);
                SilkMarshal.Free(entry);
            }
        }
    }

    /// <summary>A buffer on the GPU the kernels read and write, with the given contents (or empty, which the GPU zeroes).</summary>
    public GpuBuffer Storage<T>(ReadOnlySpan<T> data, long minimumBytes = 0) where T : unmanaged
    {
        var bytes = Math.Max(Math.Max(data.Length * (long)sizeof(T), minimumBytes), 16);
        lock (Gate)
        {
            var descriptor = new BufferDescriptor { Size = (ulong)Align(bytes), Usage = BufferUsage.Storage | BufferUsage.CopyDst | BufferUsage.CopySrc };
            var buffer = Api.DeviceCreateBuffer(device, &descriptor);
            if (data.Length > 0)
                fixed (T* pointer = data)
                    Api.QueueWriteBuffer(queue, buffer, 0, pointer, (nuint)Align(data.Length * (long)sizeof(T)));
            return new GpuBuffer(this, buffer, Align(bytes));
        }
    }

    /// <summary>An empty storage buffer for <paramref name="count"/> values of <typeparamref name="T"/>.</summary>
    public GpuBuffer Output<T>(long count) where T : unmanaged => Storage(ReadOnlySpan<T>.Empty, count * sizeof(T));

    private static long Align(long bytes) => (bytes + 3) & ~3L;

    /// <summary>
    /// Runs the kernel over <paramref name="items"/> items. The uniform block holds 4 floats vectors and 4 integer vectors
    /// (<c>vec4 uf[4]; ivec4 ui[4]</c>, 128 bytes), the storage buffers follow at bindings 1 to n.
    /// </summary>
    public void Run(GpuKernel kernel, ReadOnlySpan<float> floats, ReadOnlySpan<int> ints, int items, params GpuBuffer[] buffers)
    {
        if (buffers.Length != kernel.StorageBuffers)
            throw new ArgumentException($"The kernel takes {kernel.StorageBuffers} storage buffers, not {buffers.Length}");
        if (items <= 0)
            return;
        if (items > MaxItemsPerDispatch)
            throw new ArgumentOutOfRangeException(nameof(items), $"A dispatch covers {MaxItemsPerDispatch} items at most");
        Span<byte> uniform = stackalloc byte[128];
        uniform.Clear();
        MemoryMarshal.AsBytes(floats[..Math.Min(floats.Length, 16)]).CopyTo(uniform);
        MemoryMarshal.AsBytes(ints[..Math.Min(ints.Length, 16)]).CopyTo(uniform[64..]);

        lock (Gate)
        {
            var uniformDescriptor = new BufferDescriptor { Size = 128, Usage = BufferUsage.Uniform | BufferUsage.CopyDst };
            var uniformBuffer = Api.DeviceCreateBuffer(device, &uniformDescriptor);
            fixed (byte* bytes = uniform)
                Api.QueueWriteBuffer(queue, uniformBuffer, 0, bytes, 128);

            var entries = stackalloc BindGroupEntry[buffers.Length + 1];
            entries[0] = new BindGroupEntry { Binding = 0, Buffer = uniformBuffer, Size = 128 };
            for (var b = 0; b < buffers.Length; b++)
                entries[b + 1] = new BindGroupEntry { Binding = (uint)(b + 1), Buffer = buffers[b].Handle, Size = (ulong)buffers[b].Size };
            var groupDescriptor = new BindGroupDescriptor { Layout = kernel.Layout, EntryCount = (nuint)(buffers.Length + 1), Entries = entries };
            var group = Api.DeviceCreateBindGroup(device, &groupDescriptor);

            var encoder = Api.DeviceCreateCommandEncoder(device, new CommandEncoderDescriptor());
            var pass = Api.CommandEncoderBeginComputePass(encoder, new ComputePassDescriptor());
            Api.ComputePassEncoderSetPipeline(pass, kernel.Pipeline);
            Api.ComputePassEncoderSetBindGroup(pass, 0, group, 0, null);
            Api.ComputePassEncoderDispatchWorkgroups(pass, (uint)((items + WorkgroupSize - 1) / WorkgroupSize), 1, 1);
            Api.ComputePassEncoderEnd(pass);
            var commands = Api.CommandEncoderFinish(encoder, new CommandBufferDescriptor());
            Api.QueueSubmit(queue, 1, &commands);

            Api.CommandBufferRelease(commands);
            Api.ComputePassEncoderRelease(pass);
            Api.CommandEncoderRelease(encoder);
            Api.BindGroupRelease(group);
            Api.BufferRelease(uniformBuffer);
            if (TakeErrors() is { } error)
                throw new GpuException($"Dispatch failed: {error}");
        }
    }

    /// <summary>Reads <paramref name="count"/> values back from a buffer, waiting for the GPU to finish what it was given.</summary>
    public T[] Read<T>(GpuBuffer buffer, int count) where T : unmanaged
    {
        var result = new T[count];
        if (count == 0)
            return result;
        var bytes = Align(count * (long)sizeof(T));
        lock (Gate)
        {
            var descriptor = new BufferDescriptor { Size = (ulong)bytes, Usage = BufferUsage.MapRead | BufferUsage.CopyDst };
            var staging = Api.DeviceCreateBuffer(device, &descriptor);
            var encoder = Api.DeviceCreateCommandEncoder(device, new CommandEncoderDescriptor());
            Api.CommandEncoderCopyBufferToBuffer(encoder, buffer.Handle, 0, staging, 0, (ulong)bytes);
            var commands = Api.CommandEncoderFinish(encoder, new CommandBufferDescriptor());
            Api.QueueSubmit(queue, 1, &commands);
            Api.CommandBufferRelease(commands);
            Api.CommandEncoderRelease(encoder);

            var done = false;
            var status = BufferMapAsyncStatus.Unknown;
            Api.BufferMapAsync(staging, MapMode.Read, 0, (nuint)bytes, new PfnBufferMapCallback((s, user) =>
            {
                status = s;
                done = true;
            }), null);
            while (!done)
                extension.DevicePoll(device, true, null);
            if (status != BufferMapAsyncStatus.Success)
                throw new GpuException($"Reading the GPU buffer failed: {status}");
            var mapped = Api.BufferGetConstMappedRange(staging, 0, (nuint)bytes);
            new ReadOnlySpan<T>(mapped, count).CopyTo(result);
            Api.BufferUnmap(staging);
            Api.BufferRelease(staging);
            if (TakeErrors() is { } error)
                throw new GpuException($"Reading failed: {error}");
        }
        return result;
    }
}
