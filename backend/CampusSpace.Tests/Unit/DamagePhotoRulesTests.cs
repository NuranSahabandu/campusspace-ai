using CampusSpace.Api.Middleware;
using CampusSpace.Api.Photos;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace CampusSpace.Tests.Unit;

/// <summary>DamagePhotoRules (plan §15.3): magic-byte detection and the size limit, the same for every photo store.</summary>
public sealed class DamagePhotoRulesTests
{
    public static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    public static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    public static FormFile File(byte[] bytes, string name = "photo.jpg") =>
        new(new MemoryStream(bytes), 0, bytes.Length, "photo", name) { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };

    [Fact]
    public void Detect_reads_jpeg_and_png_signatures_only()
    {
        DamagePhotoRules.Detect(JpegBytes).Should().Be(DamagePhotoRules.Jpeg);
        DamagePhotoRules.Detect(PngBytes).Should().Be(DamagePhotoRules.Png);
        DamagePhotoRules.Detect("hello, world"u8).Should().BeNull();
        DamagePhotoRules.Detect([0xFF, 0xD8]).Should().BeNull();
        DamagePhotoRules.Detect([]).Should().BeNull();
    }

    [Fact]
    public async Task A_text_file_named_jpg_is_rejected()
    {
        var act = () => DamagePhotoRules.ValidateAsync(File("not an image"u8.ToArray()));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Errors
            .Should().ContainKey(DamagePhotoRules.Field).WhoseValue.Should().Equal(DamagePhotoRules.NotAnImageMessage);
    }

    [Fact]
    public async Task Empty_and_too_large_files_are_rejected()
    {
        var tooLarge = new byte[DamagePhotoRules.MaxBytes + 1];
        JpegBytes.CopyTo(tooLarge, 0);

        (await FluentActions.Awaiting(() => DamagePhotoRules.ValidateAsync(File([]))).Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(DamagePhotoRules.EmptyMessage);
        (await FluentActions.Awaiting(() => DamagePhotoRules.ValidateAsync(File(tooLarge))).Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(DamagePhotoRules.TooLargeMessage);
    }

    [Fact]
    public async Task Exactly_the_limit_is_accepted()
    {
        var limit = new byte[DamagePhotoRules.MaxBytes];
        PngBytes.CopyTo(limit, 0);

        (await DamagePhotoRules.ValidateAsync(File(limit))).Should().Be(DamagePhotoRules.Png);
    }

    [Fact]
    public void Keys_are_random_hex_with_the_detected_extension()
    {
        var key = PhotoKeys.New(DamagePhotoRules.Png);

        key.Should().MatchRegex("^[0-9a-f]{32}\\.png$");
        PhotoKeys.New(DamagePhotoRules.Png).Should().NotBe(key);
        PhotoKeys.IsValid(key).Should().BeTrue();
        PhotoKeys.ContentTypeOf(key).Should().Be("image/png");
        PhotoKeys.ContentTypeOf(PhotoKeys.New(DamagePhotoRules.Jpeg)).Should().Be("image/jpeg");
    }

    [Theory]
    [InlineData("../secret.jpg")]
    [InlineData("/etc/passwd")]
    [InlineData("0123456789abcdef0123456789abcdef.gif")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.jpg")]
    [InlineData("")]
    [InlineData(null)]
    public void Keys_outside_the_pattern_are_invalid(string? key)
    {
        PhotoKeys.IsValid(key).Should().BeFalse();
    }
}
