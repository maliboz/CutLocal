using CutLocal.Application;

namespace CutLocal.UnitTests;

public sealed class OutputPathPolicyTests
{
    [Fact]
    public void CreateSiblingPngPath_PreservesUnicodeDirectoryAndAddsSuffix()
    {
        string input = Path.Combine(Path.GetTempPath(), "İstanbul", "ürün.png");

        string output = OutputPathPolicy.CreateSiblingPngPath(input);

        Assert.Equal(Path.Combine(Path.GetTempPath(), "İstanbul", "ürün.cutlocal.png"), output);
    }

    [Fact]
    public void CreatePngPath_UsesSelectedDirectoryAndNormalizesSuffix()
    {
        string input = Path.Combine(Path.GetTempPath(), "ürün fotoğrafı.png");
        string directory = Path.Combine(Path.GetTempPath(), "CutLocal Output");

        string result = OutputPathPolicy.CreatePngPath(input, directory, "clean");

        Assert.Equal(Path.Combine(directory, "ürün fotoğrafı.clean.png"), result);
    }

    [Theory]
    [InlineData("..\\escape")]
    [InlineData("../escape")]
    [InlineData("..")]
    public void CreatePngPath_RejectsUnsafeSuffix(string suffix)
    {
        Assert.Throws<ArgumentException>(() => OutputPathPolicy.CreatePngPath(
            Path.Combine(Path.GetTempPath(), "input.png"),
            Path.GetTempPath(),
            suffix));
    }
}
