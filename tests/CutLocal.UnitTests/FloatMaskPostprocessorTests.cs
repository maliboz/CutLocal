using CutLocal.Domain;
using CutLocal.Imaging;

namespace CutLocal.UnitTests;

public sealed class FloatMaskPostprocessorTests
{
    [Fact]
    public void Normalize_SigmoidMinmax_AppliesStableActivationBeforeNormalization()
    {
        FloatMaskPostprocessor sut = new();

        using RefinedMask result = sut.Normalize(
            [-1000f, 0f, 1000f],
            width: 3,
            height: 1,
            activation: "sigmoid-minmax",
            new MaskRefinementOptions());

        Assert.Equal(0f, result.Values.Span[0], precision: 5);
        Assert.Equal(0.5f, result.Values.Span[1], precision: 5);
        Assert.Equal(1f, result.Values.Span[2], precision: 5);
    }

    [Fact]
    public void Normalize_PreservesSoftValuesAfterMinMax()
    {
        FloatMaskPostprocessor processor = new();

        using RefinedMask mask = processor.Normalize(
            [10f, 15f, 20f],
            width: 3,
            height: 1,
            new MaskRefinementOptions());

        Assert.Equal(0f, mask.Values.Span[0]);
        Assert.Equal(0.5f, mask.Values.Span[1]);
        Assert.Equal(1f, mask.Values.Span[2]);
    }

    [Fact]
    public void Normalize_SoftThresholdRecentersAlphaWithoutDestroyingSoftEdges()
    {
        FloatMaskPostprocessor processor = new();

        using RefinedMask mask = processor.Normalize(
            [0f, 0.25f, 0.5f, 0.75f, 1f],
            width: 5,
            height: 1,
            new MaskRefinementOptions { Threshold = 0.75 });

        Assert.Equal(0f, mask.Values.Span[0], precision: 5);
        Assert.Equal(1f / 6f, mask.Values.Span[1], precision: 5);
        Assert.Equal(1f / 3f, mask.Values.Span[2], precision: 5);
        Assert.Equal(0.5f, mask.Values.Span[3], precision: 5);
        Assert.Equal(1f, mask.Values.Span[4], precision: 5);
    }

    [Fact]
    public void Normalize_FeatherRadiusSoftensTheThresholdBoundarySymmetrically()
    {
        FloatMaskPostprocessor processor = new();

        using RefinedMask mask = processor.Normalize(
            [0f, 0f, 1f, 0f, 0f],
            width: 5,
            height: 1,
            activation: "minmax",
            new MaskRefinementOptions
            {
                HardCut = true,
                Threshold = 0.5,
                FeatherRadius = 1,
            },
            outputWidth: 5,
            outputHeight: 1);

        Assert.InRange(mask.Values.Span[2], 0.7f, 0.9f);
        Assert.InRange(mask.Values.Span[1], 0.05f, 0.2f);
        Assert.Equal(mask.Values.Span[1], mask.Values.Span[3], precision: 5);
        Assert.Equal(mask.Values.Span[0], mask.Values.Span[4], precision: 5);
    }

    [Fact]
    public void Normalize_HardCutAndInvertApplyAfterNormalization()
    {
        FloatMaskPostprocessor processor = new();

        using RefinedMask mask = processor.Normalize(
            [0f, 0.75f, 1f],
            width: 3,
            height: 1,
            new MaskRefinementOptions { HardCut = true, Invert = true, Threshold = 0.5 });

        Assert.Equal([1f, 0f, 0f], mask.Values.Span.ToArray());
    }
}
