using CampusSpace.Api.Photos;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

/// <summary>LocalPhotoStore: a byte-for-byte round trip, and keys that can't leave the root.</summary>
public sealed class LocalPhotoStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "campusspace-photo-tests", Guid.NewGuid().ToString("N"));
    private readonly LocalPhotoStore _store;

    public LocalPhotoStoreTests() => _store = new LocalPhotoStore(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Put_Open_Exists_and_Delete_round_trip_the_bytes()
    {
        var key = PhotoKeys.New(DamagePhotoRules.Png);
        var bytes = DamagePhotoRulesTests.PngBytes;

        await _store.PutAsync(key, new MemoryStream(bytes), bytes.Length, "image/png");

        (await _store.ExistsAsync(key)).Should().BeTrue();
        await using (var content = (await _store.OpenAsync(key))!)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy);
            copy.ToArray().Should().Equal(bytes);
        }
        await _store.DeleteAsync(key);
        (await _store.OpenAsync(key)).Should().BeNull();
        (await _store.ExistsAsync(key)).Should().BeFalse();
        await _store.DeleteAsync(key); // a missing object is not an error
    }

    [Fact]
    public async Task A_failed_copy_leaves_no_partial_file()
    {
        var key = PhotoKeys.New(DamagePhotoRules.Jpeg);

        var act = () => _store.PutAsync(key, new FailingStream(), 100, "image/jpeg");

        await act.Should().ThrowAsync<IOException>();
        (await _store.ExistsAsync(key)).Should().BeFalse();
        Directory.GetFiles(_root).Should().BeEmpty();
    }

    [Theory]
    [InlineData("../secret.jpg")]
    [InlineData("/etc/passwd")]
    [InlineData("0123456789abcdef0123456789abcdef.gif")]
    public async Task Keys_outside_the_pattern_are_never_used(string key)
    {
        await FluentActions.Awaiting(() => _store.OpenAsync(key)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => _store.DeleteAsync(key)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => _store.PutAsync(key, new MemoryStream([1]), 1, "image/jpeg"))
            .Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>Writes one byte, then fails, like a client that disconnects mid-upload.</summary>
    private sealed class FailingStream : Stream
    {
        private bool _sent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_sent)
                throw new IOException("connection reset");
            _sent = true;
            buffer[offset] = 0xFF;
            return 1;
        }
    }
}
