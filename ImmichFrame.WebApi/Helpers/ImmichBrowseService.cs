using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;

namespace ImmichFrame.WebApi.Helpers;

public record NamedId(string Id, string Name);
public record BrowseResult(List<NamedId> Albums, List<NamedId> People);

/// <summary>
/// Lists albums and people from an Immich server so the admin UI can offer pickers instead of
/// requiring raw IDs. The request is made from the server, so the URL must be reachable from the
/// ImmichFrame host (not just the admin's browser).
/// </summary>
public class ImmichBrowseService(IHttpClientFactory httpClientFactory, ILogger<ImmichBrowseService> logger)
{
    public async Task<BrowseResult> BrowseAsync(string serverUrl, string apiKey, CancellationToken ct = default)
    {
        var http = httpClientFactory.CreateClient("ImmichApiAccountClient");
        http.UseApiKey(apiKey);
        // ImmichApi appends "/api"; trailing slashes would produce "//api", which some proxies 404.
        var api = new ImmichApi(serverUrl.Trim().TrimEnd('/'), http);

        var albums = new List<NamedId>();
        var people = new List<NamedId>();
        Exception? albumError = null;
        Exception? peopleError = null;

        try
        {
            var result = await api.GetAllAlbumsAsync(null, null, ct);
            albums = result
                .Select(a => new NamedId(a.Id, a.AlbumName))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e)
        {
            albumError = e;
            logger.LogWarning(e, "Failed to list albums from {url}.", serverUrl);
        }

        // People can fail independently of albums (e.g. an older/newer Immich); don't let that
        // failure hide the albums the picker could still offer.
        try
        {
            var result = await api.GetAllPeopleAsync(null, null, 1, 1000, false, ct);
            people = result.People
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new NamedId(p.Id, p.Name))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e)
        {
            peopleError = e;
            logger.LogWarning(e, "Failed to list people from {url}.", serverUrl);
        }

        // If both failed, the server is unreachable or the key is wrong — surface it to the caller.
        if (albumError is not null && peopleError is not null)
        {
            throw albumError;
        }

        return new BrowseResult(albums, people);
    }
}
