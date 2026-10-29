using System;
using System.Collections.Generic;
using Xunit;

namespace ZeroAlloc.Collections.Tests;

/// <summary>
/// Regression tests for probe-index overflow: hashes near int.MaxValue, int.MinValue and negative
/// hashes must neither throw nor lose entries, including when the probe wraps around the table.
/// </summary>
public class ProbeOverflowTests
{
    public static readonly TheoryData<int> Hashes = new()
    {
        int.MaxValue, int.MaxValue - 1, int.MinValue, int.MinValue + 1, -1, -2, 0,
    };

    /// <summary>Key whose hash code is chosen by the test; equality is by <see cref="Id"/>.</summary>
    private sealed class Key : IEquatable<Key>
    {
        private readonly int _hash;
        public Key(int id, int hash) { Id = id; _hash = hash; }
        public int Id { get; }
        public bool Equals(Key? other) => other is not null && Id == other.Id;
        public override bool Equals(object? obj) => obj is Key k && Equals(k);
        public override int GetHashCode() => _hash;
    }

    private sealed class ConstComparer : IEqualityComparer<int>
    {
        private readonly int _hash;
        public ConstComparer(int hash) => _hash = hash;
        public bool Equals(int a, int b) => a == b;
        public int GetHashCode(int x) => _hash;
    }

    private const int N = 40; // more than a small table holds: forces growth and wrap-around

    [Theory]
    [MemberData(nameof(Hashes))]
    public void SpanDictionary_CollidingKeys_RoundTrip(int hash)
    {
        var d = new SpanDictionary<Key, int>(4);
        try
        {
            for (int i = 0; i < N; i++) d[new Key(i, hash)] = i;
            Assert.Equal(N, d.Count);
            for (int i = 0; i < N; i++)
            {
                Assert.True(d.TryGetValue(new Key(i, hash), out var v));
                Assert.Equal(i, v);
            }
            for (int i = 0; i < N; i++) d[new Key(i, hash)] = i + 100;
            Assert.Equal(N, d.Count);
            for (int i = 0; i < N; i += 2) Assert.True(d.Remove(new Key(i, hash)));
            Assert.False(d.Remove(new Key(0, hash)));
            for (int i = 0; i < N; i++)
                Assert.Equal(i % 2 == 1, d.TryGetValue(new Key(i, hash), out var v) && v == i + 100);
            for (int i = 0; i < N; i += 2) d.Add(new Key(i, hash), i); // reuses deleted slots
            Assert.Equal(N, d.Count);
        }
        finally { d.Dispose(); }
    }

    [Theory]
    [MemberData(nameof(Hashes))]
    public void HeapSpanDictionary_CollidingKeys_RoundTrip(int hash)
    {
        using var d = new HeapSpanDictionary<Key, int>(4);
        for (int i = 0; i < N; i++) d[new Key(i, hash)] = i;
        Assert.Equal(N, d.Count);
        for (int i = 0; i < N; i++)
        {
            Assert.True(d.TryGetValue(new Key(i, hash), out var v));
            Assert.Equal(i, v);
        }
        for (int i = 0; i < N; i++) d[new Key(i, hash)] = i + 100;
        Assert.Equal(N, d.Count);
        for (int i = 0; i < N; i += 2) Assert.True(d.Remove(new Key(i, hash)));
        Assert.False(d.Remove(new Key(0, hash)));
        for (int i = 0; i < N; i++)
            Assert.Equal(i % 2 == 1, d.TryGetValue(new Key(i, hash), out var v) && v == i + 100);
        for (int i = 0; i < N; i += 2) d.Add(new Key(i, hash), i);
        Assert.Equal(N, d.Count);
    }

    [Theory]
    [MemberData(nameof(Hashes))]
    public void ConcurrentHeapSpanDictionary_CollidingKeys_RoundTrip(int hash)
    {
        using var d = new ConcurrentHeapSpanDictionary<int, int>(4, new ConstComparer(hash));
        for (int i = 0; i < N; i++) d[i] = i;
        Assert.Equal(N, d.Count);
        for (int i = 0; i < N; i++)
        {
            Assert.True(d.TryGetValue(i, out var v));
            Assert.Equal(i, v);
        }
        for (int i = 0; i < N; i++) d[i] = i + 100;
        Assert.Equal(N, d.Count);
        for (int i = 0; i < N; i += 2) Assert.True(d.TryRemove(i, out _));
        Assert.False(d.TryRemove(0, out _));
        for (int i = 0; i < N; i++)
            Assert.Equal(i % 2 == 1, d.TryGetValue(i, out var v) && v == i + 100);
        for (int i = 0; i < N; i += 2) Assert.True(d.TryAdd(i, i));
        Assert.Equal(N, d.Count);
    }

    [Fact]
    public void ConcurrentHeapSpanDictionary_IssueRepro_ConstantMaxValueHash()
    {
        using var d = new ConcurrentHeapSpanDictionary<int, int>(4, new ConstComparer(int.MaxValue));
        d[1] = 1; d[2] = 2; d[3] = 3;
        Assert.Equal(3, d.Count);
    }
}
