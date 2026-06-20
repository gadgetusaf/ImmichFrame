using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;

namespace ImmichFrame.WebApi.Helpers;

public record NamedId(string Id, string Name);
public record BrowseResult(List<NamedId> Albums, List<NamedId> People);

/// <summary>
/// Lists albums and people from an Immich server so the admin UI can offer pickers instead of
/// requiring raw IDs. Used only by the admin account editor.
/// </summary>
public class ImmichBrowseService(IHttpClientFactory httpClientFactory)
{
    public async Task<BrowseResult> BrowseAsync(string serverUrl, string apiKey, CancellationToken ct = default)
    {
        var http = httpClientFactory.CreateClient("ImmichApiAccountClient");
        http.UseApiKey(apiKey);
        var api = new ImmichApi(serverUrl, http);

        var albums = await api.GetAllAlbumsAsync(null, null, ct);
        var people = await api.GetAllPeopleAsync(null, null, 1, 1000, false, ct);

        var albumList = albums
            .Select(a => new NamedId(a.Id, a.AlbumName))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var peopleList = people.People
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new NamedId(p.Id, p.Name))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new BrowseResult(albumList, peopleList);
    }
}
