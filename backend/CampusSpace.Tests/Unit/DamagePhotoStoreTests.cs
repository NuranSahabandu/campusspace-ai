using CampusSpace.Api.Middleware;
using CampusSpace.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace CampusSpace.Tests.Unit;

/// <summary>DamagePhotoStore (plan §15.3): magic-byte detection, the size limit, and names that can't leave the root.</summary>
public sealed class DamagePhotoStoreTests : IDisposable
{
    public static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    public static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "campusspace-photo-tests", Guid.NewGuid().ToString("N"));
    private readonly DamagePhotoStore _store;

    public DamagePhotoStoreTests() => _store = new DamagePhotoStore(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static FormFile File(byte[] bytes, string name = "photo.jpg") =>
        new(new MemoryStream(bytes), 0, bytes.Length, "photo", name) { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };

    [Fact]
    public void Detect_reads_jpeg_and_png_signatures_only()
    {
        DamagePhotoStore.Detect(JpegBytes).Should().Be(DamagePhotoStore.Jpeg);
        DamagePhotoStore.Detect(PngBytes).Should().Be(DamagePhotoStore.Png);
        DamagePhotoStore.Detect("hello, world"u8).Should().BeNull();
        DamagePhotoStore.Detect([0xFF, 0xD8]).Should().BeNull();
        DamagePhotoStore.Detect([]).Should().BeNull();
    }

    [Fact]
    public async Task A_text_file_named_jpg_is_rejected()
    {
        var act = () => _store.ValidateAsync(File("not an image"u8.ToArray()));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Errors
            .Should().ContainKey(DamagePhotoStore.Field).WhoseValue.Should().Equal(DamagePhotoStore.NotAnImageMessage);
    }

    [Fact]
    public async Task Empty_and_too_large_files_are_rejected()
    {
        var tooLarge = new byte[DamagePhotoStore.MaxBytes + 1];
        JpegBytes.CopyTo(tooLarge, 0);

        (await FluentActions.Awaiting(() => _store.ValidateAsync(File([]))).Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(DamagePhotoStore.EmptyMessage);
        (await FluentActions.Awaiting(() => _store.ValidateAsync(File(tooLarge))).Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(DamagePhotoStore.TooLargeMessage);
    }

    [Fact]
    public async Task Exactly_the_limit_is_accepted()
    {
        var limit = new byte[DamagePhotoStore.MaxBytes];
        PngBytes.CopyTo(limit, 0);

        (await _store.ValidateAsync(File(limit))).Should().Be(DamagePhotoStore.Png);
    }

    [Fact]
    public async Task Save_uses_a_random_name_with_the_detected_extension_and_Open_and_Delete_find_it()
    {
        var upload = File(PngBytes, "holiday.jpg");
        var name = await _store.SaveAsync(upload, await _store.ValidateAsync(upload));

        name.Should().MatchRegex("^[0-9a-f]{32}\\.png$");
        var photo = _store.Open(name)!;
        photo.ContentType.Should().Be("image/png");
        await using (photo.Content)
        {
            using var copy = new MemoryStream();
            await photo.Content.CopyToAsync(copy);
            copy.ToArray().Should().Equal(PngBytes);
        }

        _store.Delete(name);
        _store.Open(name).Should().BeNull();
        _store.Delete(name); // a missing file is not an error
    }

    [Theory]
    [InlineData("../secret.jpg")]
    [InlineData("/etc/passwd")]
    [InlineData("0123456789abcdef0123456789abcdef.gif")]
    public void Names_outside_the_pattern_are_never_opened(string name)
    {
        _store.Open(name).Should().BeNull();
    }
}
