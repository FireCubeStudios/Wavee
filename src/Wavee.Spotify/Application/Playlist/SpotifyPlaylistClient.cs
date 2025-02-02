﻿using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Eum.Spotify.playlist4;
using Eum.Spotify.playlists;
using Google.Protobuf;
using LanguageExt;
using Mediator;
using Spotify.Metadata;
using TagLib.Ape;
using Wavee.Spotify.Application.AudioKeys.QueryHandlers;
using Wavee.Spotify.Application.Metadata.Query;
using Wavee.Spotify.Application.Remote;
using Wavee.Spotify.Common;
using Wavee.Spotify.Domain.Common;
using Wavee.Spotify.Infrastructure.LegacyAuth;

namespace Wavee.Spotify.Application.Playlist;

internal sealed class SpotifyPlaylistClient : ISpotifyPlaylistClient
{
    private readonly HttpClient _httpClient;
    private readonly SpotifyTcpHolder _tcpHolder;
    private readonly List<object> _changeListeners = new();
    private readonly IMediator _mediator;

    public SpotifyPlaylistClient(IHttpClientFactory httpClientFactory,
        SpotifyTcpHolder tcpHolder,
        SpotifyRemoteHolder remoteHolder, IMediator mediator)
    {
        _tcpHolder = tcpHolder;
        _mediator = mediator;
        _httpClient = httpClientFactory.CreateClient(Constants.SpotifyRemoteStateHttpClietn);

        remoteHolder.PlaylistChanged += RemoteHolderOnPlaylistChanged;
    }

    public async Task<Diff> DiffPlaylist(SpotifyId id, BigInteger fromRevision, CancellationToken cancellationToken)
    {
        //https://gae2-spclient.spotify.com/playlist/v2/playlist/37i9dQZEVXcWZn0ZycxMML/diff?revision=0%2C000000003d22e7b3feb9d5b21a2330fc33481752&handlesContent=

        //base64 ZVPI2AAAAAC3edGc4yReH6HWZ4nv5JOZ -> 1699989720,00000000b779d19ce3245e1fa1d66789efe49399
        static string CreateRevisionString(BigInteger b)
        {
            Span<byte> bytes = b.ToByteArray(true, true);
            //First 4 bytes (only if length is 24)
            var missing = 24 - bytes.Length;
            uint number = 0;
            int offset = sizeof(uint);
            if (missing is 0)
            {
                number = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(0, sizeof(uint)));
            }
            else
            {
                var getNumb = sizeof(uint) - missing;
                var numberBytes = bytes.Slice(0, getNumb);
                offset = numberBytes.Length;
                number = numberBytes[0];
            }

            var hex = SpotifyGetAudioKeyQueryHandler.ToBase16(bytes.Slice(offset)).ToLower();
            var together = $"{number},{hex}";
            return together;
        }

        var revisionString = CreateRevisionString(fromRevision);

        const string endpoint =
            "https://spclient.com/playlist/v2/playlist/{0}/diff?revision={1}&handlesContent=";
        var uri = string.Format(endpoint, id.ToBase62(), revisionString);
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var diff = Diff.Parser.ParseFrom(stream);
        return diff;
    }

    public async Task<SelectedListContent> GetRootList(CancellationToken cancellationToken)
    {
        const string endpoint =
            "https://spclient.com/playlist/v2/user/{0}/rootlist?decorate=revision,length,attributes,timestamp,owner";
        var user = _tcpHolder.WelcomeMessage.Result.CanonicalUsername;
        var url = string.Format(endpoint, user);
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rootlist = SelectedListContent.Parser.ParseFrom(stream);
        return rootlist;
    }

    public async Task<ulong?> GetPopCount(SpotifyId fromUri, CancellationToken cancellationToken)
    {
        const string endpoint = "https://spclient.com/popcount/v2/playlist/{0}/count";
        var url = string.Format(endpoint, fromUri.ToBase62());
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var popCount = PopcountResult.Parser.ParseFrom(stream);

        return ((popCount.HasCount && !popCount.CountHiddenFromUsers) ? (ulong)popCount.Count : null);
    }

    public async Task<SelectedListContent> GetPlaylist(SpotifyId fromUri, CancellationToken cancellationToken)
    {
        const string endpoint = "https://spclient.com/playlist/v2/playlist/{0}";
        var url = string.Format(endpoint, fromUri.ToBase62());
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var playlist = SelectedListContent.Parser.ParseFrom(stream);
        return playlist;
    }

    public async Task<(SelectedListContent List, IEnumerable<SpotifyTrackOrEpisode>)> GetPlaylistWithTracks(SpotifyId fromUri, CancellationToken cancellationToken)
    {
        var list = await GetPlaylist(fromUri, cancellationToken);
        if (list.Contents.Items.Count is 0)
            return (list, Enumerable.Empty<SpotifyTrackOrEpisode>());

        var uris = list.Contents.Items.DistinctBy(f => f.Uri)
            .Select(f => (SpotifyId.FromUri(f.Uri), f))
            .GroupBy(f => f.Item1.Type);

        var output = new List<SpotifyTrackOrEpisode>(list.Contents.Items.Count);

        foreach (var group in uris)
        {
            var metadataRaw = await _mediator.Send(new FetchBatchedMetadataQuery
            {
                AllowCache = true,
                Uris = group.Select(f => f.Item1.ToString()).ToImmutableArray(),
                Country = _tcpHolder.Country,
                ItemsType = group.Key
            }, cancellationToken);

            switch (group.Key)
            {
                case SpotifyItemType.Track:
                    {
                        var metadata = metadataRaw.ToDictionary(x => x.Key,
                            x => Track.Parser.ParseFrom(x.Value));
                        output.AddRange(metadata.Select(kvp => kvp.Value)
                            .Select(dummy => new SpotifyTrackOrEpisode(dummy, null, SpotifyId.FromRaw(dummy.Gid.Span, SpotifyItemType.Track))));
                        break;
                    }
                case SpotifyItemType.PodcastEpisode:
                    {
                        var metadata = metadataRaw.ToDictionary(x => x.Key,
                            x => Episode.Parser.ParseFrom(x.Value));
                        output.AddRange(metadata.Select(kvp => kvp.Value)
                            .Select(dummy => new SpotifyTrackOrEpisode(null, dummy, SpotifyId.FromRaw(dummy.Gid.Span, SpotifyItemType.PodcastEpisode))));
                        break;
                    }
                default:
                    throw new NotSupportedException();
            }
        }

        return (list, output);
    }

    public SpotifyPlaylistChangeListener ChangeListener(SpotifyId id)
    {
        var listener = new SpotifyPlaylistChangeListener(id);
        _changeListeners.Add(listener);
        return listener;
    }

    private void RemoteHolderOnPlaylistChanged(object? sender, PlaylistModificationInfo playlistModificationInfo)
    {
        foreach (var listener in _changeListeners)
        {
            if (listener is not SpotifyPlaylistChangeListener listener1)
                continue;
            if (listener1.Id.ToString() == Encoding.UTF8.GetString(playlistModificationInfo.Uri.Span))
            {
                listener1.Incoming(playlistModificationInfo);
            }
        }
    }
}

public interface ISpotifyPlaylistClient
{
    Task<Diff> DiffPlaylist(SpotifyId id, BigInteger fromRevision, CancellationToken cancellationToken);
    Task<SelectedListContent> GetRootList(CancellationToken cancellationToken);
    Task<ulong?> GetPopCount(SpotifyId fromUri, CancellationToken cancellationToken);
    Task<SelectedListContent> GetPlaylist(SpotifyId fromUri, CancellationToken cancellationToken);
    Task<(SelectedListContent List, IEnumerable<SpotifyTrackOrEpisode>)> GetPlaylistWithTracks(SpotifyId fromUri, CancellationToken cancellationToken);
    SpotifyPlaylistChangeListener ChangeListener(SpotifyId id);
}

public sealed class SpotifyPlaylistChangeListener
{
    internal SpotifyPlaylistChangeListener(SpotifyId id)
    {
        Id = id;
    }
    public SpotifyId Id { get; }

    public event EventHandler<PlaylistModificationInfo>? ItemsChanged;

    public void Incoming(PlaylistModificationInfo playlistModificationInfo)
    {
        ItemsChanged?.Invoke(this, playlistModificationInfo);
    }
}