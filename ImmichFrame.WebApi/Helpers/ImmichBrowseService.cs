using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;

namespace ImmichFrame.WebApi.Helpers;

public record NamedId(string Id, string Name);
public record BrowseResult(List<NamedId> Albums, List<NamedId> People, List<string> Warnings);

/// <summary>
/// Talks to an Immich server on behalf of the admin UI: lists albums/people for the pickers and
/// validates that an API key has the permissions ImmichFrame needs. Requests are made from the
/// server, so the URL must be reachable from the ImmichFrame host (not just the admin's browser).
/// </summary>
public class ImmichBrowseService(IHttpClientFactory httpClientFactory, ILogger<ImmichBrowseService> logger)
{
    private ImmichApi BuildApi(string serverUrl, string apiKey)
    {
        var http = httpClientFactory.CreateClient("ImmichApiAccountClient");
        http.UseApiKey(apiKey);
        // ImmichApi appends "/api"; trailing slashes would produce "//api", which some proxies 404.
        return new ImmichApi(serverUrl.Trim().TrimEnd('/'), http);
    }

    public async Task<BrowseResult> BrowseAsync(string serverUrl, string apiKey, CancellationToken ct = default)
    {
        var api = BuildApi(serverUrl, apiKey);
        var albums = new List<NamedId>();
        var people = new List<NamedId>();
        var warnings = new List<string>();
        Exception? albumError = null;
        Exception? peopleError = null;

        try
        {
            var result = await api.GetAllAlbumsAsync(null, null, ct);
            albums = result.Select(a => new NamedId(a.Id, a.AlbumName))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e)
        {
            albumError = e;
            logger.LogWarning(e, "Failed to list albums from {url}.", serverUrl);
            warnings.Add($"Couldn't load albums — {ImmichErrors.Describe(e)}");
        }

        try
        {
            var result = await api.GetAllPeopleAsync(null, null, 1, 1000, false, ct);
            people = result.People.Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new NamedId(p.Id, p.Name))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e)
        {
            peopleError = e;
            logger.LogWarning(e, "Failed to list people from {url}.", serverUrl);
            warnings.Add($"Couldn't load people — {ImmichErrors.Describe(e)}");
        }

        // If both failed, the server is unreachable or the key is wrong — surface it to the caller.
        if (albumError is not null && peopleError is not null)
        {
            throw albumError;
        }

        return new BrowseResult(albums, people, warnings);
    }

    /// <summary>
    /// Probes the operations ImmichFrame relies on and returns a warning for each that the API key
    /// can't perform — most importantly viewing assets, without which the slideshow shows nothing.
    /// </summary>
    public async Task<List<string>> ValidateAsync(string serverUrl, string apiKey, CancellationToken ct = default)
    {
        var api = BuildApi(serverUrl, apiKey);
        var warnings = new List<string>();

        AssetResponseDto? sample = null;
        try
        {
            var search = await api.SearchAssetsAsync(new MetadataSearchDto { Page = 1, Size = 1 }, ct);
            sample = search.Assets.Items.FirstOrDefault();
        }
        catch (Exception e)
        {
            warnings.Add($"Couldn't search assets — {ImmichErrors.Describe(e)} (needs 'asset.read')");
        }

        if (sample is not null && Guid.TryParse(sample.Id, out var sampleId))
        {
            try
            {
                await api.ViewAssetAsync(sampleId, string.Empty, AssetMediaSize.Thumbnail);
            }
            catch (Exception e)
            {
                warnings.Add($"Couldn't view a photo — {ImmichErrors.Describe(e)} Without 'asset.view' the slideshow can't display images.");
            }
        }

        try
        {
            await api.GetAllAlbumsAsync(null, null, ct);
        }
        catch (Exception e)
        {
            warnings.Add($"Couldn't list albums — {ImmichErrors.Describe(e)} (needs 'album.read')");
        }

        try
        {
            await api.GetAllPeopleAsync(null, null, 1, 1, false, ct);
        }
        catch (Exception e)
        {
            warnings.Add($"Couldn't list people — {ImmichErrors.Describe(e)} (needs 'person.read')");
        }

        return warnings;
    }
}
