namespace ImmichFrame.WebApi.Persistence.Entities;

public static class SlideshowAccess
{
    public const string None = "None";
    public const string Pin = "Pin";
    public const string ViewerAuth = "ViewerAuth";
}

/// <summary>
/// A named, shareable slideshow link (frame.example.com/{Slug}). It pulls from one
/// <see cref="AccountEntity"/> using its own content filters, and is gated by an access policy.
/// </summary>
public class SlideshowLinkEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>URL-safe identifier that appears in the public link path.</summary>
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>The account this link pulls photos from (provides the Immich URL + API key).</summary>
    public Guid AccountId { get; set; }

    public string AccessPolicy { get; set; } = SlideshowAccess.None;

    /// <summary>Hashed PIN when <see cref="AccessPolicy"/> is <see cref="SlideshowAccess.Pin"/>.</summary>
    public string? PinHash { get; set; }

    /// <summary>
    /// Server-managed value baked into every issued access token. Rotating it (on a PIN/policy change
    /// or when the link is disabled) instantly invalidates all previously-issued cookies. New links get
    /// a random stamp; rows created before this column existed default to "" and round-trip cleanly
    /// until their next security-relevant edit.
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public bool Enabled { get; set; } = true;

    // Content filters (same shape as an account's).
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
}
