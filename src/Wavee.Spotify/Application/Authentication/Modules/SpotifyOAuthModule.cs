using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Eum.Spotify;
using Google.Protobuf;
using LiteDB;
using Wavee.Spotify.Common.Contracts;
using Wavee.Spotify.Infrastructure.LegacyAuth;

namespace Wavee.Spotify.Application.Authentication.Modules;

public sealed partial class SpotifyOAuthModule : ISpotifyAuthModule
{
    private readonly SpotifyTcpHolder _tcpHolder;
    private readonly HttpClient _accountsApi;
    private readonly FetchRedirectUrlDelegate _openBrowser;
    private readonly SpotifyClientConfig _config;

    public SpotifyOAuthModule(FetchRedirectUrlDelegate openBrowser,
        IHttpClientFactory httpClientFactory,
        SpotifyClientConfig config,
        SpotifyTcpHolder tcpHolder)
    {
        _openBrowser = openBrowser;
        _config = config;
        _tcpHolder = tcpHolder;
        _accountsApi = httpClientFactory.CreateClient(Constants.SpotifyAccountsApiHttpClient);
    }

    public bool IsDefault { get; private set; }

    public async Task<StoredCredentials> GetCredentials(string? username, CancellationToken cancellationToken = default)
    {
        //utm_medium=desktop-win32-store&response_type=code
        //&flow_ctx=dbbc3e5d-7bc8-4bf7-a6ff-782e98e7d603%3A1700995564
        //&redirect_uri=http%3A%2F%2F127.0.0.1%3A4381%2Flogin&code_challenge_method=S256&client_id=65b708073fc0480ea92a077233ca87bd&code_challenge=aR3npe-GV1gEqctx0A5-0I5IocmJ9UqOSgr_hNshL-0&utm_source=spotify
        //https://accounts.spotify.com/en/oauth2/v2/auth?utm_campaign=organic&scope={scopes}
        //&utm_medium=desktop-win32-store
        //&response_type=code
        //&flow_ctx={guid?}
        //&redirect_uri=http://127.0.0.1:4381/login
        //&code_challenge_method=S256
        //&client_id=65b708073fc0480ea92a077233ca87bd
        //&code_challenge={code_challenge}
        //&utm_source=spotify

        const string redirectUri = "http://127.0.0.1:5001/login";
        //scope=playlist-modify ugc-image-upload user-follow-read user-read-email user-read-private app-remote-control streaming user-follow-modify user-modify-playback-state user-library-modify playlist-modify-public playlist-read user-read-birthdate user-top-read playlist-read-private playlist-read-collaborative user-modify-private playlist-modify-private user-modify user-library-read user-personalized user-read-play-history user-read-playback-state user-read-currently-playing user-read-recently-played user-read-playback-position
        const string scopes =
            "playlist-modify ugc-image-upload user-follow-read user-read-email user-read-private app-remote-control streaming user-follow-modify user-modify-playback-state user-library-modify playlist-modify-public playlist-read user-read-birthdate user-top-read playlist-read-private playlist-read-collaborative user-modify-private playlist-modify-private user-modify user-library-read user-personalized user-read-play-history user-read-playback-state user-read-currently-playing user-read-recently-played user-read-playback-position";
        const string codeChallengeMethod = "S256";
        const string utmSource = "spotify";
        const string utmMedium = "desktop-win32-store";
        const string responseType = "code";

        var flowctx = Guid.NewGuid().ToString();
        var codeVerifier = GenerateNonce();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        var query = new Dictionary<string, string>
        {
            { "utm_campaign", "organic" },
            { "scope", scopes },
            { "utm_medium", utmMedium },
            { "response_type", responseType },
            { "flow_ctx", flowctx },
            { "redirect_uri", redirectUri },
            { "code_challenge_method", codeChallengeMethod },
            { "client_id", Constants.SpotifyClientId },
            { "code_challenge", codeChallenge },
            { "utm_source", utmSource }
        };

        var queryString = System.Web.HttpUtility.ParseQueryString(string.Empty);
        foreach (var (key, value) in query)
        {
            queryString[key] = value;
        }

        var urlBuilder = new UriBuilder("https://accounts.spotify.com")
        {
            Path = "/en/oauth2/v2/auth",
            Query = queryString.ToString()
        };
        var url = urlBuilder.ToString();
        var (redirectUrl, makeDefault) = await _openBrowser(url, cancellationToken);
        IsDefault = makeDefault;
        var code = Regex.Match(redirectUrl, "code=(.*)").Groups[1].Value;
        // POST https://accounts.spotify.com/api/token
        //Content-Type: application/x-www-form-urlencoded
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        var body = new Dictionary<string, string>
        {
            { "client_id", Constants.SpotifyClientId },
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", redirectUri },
            { "code_verifier", codeVerifier }
        };
        request.Content = new FormUrlEncodedContent(body);
        using var response = await _accountsApi.SendAsync(request, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var jsondoc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var accessToken = jsondoc.RootElement.GetProperty("access_token").GetString();
        jsondoc.RootElement.GetProperty("expires_in").GetInt32();
        jsondoc.RootElement.GetProperty("refresh_token").GetString();
        jsondoc.RootElement.GetProperty("scope").GetString();
        jsondoc.RootElement.GetProperty("token_type").GetString();
        var finalUsername = jsondoc.RootElement.GetProperty("username").GetString();


        //Exchange for accesspoint token
        var deviceId = _config.Remote.DeviceId;

        await _tcpHolder.Connect(
            credentials: new LoginCredentials
            {
                AuthData = ByteString.CopyFromUtf8(accessToken),
                Username = finalUsername,
                Typ = AuthenticationType.AuthenticationSpotifyToken
            },
            deviceId: deviceId,
            cancellationToken: cancellationToken
        );

        var apwelcome = await _tcpHolder.WelcomeMessage;


        var reusableUsername = apwelcome.CanonicalUsername;
        var reusablePassword = apwelcome.ReusableAuthCredentials;

        var reusablePasswordType = apwelcome.ReusableAuthCredentialsType;
        return new StoredCredentials(
            Username: reusableUsername,
            ReusableCredentialsBase64: reusablePassword.ToBase64(),
            ReusableCredentialsType: (int)reusablePasswordType,
            IsDefault: makeDefault
        )
        {
            Id = ObjectId.NewObjectId()
        };
    }

    private static string GenerateNonce()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz123456789";
        var nonce = new char[128];
        for (int i = 0; i < nonce.Length; i++)
        {
            var numberToPick = RandomNumberGenerator.GetInt32(
                fromInclusive: 0,
                toExclusive: chars.Length
            );
            nonce[i] = chars[numberToPick];
        }

        return new string(nonce);
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier));
        var b64Hash = Convert.ToBase64String(hash);
        var code = UrlSafeRegex().Replace(b64Hash, "-");
        code = UrlSafeRegex_2().Replace(code, "_");
        code = UrlSafeRegex_3().Replace(code, "");
        return code;
    }

    [GeneratedRegex("\\+")]
    private static partial Regex UrlSafeRegex();

    [GeneratedRegex("\\/")]
    private static partial Regex UrlSafeRegex_2();

    [GeneratedRegex("=+$")]
    private static partial Regex UrlSafeRegex_3();
}

public delegate Task<OpenBrowserResult> FetchRedirectUrlDelegate(string url, CancellationToken cancellationToken);

public readonly record struct OpenBrowserResult(string Url, bool MakeDefault);