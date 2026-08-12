using System.Buffers;
using CutLocal.Domain;
using CutLocal.Imaging;

namespace CutLocal.StressTests;

public sealed class PooledMaskLifetimeTests
{
    [Fact]
    public void RepeatedMaskLifetime_DisposesEveryOwner()
    {
        using TrackingMemoryPool pool = new();
        FloatMaskPostprocessor processor = new(pool);
        float[] values = Enumerable.Range(0, 16 * 16).Select(index => index / 255f).ToArray();

        for (int iteration = 0; iteration < 500; iteration++)
        {
            using RefinedMask mask = processor.Normalize(
                values,
                width: 16,
                height: 16,
                new MaskRefinementOptions());
            Assert.Equal(1, pool.OutstandingOwners);
        }

        Assert.Equal(0, pool.OutstandingOwners);
        Assert.Equal(500, pool.TotalRents);
    }

    private sealed class TrackingMemoryPool : MemoryPool<float>
    {
        private int _outstandingOwners;
        private int _totalRents;

        public override int MaxBufferSize => int.MaxValue;

        public int OutstandingOwners => Volatile.Read(ref _outstandingOwners);

        public int TotalRents => Volatile.Read(ref _totalRents);

        public override IMemoryOwner<float> Rent(int minimumBufferSize = -1)
        {
            int length = minimumBufferSize < 0 ? 1 : minimumBufferSize;
            Interlocked.Increment(ref _outstandingOwners);
            Interlocked.Increment(ref _totalRents);
            return new TrackingOwner(new float[length], this);
        }

        protected override void Dispose(bool disposing)
        {
        }

        private void Release() => Interlocked.Decrement(ref _outstandingOwners);

        private sealed class TrackingOwner : IMemoryOwner<float>
        {
            private TrackingMemoryPool? _pool;

            public TrackingOwner(float[] values, TrackingMemoryPool pool)
            {
                Memory = values;
                _pool = pool;
            }

            public Memory<float> Memory { get; }

            public void Dispose()
            {
                TrackingMemoryPool? pool = Interlocked.Exchange(ref _pool, null);
                pool?.Release();
            }
        }
    }
}
