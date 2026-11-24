namespace Normetry.Api;

/// <summary>Where the SignalR hubs live and how their clients send the access token.</summary>
internal static class HubPaths
{
    /// <summary>Every hub is mapped under it; only there may the token come in the query string.</summary>
    public const string Prefix = "/hubs";

    /// <summary>
    /// The query parameter the SignalR client puts the access token in, since browsers cannot set
    /// headers on a WebSocket. The gateway removes it before forwarding.
    /// </summary>
    public const string AccessTokenQueryKey = "access_token";
}
