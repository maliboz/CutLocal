using System.Security.Cryptography;
using CutLocal.Domain;
using SkiaSharp;

namespace CutLocal.Tests.Fixtures;

public static class FixtureModel
{
    public const int ModelWidth = 16;
    public const int ModelHeight = 16;

    public static async Task<(string Path, ModelDescriptor Descriptor)> CreateAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, "fixture-u2netp.onnx");
        byte[] model = BuildModel();
        await File.WriteAllBytesAsync(path, model, cancellationToken);
        string hash = Convert.ToHexString(SHA256.HashData(model));
        return (path, CreateDescriptor(hash));
    }

    public static string CreateGradientPng(string directory, int width = 64, int height = 32)
    {
        Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, "gradient.png");
        using SKBitmap bitmap = new(new SKImageInfo(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte value = (byte)Math.Round(x * 255d / (width - 1));
                bitmap.SetPixel(x, y, new SKColor(value, value, value, byte.MaxValue));
            }
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    public static ModelDescriptor CreateDescriptor(string sha256) => new()
    {
        Id = "u2netp",
        DisplayName = "Generated fixture U2NetP adapter model",
        Version = "test-1",
        FileName = "fixture-u2netp.onnx",
        Sha256 = sha256,
        DownloadUrl = "https://example.test/fixture-u2netp.onnx",
        License = new ModelLicenseDescriptor
        {
            Spdx = "CC0-1.0",
            CommercialUseAllowed = true,
            AttributionRequired = false,
            Source = "https://creativecommons.org/publicdomain/zero/1.0/",
        },
        Input = new ModelInputDescriptor
        {
            Width = ModelWidth,
            Height = ModelHeight,
            Layout = "NCHW",
            ColorOrder = "RGB",
            Mean = [0.485, 0.456, 0.406],
            Std = [0.229, 0.224, 0.225],
            ResizeMode = "stretch",
            NodeName = "input",
        },
        Output = new ModelOutputDescriptor
        {
            Activation = "minmax",
            Type = "alpha-mask",
            NodeName = "output",
        },
        RecommendedMemoryMb = 64,
        Tier = "test",
        SupportedProviders = ["cpu"],
    };

    private static byte[] BuildModel()
    {
        byte[] inputType = BuildTensorType([1, 3, ModelHeight, ModelWidth]);
        byte[] outputType = BuildTensorType([1, 1, ModelHeight, ModelWidth]);
        byte[] node = BuildMessage(writer =>
        {
            writer.String(1, "input");
            writer.String(2, "output");
            writer.String(3, "fixture_reduce_mean");
            writer.String(4, "ReduceMean");
            writer.Message(5, BuildMessage(attribute =>
            {
                attribute.String(1, "axes");
                attribute.PackedVarints(8, [1]);
                attribute.Varint(20, 7);
            }));
            writer.Message(5, BuildMessage(attribute =>
            {
                attribute.String(1, "keepdims");
                attribute.Varint(3, 1);
                attribute.Varint(20, 2);
            }));
        });
        byte[] graph = BuildMessage(writer =>
        {
            writer.Message(1, node);
            writer.String(2, "CutLocal deterministic fixture");
            writer.Message(11, BuildValueInfo("input", inputType));
            writer.Message(12, BuildValueInfo("output", outputType));
        });
        byte[] opset = BuildMessage(writer => writer.Varint(2, 13));

        return BuildMessage(writer =>
        {
            writer.Varint(1, 8);
            writer.String(2, "CutLocal tests");
            writer.Message(7, graph);
            writer.Message(8, opset);
        });
    }

    private static byte[] BuildValueInfo(string name, byte[] tensorType) => BuildMessage(writer =>
    {
        writer.String(1, name);
        writer.Message(2, tensorType);
    });

    private static byte[] BuildTensorType(IReadOnlyList<ulong> dimensions)
    {
        byte[] shape = BuildMessage(writer =>
        {
            foreach (ulong dimension in dimensions)
            {
                writer.Message(1, BuildMessage(item => item.Varint(1, dimension)));
            }
        });
        byte[] tensor = BuildMessage(writer =>
        {
            writer.Varint(1, 1);
            writer.Message(2, shape);
        });
        return BuildMessage(writer => writer.Message(1, tensor));
    }

    private static byte[] BuildMessage(Action<ProtoWriter> configure)
    {
        using ProtoWriter writer = new();
        configure(writer);
        return writer.ToArray();
    }

    private sealed class ProtoWriter : IDisposable
    {
        private readonly MemoryStream _stream = new();

        public void Varint(int field, ulong value)
        {
            WriteUnsigned((ulong)(field << 3));
            WriteUnsigned(value);
        }

        public void String(int field, string value) => Message(field, System.Text.Encoding.UTF8.GetBytes(value));

        public void Message(int field, byte[] value)
        {
            WriteUnsigned((ulong)((field << 3) | 2));
            WriteUnsigned((ulong)value.Length);
            _stream.Write(value);
        }

        public void PackedVarints(int field, IReadOnlyList<ulong> values)
        {
            using ProtoWriter packed = new();
            foreach (ulong value in values)
            {
                packed.WriteUnsigned(value);
            }

            Message(field, packed.ToArray());
        }

        public byte[] ToArray() => _stream.ToArray();

        public void Dispose() => _stream.Dispose();

        private void WriteUnsigned(ulong value)
        {
            while (value >= 0x80)
            {
                _stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            _stream.WriteByte((byte)value);
        }
    }
}
