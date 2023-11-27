﻿using Eum.Spotify.connectstate;
using Eum.Spotify.playplay;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Wavee.Application.Playback;
using Wavee.Domain.Playback.Player;
using Wavee.Players.NAudio;
using Wavee.Spotify;
using Wavee.Spotify.Application.Authentication.Modules;
using Wavee.Spotify.Application.Playback;
using Wavee.Spotify.Common.Contracts;

var storagePath = "/d/";
Directory.CreateDirectory(storagePath);
var sp = new ServiceCollection()
    .AddSpotify(new SpotifyClientConfig
    {
        Storage = new StorageSettings
        {
            Path = storagePath
        },
        Remote = new SpotifyRemoteConfig
        {
            DeviceName = "Wavee debug",
            DeviceType = DeviceType.Computer
        },
        Playback = new SpotifyPlaybackConfig
        {
            InitialVolume = 0.5,
            PreferedQuality = SpotifyAudioQuality.High
        }
    })
    .WithStoredOrOAuthModule(OpenBrowser)
    .WithPlayer<NAudioPlayer>()
    .AddMediator()
    .BuildServiceProvider();

const string base64 = "CAMSEAEn1RRO2yBW/ETBsuaDz4kgASgBMKnjkKsG";
var res = PlayPlayLicenseRequest.Parser.ParseFrom(ByteString.FromBase64(base64));
const string secondOne = "CAMSEAEn1RRO2yBW/ETBsuaDz4kgASgBMJbmkKsG";
var res2 = PlayPlayLicenseRequest.Parser.ParseFrom(ByteString.FromBase64(secondOne));
const string thirdone = "CAMSEAEn1RRO2yBW/ETBsuaDz4kgASgBMNjmkKsG";
var res3 = PlayPlayLicenseRequest.Parser.ParseFrom(ByteString.FromBase64(thirdone));
var client = sp.GetRequiredService<ISpotifyClient>();
const string filePathMp3 = "C:\\Users\\ckara\\Downloads\\4dba53850d6bfdb9800d53d65fe2e5f1369b9040.mp3";
client.Player.Crossfade(TimeSpan.FromSeconds(10));
// await client.Player.Play(WaveePlaybackList.Create(
//     LocalMediaSource.CreateFromFilePath(filePathMp3),
//     LocalMediaSource.CreateFromFilePath(filePathMp3)));

//await client.Initialize();

var spotifyTrack = SpotifyMediaSource.CreateFromUri(client, "spotify:track:6ru2VMkOMkSBUKWqrTZYwe");
await client.Player.Play(WaveePlaybackList.Create(spotifyTrack));

// client.PlaybackStateChanged += (sender, state) =>
// {
//     Console.WriteLine($"Playback state changed: {state}");
// };
// var x = "";
Console.ReadLine();

static Task<OpenBrowserResult> OpenBrowser(string url, CancellationToken ct)
{
    Console.WriteLine($"Open browser: {url} and return redirect url");

    var redirect = Console.ReadLine();
    return Task.FromResult(new OpenBrowserResult(redirect, true));
}