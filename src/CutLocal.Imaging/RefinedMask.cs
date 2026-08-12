using System.Buffers;

namespace CutLocal.Imaging;

/// <summary>Owns a float alpha mask rented from a memory pool.</summary>
public sealed class RefinedMask : IDisposable
{
    private IMemoryOwner<float>? _owner;

    /// <summary>Initializes an owned mask.</summary>
    internal RefinedMask(IMemoryOwner<float> owner, int width, int height)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Width = width;
        Height = height;
        Values = owner.Memory[..checked(width * height)];
    }

    /// <summary>Gets mask width.</summary>
    public int Width { get; }

    /// <summary>Gets mask height.</summary>
    public int Height { get; }

    /// <summary>Gets float alpha values in row-major order.</summary>
    public Memory<float> Values { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        IMemoryOwner<float>? owner = Interlocked.Exchange(ref _owner, null);
        owner?.Dispose();
    }
}
