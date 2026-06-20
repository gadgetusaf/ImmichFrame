using ImmichFrame.WebApi.Persistence.Entities;

namespace ImmichFrame.WebApi.Models;

/// <summary>
/// Admin-facing representation of an Immich account. The API key is write-only: it is never
/// returned to the browser (only <see cref="HasApiKey"/> is), and on update an empty value keeps
/// the stored key unchanged.
/// </summary>
public class AccountDto
{
    public Guid Id { get; set; }
    public string ImmichServerUrl { get; set; } = string.Empty;

    public bool HasApiKey { get; set; }
    public string? ApiKey { get; set; }

    public bool ShowMemories { get; set; }
    public bool ShowFavorites { get; set; }
    public bool ShowArchived { get; set; }
    public bool ShowVideos { get; set; }

    public int? ImagesFromDays { get; set; }
    public DateTime? ImagesFromDate { get; set; }
    public DateTime? ImagesUntilDate { get; set; }
    public List<Guid> Albums { get; set; } = new();
    public List<Guid> ExcludedAlbums { get; set; } = new();
    public List<Guid> People { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public int? Rating { get; set; }

    public static AccountDto FromEntity(AccountEntity e) => new()
    {
        Id = e.Id,
        ImmichServerUrl = e.ImmichServerUrl,
        HasApiKey = !string.IsNullOrEmpty(e.ApiKey),
        ApiKey = null,
        ShowMemories = e.ShowMemories,
        ShowFavorites = e.ShowFavorites,
        ShowArchived = e.ShowArchived,
        ShowVideos = e.ShowVideos,
        ImagesFromDays = e.ImagesFromDays,
        ImagesFromDate = e.ImagesFromDate,
        ImagesUntilDate = e.ImagesUntilDate,
        Albums = new List<Guid>(e.Albums),
        ExcludedAlbums = new List<Guid>(e.ExcludedAlbums),
        People = new List<Guid>(e.People),
        Tags = new List<string>(e.Tags),
        Rating = e.Rating,
    };

    /// <summary>Copies the editable filter fields onto an entity (never the Id or API key).</summary>
    public void ApplyTo(AccountEntity e)
    {
        e.ImmichServerUrl = ImmichServerUrl.Trim();
        e.ShowMemories = ShowMemories;
        e.ShowFavorites = ShowFavorites;
        e.ShowArchived = ShowArchived;
        e.ShowVideos = ShowVideos;
        e.ImagesFromDays = ImagesFromDays;
        e.ImagesFromDate = ImagesFromDate;
        e.ImagesUntilDate = ImagesUntilDate;
        e.Albums = new List<Guid>(Albums);
        e.ExcludedAlbums = new List<Guid>(ExcludedAlbums);
        e.People = new List<Guid>(People);
        e.Tags = new List<string>(Tags);
        e.Rating = Rating;
    }
}
