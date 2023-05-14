﻿using Eum.Spotify;
using Eum.Spotify.connectstate;
using Google.Protobuf;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Wavee.AudioOutput.LibVLC;
using Wavee.AudioOutput.NAudio;
using Wavee.Core.Contracts;
using Wavee.Core.Enums;
using Wavee.Core.Id;
using Wavee.Spotify.Configs;
using Wavee.Spotify.Infrastructure;

NAudioOutput.SetAsMainOutput();

var credentials = new LoginCredentials
{
    Username = Environment.GetEnvironmentVariable("SPOTIFY_USERNAME"),
    AuthData = ByteString.CopyFromUtf8(Environment.GetEnvironmentVariable("SPOTIFY_PASSWORD")),
    Typ = AuthenticationType.AuthenticationUserPass
};

var config = new SpotifyConfig(
    CachePath: "cache_temp.db",
    Remote: new SpotifyRemoteConfig(
        DeviceName: "Wavee Test",
        DeviceType.Computer
    ),
    Playback: new SpotifyPlaybackConfig(
        PreferredQualityType.Highest,
        Autoplay: true
    )
);

//https://open.spotify.com/playlist/1xaI099prElMKypSdl40Bl?si=f3e7559f3ed7421d
//https://open.spotify.com/album/6lumjI581TEGHeTviSikrm?si=cc17ba62e1c34c46
var spotifyCore = await SpotifyClient.Create(credentials, config, Option<ILogger>.None);
spotifyCore.RemoteClient.StateChanged.Subscribe(x => { Console.WriteLine(x); });
await spotifyCore.PlaybackClient.PlayContext("spotify:playlist:1xaI099prElMKypSdl40Bl",
    0, TimeSpan.Zero, true,
    Option<PreferredQualityType>.None,
    CancellationToken.None);

var c = Console.ReadLine();

class MaskedFsStream : IAudioStream
{
    private readonly FileStream _fs;

    public MaskedFsStream(FileStream fs, AudioId id)
    {
        _fs = fs;
        Track = new DummyTrack(id, "test", Seq<ITrackArtist>.Empty, null, TimeSpan.MaxValue, true);
    }

    public ITrack Track { get; }
    public Option<string> Uid { get; }

    public Stream AsStream()
    {
        return _fs;
    }

    private readonly record struct DummyTrack(AudioId Id, string Title, Seq<ITrackArtist> Artists, ITrackAlbum Album,
        TimeSpan Duration, bool CanPlay) : ITrack;
}