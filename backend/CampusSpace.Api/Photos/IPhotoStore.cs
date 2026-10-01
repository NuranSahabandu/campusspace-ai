namespace CampusSpace.Api.Photos;

/// <summary>A stored photo opened for reading, with the content type recorded at upload. The caller disposes the stream (File() does).</summary>
public sealed record StoredPhoto(Stream Content, string ContentType);

/// <summary>
/// Where damage photos live: <see cref="R2PhotoStore"/> (a private Cloudflare R2 bucket) or <see cref="LocalPhotoStore"/>
/// (a folder, for Development without R2 keys and for tests). Chosen from configuration in
/// <see cref="PhotoStorageExtensions.AddPhotoStorage"/>. Keys are <see cref="PhotoKeys"/>; a store never sees a name,
/// an id or a URL. Photos are served only through GET /api/loans/{id}/photo.
/// A store that can't do its job throws <see cref="PhotoStoreUnavailableException"/> (502/503), never the SDK's error.
/// </summary>
public interface IPhotoStore
{
    /// <summary>The store's name for the startup log ("R2" or "local folder"). Never a bucket, account or path.</summary>
    string Name { get; }

    /// <summary>Stores <paramref name="length"/> bytes under <paramref name="key"/> with only a content type (no metadata).</summary>
    Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken ct = default);

    /// <summary>Opens a stored photo, or null when there is no such object.</summary>
    Task<Stream?> OpenAsync(string key, CancellationToken ct = default);

    /// <summary>Removes a stored photo. A missing object is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}

public enum PhotoStoreFailure
{
    /// <summary>No answer: network, DNS, TLS or timeout. 503.</summary>
    Unreachable,

    /// <summary>The store answered with an error (for example 403 or 5xx). 502.</summary>
    Failed,
}

/// <summary>
/// The photo store could not be used. The message is a fixed text: never the SDK's message, a key, the bucket or the
/// account, and no inner exception (whose message could carry them into the error log). The store logs the operation,
/// HTTP status and error code itself. GlobalExceptionHandler maps it to 503 (Unreachable) or 502 (Failed).
/// </summary>
public sealed class PhotoStoreUnavailableException(PhotoStoreFailure failure)
    : Exception(failure == PhotoStoreFailure.Unreachable ? UnreachableMessage : FailedMessage)
{
    public const string UnreachableMessage = "Photo storage is unavailable; try again later";
    public const string FailedMessage = "Photo storage returned an error";

    public PhotoStoreFailure Failure { get; } = failure;
}
