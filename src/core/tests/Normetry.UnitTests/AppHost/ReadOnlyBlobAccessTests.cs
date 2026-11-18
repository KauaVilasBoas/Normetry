using System.Security.Cryptography;
using System.Web;
using Normetry.AppHost;
using Xunit;

namespace Normetry.UnitTests.AppHost;

public sealed class ReadOnlyBlobAccessTests
{
    private const string Endpoint = "http://127.0.0.1:10000/devstoreaccount1";

    // A random key: the SAS is signed locally, nothing is sent.
    private static readonly string Account =
        $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))};BlobEndpoint={Endpoint};";

    [Fact]
    public void The_ai_service_gets_a_sas_that_reads_one_container_and_does_nothing_else()
    {
        var expiresOn = new DateTimeOffset(2027, 11, 18, 0, 0, 0, TimeSpan.Zero);

        var settings = ReadOnlyBlobAccess
            .ConnectionString(Account, "documents", expiresOn)
            .Split(';')
            .Select(setting => setting.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1]);

        Assert.Equal(["BlobEndpoint", "SharedAccessSignature"], settings.Keys);
        Assert.Equal(Endpoint, settings["BlobEndpoint"].TrimEnd('/'));
        var sas = HttpUtility.ParseQueryString(settings["SharedAccessSignature"]);
        Assert.Equal("c", sas["sr"]); // the container, not the account
        Assert.Equal("r", sas["sp"]); // read only: no add, create, write, delete or list
        Assert.Equal(expiresOn, DateTimeOffset.Parse(sas["se"]!));
        Assert.NotEmpty(sas["sig"]!);
    }
}
