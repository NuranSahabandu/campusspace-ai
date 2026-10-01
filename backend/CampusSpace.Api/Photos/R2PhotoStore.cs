using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace CampusSpace.Api.Photos;

/// <summary>
/// Damage photos in a private Cloudflare R2 bucket over the S3 API (AWSSDK.S3). The bucket is never public and no URL is
/// ever handed out: the API streams each photo (GET /api/loans/{id}/photo). Objects carry only a content type (no
/// metadata, no names or ids). Errors become <see cref="PhotoStoreUnavailableException"/>; logs carry the operation,
/// HTTP status and S3 error code only, never the SDK's message, a key, the bucket, the account or a credential.
/// </summary>
public sealed partial class R2PhotoStore(IAmazonS3 s3, string bucket, ILogger<R2PhotoStore> logger) : IPhotoStore, IDisposable
{
    /// <summary>Per attempt; with <see cref="MaxRetries"/> a call gives up after about half a minute at worst.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>SDK retries for transient errors. Every call is idempotent (a retried Put writes the same key).</summary>
    public const int MaxRetries = 2;

    public string Name => "R2";

    /// <summary>
    /// The S3 client for R2: path-style, region "auto", a timeout, and checksums only when required (R2 does not accept
    /// the SDK v4 default streaming checksums). <paramref name="httpClientFactory"/> is for tests (no network).
    /// </summary>
    public static AmazonS3Client CreateClient(R2Options options, HttpClientFactory? httpClientFactory = null)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{options.AccountId!.Trim()}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            Timeout = Timeout,
            MaxErrorRetry = MaxRetries,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        if (httpClientFactory is not null)
            config.HttpClientFactory = httpClientFactory;
        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId!.Trim(), options.SecretAccessKey!.Trim()), config);
    }

    public async Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken ct = default)
    {
        PhotoKeys.EnsureValid(key);
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            // R2 needs an unsigned payload with the SDK v4 (Cloudflare's .NET example); the request itself is signed.
            DisablePayloadSigning = true,
        };
        request.Headers.ContentLength = length;
        try
        {
            await s3.PutObjectAsync(request, ct);
        }
        catch (Exception e) when (!IsCallerCancellation(e, ct))
        {
            throw Fail("put", e);
        }
    }

    public async Task<Stream?> OpenAsync(string key, CancellationToken ct = default)
    {
        PhotoKeys.EnsureValid(key);
        try
        {
            var response = await s3.GetObjectAsync(bucket, key, ct);
            return new OwnedStream(response);
        }
        catch (AmazonS3Exception e) when (IsNotFound(e))
        {
            return null;
        }
        catch (Exception e) when (!IsCallerCancellation(e, ct))
        {
            throw Fail("get", e);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        PhotoKeys.EnsureValid(key);
        try
        {
            await s3.DeleteObjectAsync(bucket, key, ct); // S3 semantics: a missing key is not an error
        }
        catch (Exception e) when (!IsCallerCancellation(e, ct))
        {
            throw Fail("delete", e);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        PhotoKeys.EnsureValid(key);
        try
        {
            await s3.GetObjectMetadataAsync(bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception e) when (IsNotFound(e))
        {
            return false;
        }
        catch (Exception e) when (!IsCallerCancellation(e, ct))
        {
            throw Fail("head", e);
        }
    }

    public void Dispose() => s3.Dispose();

    private static bool IsNotFound(AmazonS3Exception e) => e.StatusCode == HttpStatusCode.NotFound;

    private static bool IsCallerCancellation(Exception e, CancellationToken ct) => e is OperationCanceledException && ct.IsCancellationRequested;

    // S3 error codes are short PascalCase words ("AccessDenied"); anything else is not logged.
    [GeneratedRegex("^[A-Za-z]{1,40}$")]
    private static partial Regex ErrorCodePattern();

    /// <summary>Classifies an SDK failure and logs only safe facts about it.</summary>
    private PhotoStoreUnavailableException Fail(string operation, Exception e)
    {
        if (e is AmazonServiceException { StatusCode: > 0 } service)
        {
            var code = service.ErrorCode is { } c && ErrorCodePattern().IsMatch(c) ? c : "other";
            logger.LogError("R2 {Operation} failed: HTTP {Status} ({ErrorCode})", operation, (int)service.StatusCode, code);
            return new PhotoStoreUnavailableException(PhotoStoreFailure.Failed);
        }
        if (IsNetwork(e))
        {
            logger.LogError("R2 {Operation} failed: unreachable ({ErrorType})", operation, Innermost(e).GetType().Name);
            return new PhotoStoreUnavailableException(PhotoStoreFailure.Unreachable);
        }
        logger.LogError("R2 {Operation} failed: {ErrorType}", operation, e.GetType().Name);
        return new PhotoStoreUnavailableException(PhotoStoreFailure.Failed);
    }

    private static bool IsNetwork(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException)
            if (x is HttpRequestException or IOException or SocketException or TimeoutException or OperationCanceledException)
                return true;
        return false;
    }

    private static Exception Innermost(Exception e)
    {
        while (e.InnerException is not null)
            e = e.InnerException;
        return e;
    }

    /// <summary>The object's body; disposing it also disposes the SDK response (and its HTTP connection).</summary>
    private sealed class OwnedStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _inner.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _inner.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                response.Dispose();
            base.Dispose(disposing);
        }
    }
}
