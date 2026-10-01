using System.Net;
using System.Text;
using Amazon.Runtime;
using CampusSpace.Api.Photos;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace CampusSpace.Tests.Unit;

/// <summary>
/// R2PhotoStore over a stub HTTP handler (the SDK's HttpClientFactory hook): no network, never R2. Checks the request
/// R2 receives, the error classification, and that no credential, SDK message, key or bucket reaches the logs or the
/// exception.
/// </summary>
public sealed class R2PhotoStoreTests
{
    private const string Account = "0123456789abcdef0123456789abcdef";
    private const string Bucket = "campusspace-photos-test";
    private const string Secret = "SENTINEL-SECRET-0f4c";
    private const string AccessKey = "SENTINELACCESSKEY";
    private const string ProviderText = "SENTINEL-PROVIDER-MESSAGE";

    private static readonly string Key = PhotoKeys.New(DamagePhotoRules.Png);

    private readonly CapturingLoggerProvider _logs = new();
    private readonly List<(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, byte[] Body)> _requests = [];

    private R2PhotoStore Store(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var options = new R2Options { AccountId = Account, AccessKeyId = AccessKey, SecretAccessKey = Secret, Bucket = Bucket };
        var client = R2PhotoStore.CreateClient(options, new StubFactory(request =>
        {
            var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(",", h.Value));
            var body = request.Content?.ReadAsByteArrayAsync().GetAwaiter().GetResult() ?? [];
            _requests.Add((request.Method, request.RequestUri!, headers, body));
            return respond(request);
        }));
        var factory = LoggerFactory.Create(b => b.AddProvider(_logs));
        return new R2PhotoStore(client, Bucket, factory.CreateLogger<R2PhotoStore>());
    }

    private static HttpResponseMessage Xml(HttpStatusCode status, string code) => new(status)
    {
        Content = new StringContent(
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>{code}</Code><Message>{ProviderText} {Bucket} {Key}</Message></Error>",
            Encoding.UTF8, "application/xml"),
    };

    private void ShouldLeakNothing(Exception? exception = null)
    {
        var text = string.Join("\n", _logs.Entries.Select(e => e.Message)) + exception;
        foreach (var secret in new[] { Secret, AccessKey, ProviderText, Bucket, Key, Account })
            text.Should().NotContain(secret);
    }

    [Fact]
    public async Task Put_sends_the_bytes_path_style_with_only_a_content_type()
    {
        var store = Store(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var bytes = DamagePhotoRulesTests.PngBytes;

        await store.PutAsync(Key, new MemoryStream(bytes), bytes.Length, "image/png");

        var put = _requests.Should().ContainSingle().Subject;
        put.Method.Should().Be(HttpMethod.Put);
        put.Uri.Host.Should().Be($"{Account}.r2.cloudflarestorage.com");
        put.Uri.AbsolutePath.Should().Be($"/{Bucket}/{Key}");
        put.Headers["content-type"].Should().Be("image/png");
        put.Headers.Keys.Should().NotContain(h => h.StartsWith("x-amz-meta-"));
        put.Headers["x-amz-content-sha256"].Should().Be("UNSIGNED-PAYLOAD");
        put.Headers["authorization"].Should().Contain("/auto/s3/aws4_request").And.NotContain(Secret);
        put.Body.Should().Equal(bytes);
        ShouldLeakNothing();
    }

    [Fact]
    public async Task Open_returns_the_bytes_and_null_for_a_missing_object()
    {
        var bytes = DamagePhotoRulesTests.JpegBytes;
        var store = Store(request => request.RequestUri!.AbsolutePath.EndsWith(Key)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : Xml(HttpStatusCode.NotFound, "NoSuchKey"));

        await using (var content = (await store.OpenAsync(Key))!)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy);
            copy.ToArray().Should().Equal(bytes);
        }
        (await store.OpenAsync(PhotoKeys.New(DamagePhotoRules.Jpeg))).Should().BeNull();
        ShouldLeakNothing();
    }

    [Fact]
    public async Task Exists_is_a_HEAD_and_false_on_404()
    {
        var store = Store(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await store.ExistsAsync(Key)).Should().BeFalse();
        _requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Head);
    }

    [Fact]
    public async Task An_error_answer_is_Failed_and_logs_only_status_and_code()
    {
        var store = Store(_ => Xml(HttpStatusCode.Forbidden, "AccessDenied"));

        var error = (await FluentActions.Awaiting(() => store.OpenAsync(Key))
            .Should().ThrowAsync<PhotoStoreUnavailableException>()).Which;

        error.Failure.Should().Be(PhotoStoreFailure.Failed);
        error.Message.Should().Be(PhotoStoreUnavailableException.FailedMessage);
        error.InnerException.Should().BeNull();
        _logs.Entries.Select(e => e.Message).Should().Contain("R2 get failed: HTTP 403 (AccessDenied)");
        ShouldLeakNothing(error);
    }

    [Fact]
    public async Task No_answer_is_Unreachable()
    {
        var store = Store(_ => throw new HttpRequestException($"connection refused {ProviderText}"));

        var error = (await FluentActions.Awaiting(() => store.PutAsync(Key, new MemoryStream([1, 2]), 2, "image/png"))
            .Should().ThrowAsync<PhotoStoreUnavailableException>()).Which;

        error.Failure.Should().Be(PhotoStoreFailure.Unreachable);
        error.Message.Should().Be(PhotoStoreUnavailableException.UnreachableMessage);
        _requests.Should().HaveCount(1 + R2PhotoStore.MaxRetries); // retried, then given up
        ShouldLeakNothing(error);
    }

    [Fact]
    public async Task Delete_of_a_missing_object_succeeds()
    {
        var store = Store(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await store.DeleteAsync(Key);

        _requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task Keys_outside_the_pattern_never_reach_R2()
    {
        var store = Store(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await FluentActions.Awaiting(() => store.OpenAsync("../other-bucket/x.jpg")).Should().ThrowAsync<ArgumentException>();
        _requests.Should().BeEmpty();
    }

    private sealed class StubFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(new StubHttpMessageHandler(respond));

        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;

        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => true;
    }
}
