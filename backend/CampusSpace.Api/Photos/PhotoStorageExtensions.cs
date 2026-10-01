using CampusSpace.Api.Options;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Photos;

public static class PhotoStorageExtensions
{
    /// <summary>
    /// The damage-photo store: <see cref="R2PhotoStore"/> when all R2 settings are set, otherwise
    /// <see cref="LocalPhotoStore"/> (Storage:DamagePhotosPath). A partial R2 configuration, or Production without R2,
    /// stops startup (<see cref="R2OptionsValidator"/>). The startup log names the store only.
    /// </summary>
    public static IServiceCollection AddPhotoStorage(this IServiceCollection services)
    {
        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<R2Options>()
            .BindConfiguration(R2Options.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<R2Options>, R2OptionsValidator>();

        services.AddSingleton<IPhotoStore>(sp =>
        {
            var r2 = sp.GetRequiredService<IOptions<R2Options>>().Value;
            return r2.IsConfigured
                ? new R2PhotoStore(R2PhotoStore.CreateClient(r2), r2.Bucket!.Trim(), sp.GetRequiredService<ILogger<R2PhotoStore>>())
                : new LocalPhotoStore(LocalFolder(sp));
        });
        services.AddScoped<LegacyPhotoImporter>();
        services.AddHostedService<PhotoStorageStartup>();
        return services;
    }

    /// <summary>Storage:DamagePhotosPath resolved against the content root (an absolute path stays as is).</summary>
    public static string LocalFolder(IServiceProvider sp) => Path.Combine(
        sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
        sp.GetRequiredService<IOptions<StorageOptions>>().Value.DamagePhotosPath);
}

/// <summary>
/// Logs the chosen photo store at startup (by name only) and, when it is R2, moves photos left in the local folder
/// into the bucket once (<see cref="LegacyPhotoImporter"/>). Never blocks or fails startup.
/// </summary>
public sealed class PhotoStorageStartup(IServiceProvider services, IPhotoStore store, ILogger<PhotoStorageStartup> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Photo store: {Store}", store.Name);
        if (store is not R2PhotoStore)
            return;
        try
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<LegacyPhotoImporter>().RunAsync(stoppingToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError("Legacy photo import failed ({ErrorType}); it runs again at the next start", e.GetType().Name);
        }
    }
}
