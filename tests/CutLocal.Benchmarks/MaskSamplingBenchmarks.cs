using BenchmarkDotNet.Attributes;
using CutLocal.Imaging;

namespace CutLocal.Benchmarks;

[MemoryDiagnoser]
public class MaskSamplingBenchmarks
{
    private readonly float[] _mask = Enumerable.Range(0, 320 * 320)
        .Select(index => (float)(index % 320) / 319)
        .ToArray();

    [Benchmark]
    public float SampleMaskIntoFullHdScanline()
    {
        float sum = 0;
        for (int x = 0; x < 1920; x++)
        {
            sum += BilinearAlphaCompositor.SampleBilinear(
                _mask,
                320,
                320,
                x,
                outputY: 540,
                outputWidth: 1920,
                outputHeight: 1080);
        }

        return sum;
    }
}
