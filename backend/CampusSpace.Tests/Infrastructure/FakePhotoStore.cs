using System.Collections.Concurrent;
using CampusSpace.Api.Photos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// An in-memory <see cref="IPhotoStore"/> (no network, never R2). <see cref="FailWith"/> makes every call throw like an
/// unreachable or failing store; <see cref="DuringPut"/> runs while an upload is "in flight" (after the object is
/// stored, before PutAsync returns), for tests about what happens meanwhile.
/// </summary>
public sealed class FakePhotoStore : IPhotoStore
{
    public ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> Objects { get; } = new();

    public string Name => "fake";

    public PhotoStoreFailure? FailWith { get; set; }

    public bool FailDelete { get; set; }

    public Func<string, Task>? DuringPut { get; set; }

    public async Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken ct = default)
    {
        PhotoKeys.EnsureValid(key);
        Throw();
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, ct);
        copy.Length.Should().Be(length);
        Objects[key] = (copy.ToArray(), contentType);
        if (DuringPut is { } hook)
            await hook(key);
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken ct = default)
    {
        Throw();
        return Task.FromResult<Stream?>(Objects.TryGetValue(key, out var o) ? new MemoryStream(o.Bytes, writable: false) : null);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Throw();
        if (FailDelete)
            throw new PhotoStoreUnavailableException(PhotoStoreFailure.Unreachable);
        Objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        Throw();
        return Task.FromResult(Objects.ContainsKey(key));
    }

    private void Throw()
    {
        if (FailWith is { } failure)
            throw new PhotoStoreUnavailableException(failure);
    }
}

public static class PhotoStoreTestData
{
    /// <summary>The same API and database with <paramref name="store"/> as the photo store.</summary>
    public static WebApplicationFactory<Program> WithPhotoStore(this CustomWebApplicationFactory factory, IPhotoStore store) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(store)));

    /// <summary>A client of <paramref name="target"/> that sends <paramref name="source"/>'s token.</summary>
    public static HttpClient SameUser(this WebApplicationFactory<Program> target, HttpClient source)
    {
        var client = target.CreateClient();
        client.DefaultRequestHeaders.Authorization = source.DefaultRequestHeaders.Authorization;
        return client;
    }
}
