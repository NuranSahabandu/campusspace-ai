namespace CampusSpace.Api.Photos;

/// <summary>
/// File-system <see cref="IPhotoStore"/> under Storage:DamagePhotosPath (outside any web root). For Development without
/// R2 keys and for tests; Production refuses it (Render's disk is wiped on every deploy).
/// </summary>
public sealed class LocalPhotoStore(string root) : IPhotoStore
{
    public string Root { get; } = Path.GetFullPath(root);

    public string Name => "local folder";

    public async Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken ct = default)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Root);
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            await content.CopyToAsync(file, ct);
        }
        catch
        {
            // Never leave a partly written file behind.
            File.Delete(path);
            throw;
        }
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken ct = default)
    {
        var path = PathOf(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        File.Delete(PathOf(key));
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(File.Exists(PathOf(key)));

    private string PathOf(string key)
    {
        PhotoKeys.EnsureValid(key);
        return Path.Combine(Root, key);
    }
}
