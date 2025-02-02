using Mediator;

namespace Wavee.Spotify.Application.Common.Queries;

public sealed class SpotifyGetAdaptiveApiUrlQuery : IQuery<SpotifyGetAdaptiveApiUrl>
{
    public required SpotifyApiUrlType Type { get; init; }
    public required SpotifyGetAdaptiveApiUrl[]? DontReturnThese { get; init; }
}

public readonly record struct SpotifyGetAdaptiveApiUrl(string Host, ushort Port)
{
    public string Url(bool https, bool withPort) => $"{(https ? "https://" : string.Empty)}{Host}:{(withPort ? Port : string.Empty)}";
}

public enum SpotifyApiUrlType
{
    AccessPoint,
    Dealer,
    SpClient
}