using System.Security.Cryptography;
using System.Text;

namespace ImmichFrame.Core.Tests;

// Immich asset/album/person/tag ids are GUIDs. Tests use short human-readable labels ("A", "asset1")
// for readability; this maps a label to a stable GUID so the same label always yields the same id
// within and across tests.
public static class TestIds
{
    public static Guid From(string label)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(label));
        return new Guid(hash);
    }
}
