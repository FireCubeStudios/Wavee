﻿using Eum.Spotify;
using LanguageExt;
using System;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Eum.Spotify.playlist4;
using Wavee.Spotify;
using Wavee.Spotify.Infrastructure.Cache;
using Wavee.Spotify.Infrastructure.Mercury;
using Wavee.Spotify.Infrastructure.Remote;
using Wavee.Spotify.Infrastructure.Remote.Messaging;
using static LanguageExt.Prelude;
using System.Threading;
using Google.Protobuf;
using Wavee.Spotify.Infrastructure.ApResolver;
using LanguageExt;
using LanguageExt.Common;
using LanguageExt.UnsafeValueAccess;
using Spotify.Collection.Proto.V2;
using Wavee.Core.Contracts;
using Wavee.Core.Ids;
using Wavee.Core.Infrastructure.IO;
using Eum.Spotify.extendedmetadata;
using Spotify.Metadata;
using Wavee.Spotify.Helpers;
using Wavee.Spotify.Infrastructure.Playback;

namespace Wavee.UI.Infrastructure.Live;

internal sealed class LiveSpotify : Traits.SpotifyIO
{
    private Option<SpotifyClient> _connection = Option<SpotifyClient>.None;
    private readonly SpotifyConfig _config;

    public LiveSpotify(SpotifyConfig config)
    {
        _config = config;
    }

    public Aff<SelectedListContent> GetRootList(CancellationToken ct) =>
        from client in Eff(() => _connection.ValueUnsafe())
        from spclient in ApResolve.GetSpClient(ct).ToAff()
            .Map(x => $"https://{x.host}:{x.port}")
            .Map(x =>
                $"{x}/playlist/v2/user/{client.WelcomeMessage.CanonicalUsername}/rootlist?decorate=revision,length,attributes,timestamp,owner")
        from bearer in client.TokenClient.GetToken(ct).ToAff().Map(x => new AuthenticationHeaderValue("Bearer", x))
        from result in HttpIO.GetAsync(spclient, bearer, LanguageExt.HashMap<string, string>.Empty, ct)
            .ToAff().MapAsync(async r =>
            {
                await using var stream = await r.Content.ReadAsStreamAsync(ct);
                return SelectedListContent.Parser.ParseFrom(stream);
            })
        select result;

    public Aff<JsonDocument> FetchDesktopHome(string types, int limit, int offset,
        int contentLimit, int contentOffset,
        CancellationToken ct) =>
        from client in Eff(() => _connection.ValueUnsafe())
        let apiurl = $"https://api.spotify.com/v1/views/desktop-home?types={types}&offset={offset}&limit={limit}&content_limit={contentLimit}&content_offset={contentOffset}"
        from bearer in client.TokenClient.GetToken(ct).ToAff().Map(x => new AuthenticationHeaderValue("Bearer", x))
        from result in HttpIO.GetAsync(apiurl, bearer, LanguageExt.HashMap<string, string>.Empty, ct)
            .ToAff().MapAsync(async r =>
            {
                await using var stream = await r.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, default, ct);
            })
        select result;

    public Aff<T> GetFromPublicApi<T>(string endpoint, CancellationToken cancellation) =>
        from client in Eff(() => _connection.ValueUnsafe())
        let apiUrl = $"https://api.spotify.com/v1{endpoint}"
        from bearer in client.TokenClient.GetToken(cancellation).ToAff()
            .Map(x => new AuthenticationHeaderValue("Bearer", x))
        from result in HttpIO.GetAsync(apiUrl, bearer, LanguageExt.HashMap<string, string>.Empty, cancellation)
            .ToAff().MapAsync(async r =>
            {
                var result = await r.Content.ReadFromJsonAsync<T>(cancellationToken: cancellation);
                r.Dispose();
                return result;
            })
        select result;

    public Aff<Unit> AddToPlaylist(AudioId playlistId,
        string lastRevision,
        Seq<AudioId> audioIds, Option<int> position) =>
        from client in Eff(() => _connection.ValueUnsafe())
        from spclient in ApResolve.GetSpClient(CancellationToken.None).ToAff()
            .Map(x => $"https://{x.host}:{x.port}")
            .Map(x =>
                $"{x}/playlist/v2/playlist/{playlistId.ToBase62()}/changes")
        from bearer in client.TokenClient.GetToken(CancellationToken.None).ToAff()
            .Map(x => new AuthenticationHeaderValue("Bearer", x))
        from content in Eff(() =>
        {
            ReadOnlyMemory<byte> baseBytes = ReadOnlyMemory<byte>.Empty;

            var lst = new ListChanges
            {
                BaseRevision = ByteString.FromBase64(lastRevision),
            };
            var baseDelta = new Delta();
            baseDelta.Info = new Eum.Spotify.playlist4.ChangeInfo();
            baseDelta.Info.Source = new Eum.Spotify.playlist4.SourceInfo();
            baseDelta.Info.Source.Client = Eum.Spotify.playlist4.SourceInfo.Types.Client.Client;
            var baseOp = new Op();
            baseOp.Kind = Op.Types.Kind.Add;
            baseOp.Add = new Eum.Spotify.playlist4.Add();
            if (position.IsSome)
            {
                baseOp.Add.FromIndex = position.ValueUnsafe();
            }
            else
            {
                baseOp.Add.AddLast = true;
            }

            foreach (var item in audioIds)
            {
                var time = DateTimeOffset.UtcNow;
                //in milliseconds
                var now = (long)time.ToUnixTimeMilliseconds();
                baseOp.Add.Items.Add(new Item
                {
                    Attributes = new ItemAttributes
                    {
                        Timestamp = now,
                    },
                    Uri = item.ToString(),
                });
            }
            baseDelta.Ops.Add(baseOp);
            lst.Deltas.Add(baseDelta);
            baseBytes = lst.ToByteArray();
            //gzip
            var gzip = GzipHelpers.GzipCompress(baseBytes);
            return (HttpContent)gzip;
        })

        from posted in HttpIO.Post(spclient, bearer, content, CancellationToken.None)
            .ToAff()
            .Map(x => x.EnsureSuccessStatusCode())
        select unit;

    public Aff<Unit> WriteLibrary(WriteRequest writeRequest, CancellationToken ct) =>
        //https://spclient.wg.spotify.com/collection/v2/write
        from client in Eff(() => _connection.ValueUnsafe())
        from spclient in ApResolve.GetSpClient(ct).ToAff()
            .Map(x => $"https://{x.host}:{x.port}")
            .Map(x =>
                $"{x}/collection/v2/write")
        from bearer in client.TokenClient.GetToken(CancellationToken.None).ToAff()
            .Map(x => new AuthenticationHeaderValue("Bearer", x))
        from content in Eff(() =>
        {
            var byteArrCnt = new ByteArrayContent(writeRequest.ToByteArray());
            byteArrCnt.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.collection-v2.spotify.proto");
            return byteArrCnt;
        })
        from posted in HttpIO.Post(spclient, bearer, content, CancellationToken.None)
            .ToAff()
            .Map(x => x.EnsureSuccessStatusCode())
        select unit;

    public Aff<Seq<TrackOrEpisode>> FetchBatchOfTracks(Seq<AudioId> items, CancellationToken ct = default)
    {
        if (items.IsEmpty)
            return SuccessAff(LanguageExt.Seq<TrackOrEpisode>.Empty);
        return from client in Eff(() => _connection.ValueUnsafe())
               from spclient in ApResolve.GetSpClient(ct).ToAff()
                   .Map(x => $"https://{x.host}:{x.port}")
                   .Map(x =>
                       $"{x}/extended-metadata/v0/extended-metadata")
               from bearer in client.TokenClient.GetToken(CancellationToken.None).ToAff()
                   .Map(x => new AuthenticationHeaderValue("Bearer", x))
               from content in Eff(() =>
               {
                   var request = new BatchedEntityRequest();
                   request.EntityRequest.AddRange(items.Select(a => new EntityRequest
                   {
                       EntityUri = a.ToString(),
                       Query =
                       {
                        new ExtensionQuery
                        {
                            ExtensionKind = a.Type switch
                            {
                                AudioItemType.Track => ExtensionKind.TrackV4,
                                AudioItemType.PodcastEpisode => ExtensionKind.EpisodeV4,
                                _ => ExtensionKind.UnknownExtension
                            }
                        }
                       }
                   }));
                   request.Header = new BatchedEntityRequestHeader
                   {
                       Catalogue = "premium",
                       Country = client.CountryCode.ValueUnsafe()
                   };
                   var byteArrCnt = new ByteArrayContent(request.ToByteArray());
                   //byteArrCnt.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.collection-v2.spotify.proto");
                   return byteArrCnt;
               })
               from posted in HttpIO.Post(spclient, bearer, content, CancellationToken.None)
                   .ToAff()
                   .MapAsync(async x =>
                   {
                       x.EnsureSuccessStatusCode();
                       await using var stream = await x.Content.ReadAsStreamAsync(ct);
                       var response = BatchedExtensionResponse.Parser.ParseFrom(stream);
                       var allData = response
                           .ExtendedMetadata
                           .SelectMany(c =>
                           {
                               return c.ExtensionKind switch
                               {
                                   ExtensionKind.EpisodeV4 => c.ExtensionData
                                       .Select(e => new TrackOrEpisode(
                                           Either<Episode, Track>.Left(Episode.Parser.ParseFrom(e.ExtensionData.Value))
                                       )),
                                   ExtensionKind.TrackV4 => c.ExtensionData
                                       .Select(e => new TrackOrEpisode(
                                           Either<Episode, Track>.Right(Track.Parser.ParseFrom(e.ExtensionData.Value))
                                       )),
                               };
                           });

                       return allData.ToSeq();
                   })
               select posted;
    }


    public async ValueTask<Unit> Authenticate(LoginCredentials credentials, CancellationToken ct = default)
    {
        var core = await SpotifyClient.CreateAsync(credentials, _config, ct);
        _connection = Some(core);
        return Unit.Default;
    }

    public Option<APWelcome> WelcomeMessage()
    {
        var maybe = _connection.Map(x => x.WelcomeMessage);
        return maybe;
    }

    public Option<IObservable<SpotifyRootlistUpdateNotification>> ObserveRootlist()
    {
        return _connection
            .Map(x => x.RemoteClient.RootlistChanged);
    }

    public Option<IObservable<SpotifyLibraryUpdateNotification>> ObserveLibrary()
    {
        return _connection
            .Map(x => x.RemoteClient.LibraryChanged);
    }


    public Option<IObservable<SpotifyRemoteState>> ObserveRemoteState()
    {
        return _connection
            .Map(x => x.RemoteClient.StateChanged);
    }

    public Option<SpotifyCache> Cache()
    {
        return _connection
            .Map(x => x.Cache);
    }

    public Option<string> CountryCode()
    {
        return _connection
            .Bind(x => x.CountryCode);
    }

    public Option<string> CdnUrl()
    {
        return _connection
            .Bind(x => x.ProductInfo.Find("image_url"));
    }

    public MercuryClient Mercury()
    {
        return _connection
            .Map(x => x.MercuryClient)
            .IfNone(() => throw new InvalidOperationException("Mercury client not available"));
    }

    public Option<string> GetOwnDeviceId()
    {
        return _connection
            .Map(x => x.DeviceId);
    }

    public Option<SpotifyRemoteClient> GetRemoteClient()
    {
        return _connection
            .Map(x => x.RemoteClient);
    }
}