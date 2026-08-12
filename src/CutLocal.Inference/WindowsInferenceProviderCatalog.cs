using System.ComponentModel;
using System.Runtime.InteropServices;
using CutLocal.Contracts;
using CutLocal.Domain;

namespace CutLocal.Inference;

/// <summary>Discovers CPU and DirectX 12 adapters without mutating the machine.</summary>
public sealed class WindowsInferenceProviderCatalog : IInferenceProviderCatalog
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint DxgiAdapterFlagSoftware = 2;
    private const int D3dFeatureLevel11 = 0xB000;
    private static readonly Guid DxgiFactory1 = new("770AAE78-F26F-4DBA-A829-253C83D1B387");
    private static readonly Guid D3d12Device = new("189819F1-1DB6-4B57-BE54-1821339B85F7");

    /// <summary>Gets the bundled CPU descriptor used by all provider policies.</summary>
    public static InferenceProviderDescriptor Cpu { get; } = new()
    {
        Kind = InferenceProviderKind.Cpu,
        Id = "cpu",
        DisplayName = "ONNX Runtime CPU",
        IsReadyOffline = true,
        MaxRecommendedConcurrency = 2,
        DeviceIdentity = "cpu",
    };

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<InferenceProviderDescriptor>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<InferenceProviderDescriptor> providers = [.. EnumerateDirectMlAdapters(), Cpu];
        return ValueTask.FromResult<IReadOnlyList<InferenceProviderDescriptor>>(providers);
    }

    private static unsafe List<InferenceProviderDescriptor> EnumerateDirectMlAdapters()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            return [];
        }

        nint factory = 0;
        List<InferenceProviderDescriptor> adapters = [];
        try
        {
            int createResult = CreateDXGIFactory1(in DxgiFactory1, out factory);
            if (createResult < 0 || factory == 0)
            {
                return [];
            }

            nint* factoryVtable = *(nint**)factory;
            delegate* unmanaged[Stdcall]<nint, uint, nint*, int> enumAdapters =
                (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)factoryVtable[12];

            for (uint index = 0; ; index++)
            {
                nint adapter = 0;
                int enumResult = enumAdapters(factory, index, &adapter);
                if (enumResult == DxgiErrorNotFound)
                {
                    break;
                }

                if (enumResult < 0)
                {
                    throw new Win32Exception(enumResult, "DXGI adapter enumeration failed.");
                }

                try
                {
                    DxgiAdapterDesc1 description = default;
                    nint* adapterVtable = *(nint**)adapter;
                    delegate* unmanaged[Stdcall]<nint, DxgiAdapterDesc1*, int> getDescription =
                        (delegate* unmanaged[Stdcall]<nint, DxgiAdapterDesc1*, int>)adapterVtable[10];
                    int descriptionResult = getDescription(adapter, &description);
                    if (descriptionResult < 0
                        || (description.Flags & DxgiAdapterFlagSoftware) != 0
                        || !SupportsDirectX12(adapter))
                    {
                        continue;
                    }

                    string name;
                    char* text = description.Description;
                    name = new string(text).TrimEnd('\0').Trim();

                    string identity = string.Create(
                        provider: null,
                        $"luid:{description.AdapterLuidHigh:X8}{description.AdapterLuidLow:X8}");
                    adapters.Add(new InferenceProviderDescriptor
                    {
                        Kind = InferenceProviderKind.DirectMl,
                        Id = $"directml:{identity}",
                        DisplayName = string.IsNullOrWhiteSpace(name) ? $"DirectML GPU {index}" : name,
                        IsReadyOffline = true,
                        MaxRecommendedConcurrency = 1,
                        DeviceIndex = checked((int)index),
                        DeviceIdentity = identity,
                        DedicatedVideoMemoryBytes = checked((long)description.DedicatedVideoMemory),
                    });
                }
                finally
                {
                    if (adapter != 0)
                    {
                        Marshal.Release(adapter);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or Win32Exception)
        {
            return [];
        }
        finally
        {
            if (factory != 0)
            {
                Marshal.Release(factory);
            }
        }

        return adapters;
    }

    private static bool SupportsDirectX12(nint adapter)
    {
        nint device = 0;
        try
        {
            int result = D3D12CreateDevice(adapter, D3dFeatureLevel11, in D3d12Device, out device);
            return result >= 0 && device != 0;
        }
        finally
        {
            if (device != 0)
            {
                Marshal.Release(device);
            }
        }
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(in Guid riid, out nint factory);

    [DllImport("d3d12.dll", ExactSpelling = true)]
    private static extern int D3D12CreateDevice(
        nint adapter,
        int minimumFeatureLevel,
        in Guid riid,
        out nint device);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct DxgiAdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSystemId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }
}
