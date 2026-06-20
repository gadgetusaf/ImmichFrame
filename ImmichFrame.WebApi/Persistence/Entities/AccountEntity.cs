using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.WebApi.Persistence.Entities;

/// <summary>
/// EF Core entity for a single Immich account (server URL + API key + content filters).
/// Replaces the in-config <c>ServerAccountSettings</c> list as the runtime source of truth.
/// </summary>
public class AccountEntity : IAccountSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ImmichServerUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    // Path-based keys are resolved to ApiKey at import time, so this is normally null for DB accounts.
    public string? ApiKeyFile { get; set; } = null;

    public bool ShowMemories { get; set; } = false;
    public bool ShowFavorites { get; set; } = false;
    public bool ShowArchived { get; set; } = false;
    public bool ShowVideos { get; set; } = false;

    public int? ImagesFromDays { get; set; }
    public DateTime? ImagesFromDate { get; set; }
    public DateTime? ImagesUntilDate { get; set; }
    public List<Guid> Albums { get; set; } = new();
    public List<Guid> ExcludedAlbums { get; set; } = new();
    public List<Guid> People { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public int? Rating { get; set; }

    public void ValidateAndInitialize()
    {
        if (!string.IsNullOrWhiteSpace(ApiKeyFile))
        {
            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                throw new Exception("Cannot specify both ApiKey and ApiKeyFile. Please provide only one.");
            }
            ApiKey = File.ReadAllText(ApiKeyFile).Trim();
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Either ApiKey or ApiKeyFile must be provided.");
        }
    }

    /// <summary>Creates a persistable entity from any <see cref="IAccountSettings"/> source.</summary>
    public static AccountEntity From(IAccountSettings a) => new()
    {
        Id = Guid.NewGuid(),
        ImmichServerUrl = a.ImmichServerUrl,
        // The source has already resolved ApiKeyFile -> ApiKey during validation.
        ApiKey = a.ApiKey,
        ApiKeyFile = null,
        ShowMemories = a.ShowMemories,
        ShowFavorites = a.ShowFavorites,
        ShowArchived = a.ShowArchived,
        ShowVideos = a.ShowVideos,
        ImagesFromDays = a.ImagesFromDays,
        ImagesFromDate = a.ImagesFromDate,
        ImagesUntilDate = a.ImagesUntilDate,
        Albums = new List<Guid>(a.Albums),
        ExcludedAlbums = new List<Guid>(a.ExcludedAlbums),
        People = new List<Guid>(a.People),
        Tags = new List<string>(a.Tags),
        Rating = a.Rating,
    };
}
