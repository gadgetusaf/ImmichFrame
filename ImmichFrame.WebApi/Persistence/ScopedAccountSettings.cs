using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Persistence.Entities;

namespace ImmichFrame.WebApi.Persistence;

/// <summary>
/// Presents a slideshow link as an <see cref="IAccountSettings"/>: the connection (server URL + API
/// key) comes from the link's account, the content filters from the link itself. This lets a link
/// reuse the exact same <c>PooledImmichFrameLogic</c> pooling the slideshow already uses.
/// </summary>
public class ScopedAccountSettings(AccountEntity account, string decryptedApiKey, SlideshowLinkEntity link)
    : IAccountSettings
{
    public string ImmichServerUrl => account.ImmichServerUrl;
    public string ApiKey => decryptedApiKey;
    public string? ApiKeyFile => null;

    public bool ShowMemories => link.ShowMemories;
    public bool ShowFavorites => link.ShowFavorites;
    public bool ShowArchived => link.ShowArchived;
    public bool ShowVideos => link.ShowVideos;
    public int? ImagesFromDays => link.ImagesFromDays;
    public DateTime? ImagesFromDate => link.ImagesFromDate;
    public DateTime? ImagesUntilDate => link.ImagesUntilDate;
    public List<Guid> Albums => link.Albums;
    public List<Guid> ExcludedAlbums => link.ExcludedAlbums;
    public List<Guid> People => link.People;
    public List<string> Tags => link.Tags;
    public int? Rating => link.Rating;

    public void ValidateAndInitialize() { }
}
