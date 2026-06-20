using ImmichFrame.WebApi.Persistence.Entities;

namespace ImmichFrame.WebApi.Models;

/// <summary>
/// Admin-facing representation of a slideshow link. The PIN is write-only (only <see cref="HasPin"/>
/// is returned); an empty PIN on update keeps the stored one.
/// </summary>
public class LinkDto
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid AccountId { get; set; }

    public string AccessPolicy { get; set; } = SlideshowAccess.None;
    public bool HasPin { get; set; }
    public string? Pin { get; set; }

    public bool Enabled { get; set; } = true;

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

    public static LinkDto FromEntity(SlideshowLinkEntity e) => new()
    {
        Id = e.Id,
        Slug = e.Slug,
        Name = e.Name,
        AccountId = e.AccountId,
        AccessPolicy = e.AccessPolicy,
        HasPin = !string.IsNullOrEmpty(e.PinHash),
        Pin = null,
        Enabled = e.Enabled,
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

    /// <summary>Copies editable fields onto an entity (not the Id, Slug, or PinHash — handled by the controller).</summary>
    public void ApplyTo(SlideshowLinkEntity e)
    {
        e.Name = Name.Trim();
        e.AccountId = AccountId;
        e.AccessPolicy = AccessPolicy == SlideshowAccess.Pin ? SlideshowAccess.Pin : SlideshowAccess.None;
        e.Enabled = Enabled;
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
