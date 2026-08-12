using CutLocal.Contracts;
using CutLocal.Domain;

namespace CutLocal.Application;

/// <summary>Validates and runs a bounded local hardware benchmark.</summary>
public sealed class BenchmarkHardwareUseCase
{
    private readonly IHardwareBenchmarkService _service;

    /// <summary>Initializes the benchmark use case.</summary>
    public BenchmarkHardwareUseCase(IHardwareBenchmarkService service)
    {
        _service = service;
    }

    /// <summary>Measures warmed inference for the selected offline provider policy.</summary>
    public ValueTask<IReadOnlyList<BenchmarkResult>> ExecuteAsync(
        HardwareBenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Iterations is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Iterations,
                "Hardware benchmark iterations must be between 1 and 50.");
        }

        return _service.RunAsync(request, cancellationToken);
    }
}
