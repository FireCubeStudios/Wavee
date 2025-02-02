﻿using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wavee.Domain.Playback;
using Wavee.Spotify.Common;
using Wavee.UI.Extensions;
using Wavee.UI.Features.Album.ViewModels;
using Wavee.UI.Features.Artist.ViewModels;
using Wavee.UI.Features.Navigation;
using Wavee.UI.Features.Playback.Notifications;
using Wavee.UI.Features.RightSidebar.ViewModels;
using Wavee.UI.Features.Shell.ViewModels;
using Wavee.UI.Test;

namespace Wavee.UI.Features.Playback.ViewModels;

public abstract class PlaybackPlayerViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    private record PositionCallback(int MinimumDifferenceMs, Action<TimeSpan> Callback, int previousMs);

    private TimeSpan? _timeSinceStopwatch;
    private Stopwatch? _stopwatch;
    private Dictionary<Guid, PositionCallback> _positionCallbacks = new();

    private readonly Timer _timer;
    private bool _isActive;
    private bool _hasPlayback;
    private string? _coverSmallImageUrl;
    private string? _title;
    private string[]? _artistsIds;
    private (string, string)[]? _artists;
    private TimeSpan _duration;

    private readonly IUIDispatcher _dispatcher;
    private string _id;
    private bool _paused;

    protected ObservableCollection<RemoteDevice> _devices = new ObservableCollection<RemoteDevice>();
    private string? _groupId;

    protected PlaybackPlayerViewModel(IUIDispatcher dispatcher, 
        RightSidebarLyricsViewModel lyrics)
    {
        _dispatcher = dispatcher;
        Lyrics = lyrics;

        _timer = new Timer(state =>
        {
            var time = _timeSinceStopwatch + _stopwatch?.Elapsed;
            if (time is not null)
            {
                foreach (var callback in _positionCallbacks)
                {
                    var (minimumDifferenceMs, action, previousMs) = callback.Value;
                    var currentMs = (int)time.Value.TotalMilliseconds;
                    var diff = currentMs - previousMs;
                    if (diff is < 0 || diff >= minimumDifferenceMs)
                    {
                        _dispatcher.Invoke(() => action(time.Value));
                        _positionCallbacks[callback.Key] = callback.Value with { previousMs = currentMs };
                    }
                }
            }
        }, null, -1, -1);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool HasPlayback
    {
        get => _hasPlayback;
        protected set => SetProperty(ref _hasPlayback, value);
    }

    public string? CoverSmallImageUrl
    {
        get => _coverSmallImageUrl;
        protected set => SetProperty(ref _coverSmallImageUrl, value);
    }

    public string? Title
    {
        get => _title;
        protected set => SetProperty(ref _title, value);
    }

    public string? GroupId
    {
        get => _groupId;
        protected set => SetProperty(ref _groupId, value);
    }
    public (string, string)[]? Artists
    {
        get => _artists;
        protected set => SetProperty(ref _artists, value);
    }

    public TimeSpan Duration
    {
        get => _duration;
        protected set => SetProperty(ref _duration, value);
    }
    public ObservableCollection<RemoteDevice> Devices => _devices;

    public string Id
    {
        get => _id;
        protected set => SetProperty(ref _id, value);
    }

 
    public bool IsPaused
    {
        get => _paused;
        protected set => SetProperty(ref _paused, value);
    }

    public RightSidebarLyricsViewModel Lyrics { get; }

    public TimeSpan Position
    {
        get
        {
            if (_timeSinceStopwatch is null) return TimeSpan.Zero;

            return _timeSinceStopwatch.Value + _stopwatch?.Elapsed ?? TimeSpan.Zero;
        }
    }

    public event EventHandler<RemoteDeviceChangeNotification>? DevicesChanged;

    public event EventHandler PlaybackChanged;
    public Guid AddPositionCallback(int minimumDifferenceMs, Action<TimeSpan> callback)
    {
        var id = Guid.NewGuid();
        _positionCallbacks.Add(id, new PositionCallback(minimumDifferenceMs, callback, 0));
        return id;
    }


    public void Activate()
    {
        IsActive = true;
    }

    protected void Pause()
    {
        if (!HasPlayback) return;
        _stopwatch?.Stop();
        _timer.Change(-1, -1);
        IsPaused = true;
    }

    protected void Resume(TimeSpan? at = null)
    {
        if (!HasPlayback) return;

        //Tick every 10ms
        _timeSinceStopwatch = at ?? _timeSinceStopwatch ?? TimeSpan.Zero;
        _stopwatch = Stopwatch.StartNew();
        _timer.Change(0, 10);
        IsPaused = false;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }
        this.IsActive = false;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        await _timer.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected void OnPlaybackChanged()
    {
        this.PlaybackChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void OnDevicesChanged()
    {
        this.DevicesChanged?.Invoke(this, new RemoteDeviceChangeNotification
        {
            Devices = _devices.ToArray()
        });
    }
}