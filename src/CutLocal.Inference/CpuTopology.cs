using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CutLocal.Inference;

/// <summary>Reads physical core topology while retaining a conservative fallback.</summary>
public static class CpuTopology
{
    private const int RelationProcessorCore = 0;
    private const int ErrorInsufficientBuffer = 122;

    /// <summary>Gets the active physical-core count visible to the process.</summary>
    public static int GetPhysicalCoreCount()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Math.Max(1, Environment.ProcessorCount);
        }

        try
        {
            uint length = 0;
            if (GetLogicalProcessorInformationEx(RelationProcessorCore, nint.Zero, ref length)
                || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer
                || length < 8)
            {
                return Math.Max(1, Environment.ProcessorCount);
            }

            nint buffer = Marshal.AllocHGlobal(checked((int)length));
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                int count = 0;
                int offset = 0;
                while (offset <= length - 8)
                {
                    int relationship = Marshal.ReadInt32(buffer, offset);
                    int recordSize = Marshal.ReadInt32(buffer, offset + 4);
                    if (recordSize < 8 || offset > length - recordSize)
                    {
                        break;
                    }

                    if (relationship == RelationProcessorCore)
                    {
                        count++;
                    }

                    offset += recordSize;
                }

                return Math.Max(1, count);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception exception) when (exception is Win32Exception
            or OverflowException
            or DllNotFoundException
            or EntryPointNotFoundException)
        {
            return Math.Max(1, Environment.ProcessorCount);
        }
    }

    /// <summary>Gets a UI-friendly CPU inference thread count that reserves one physical core.</summary>
    public static int GetRecommendedInferenceThreadCount() =>
        Math.Max(1, GetPhysicalCoreCount() - 1);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(
        int relationshipType,
        nint buffer,
        ref uint returnedLength);
}
