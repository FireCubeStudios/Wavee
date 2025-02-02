﻿using System.Collections.ObjectModel;
using Eum.Spotify.context;
using Eum.Spotify.playback;
using Wavee.Spotify.Common;
using Wavee.UI.Features.Album.ViewModels;
using Wavee.UI.Features.Artist.Queries;
using Wavee.UI.Features.Artist.ViewModels;
using Wavee.UI.Features.Library.ViewModels.Artist;

namespace Wavee.UI.Domain.Playback;

public class PlayContext
{
    internal readonly Context _spContext = new Context();
    internal PlayOrigin _playOrigin;
    internal PreparePlayOptions _playOptions = new PreparePlayOptions();

    private PlayContext() { }

    public static ArtistBuilder FromLibraryArtist(LibraryArtistViewModel artist) => new ArtistBuilder(
        artist.Id,
        artist.Name,
        artist.Albums,
        false);

    public static ArtistBuilder FromArtist(ArtistViewModel artist, bool autoplay) => new ArtistBuilder(
      artist.Id,
      artist.Name,
      artist.Overview.Discography,
      artist.Overview.TopTracks,
      autoplay);

    public static AlbumBuilder FromAlbum(string id) => new AlbumBuilder(id);

    public class ArtistBuilder
    {
        internal IEnumerable<ArtistTopTrackViewModel> _topTracks;
        internal readonly PlayContext _playContext;
        public ArtistBuilder(string artistUri,
            string? artistName,
            IEnumerable<AlbumViewModel> albums,
            bool withAutoplay)
        {
            _playContext = new PlayContext();
            _playContext._spContext.Uri = artistUri;
            _playContext._spContext.Url = string.Empty;
            if (!string.IsNullOrEmpty(artistName))
            {
                _playContext._spContext.Metadata.Add("context_description", artistName);
            }

            _playContext._spContext.Metadata.Add("disable-autoplay", withAutoplay.ToString().ToLower());
            foreach (var album in albums)
            {
                var uri = SpotifyId.FromUri(album.Id);
                _playContext._spContext.Pages.Add(new ContextPage
                {
                    PageUrl = "hm://artistplaycontext/v1/page/spotify/album/" + uri.ToBase62() + "/km",
                    Metadata =
                    {
                        {"page_uri", album.Id},
                        {"type", ((int)album.GroupType).ToString()}
                    }
                });
            }

            _playContext._playOrigin = new PlayOrigin()
            {
                DeviceIdentifier = string.Empty,
                FeatureIdentifier = "artist",
                FeatureVersion = "xpui_2023-12-04_1701707306292_36b715a",
                ViewUri = string.Empty,
                ExternalReferrer = string.Empty,
                ReferrerIdentifier = "your_library",
                FeatureClasses = { },
            };
            _playContext._playOptions = new PreparePlayOptions();
        }

        public ArtistBuilder(string artistUri, string artistName,
            IEnumerable<ArtistViewDiscographyGroupViewModel> discography,
            IEnumerable<ArtistTopTrackViewModel> topTracks,
            bool withAutoplay)
        {
            _playContext = new PlayContext();
            _playContext._spContext.Uri = artistUri;
            _playContext._spContext.Url = $"context://{artistUri}";
            if (!string.IsNullOrEmpty(artistName))
            {
                _playContext._spContext.Metadata.Add("context_description", artistName);
            }

            _playContext._spContext.Metadata.Add("disable-autoplay", withAutoplay.ToString().ToLower());
            var firstItems = discography.FirstOrDefault()?.Items;
            if (firstItems is not null)
            {
                foreach (var albumMaybe in firstItems)
                {
                    if (albumMaybe.HasValue)
                    {
                        var album = albumMaybe.Value!.Album;
                        var uri = SpotifyId.FromUri(album.Id);
                        _playContext._spContext.Pages.Add(new ContextPage
                        {
                            PageUrl = "hm://artistplaycontext/v1/page/spotify/album/" + uri.ToBase62() + "/km",
                            Metadata =
                            {
                                { "page_uri", album.Id },
                                { "type", ((int)album.GroupType).ToString() }
                            }
                        });
                    }
                }
            }

            _playContext._playOrigin = new PlayOrigin()
            {
                DeviceIdentifier = string.Empty,
                FeatureIdentifier = "artist",
                FeatureVersion = "xpui_2023-12-04_1701707306292_36b715a",
                ViewUri = string.Empty,
                ExternalReferrer = string.Empty,
                ReferrerIdentifier = "search",
                FeatureClasses = { },
            };
            _playContext._playOptions = new PreparePlayOptions();
        }

        public TopTrackBuilder FromTopTracks(IEnumerable<ArtistTopTrackViewModel> topTracks)
        {
            return new TopTrackBuilder(this, topTracks);
        }
        public DiscographyBuilder FromDiscography(PlayContextDiscographyGroupType discographyGroupType)
        {
            if (discographyGroupType is not PlayContextDiscographyGroupType.All)
            {
                var intType = discographyGroupType switch
                {
                    PlayContextDiscographyGroupType.Albums => DiscographyGroupType.Album,
                    PlayContextDiscographyGroupType.Singles => DiscographyGroupType.Single,
                    PlayContextDiscographyGroupType.Compilations => DiscographyGroupType.Compilation,
                    _ => throw new ArgumentOutOfRangeException(nameof(discographyGroupType), discographyGroupType,
                                               null)
                };
                var allpages = _playContext._spContext.Pages;
                var pages = allpages.Where(x => x.Metadata["type"] == ((int)intType).ToString());
                _playContext._spContext.Pages.Clear();
                foreach (var page in pages)
                {
                    _playContext._spContext.Pages.Add(page);
                }
            }
            return new DiscographyBuilder(this);
        }

        public class DiscographyBuilder
        {
            private readonly ArtistBuilder _builder;

            internal DiscographyBuilder(ArtistBuilder builder)
            {
                _builder = builder;
            }

            public AlbumBuilder StartWithAlbum(AlbumViewModel album)
            {
                var pageIndex = _builder
                    ._playContext
                    ._spContext
                    .Pages
                    .Select((x, i) => (x, i))
                    .FirstOrDefault(f => f.x.Metadata["page_uri"] == album.Id.ToString()).i;

                _builder
                    ._playContext._playOptions.SkipTo = new SkipToTrack
                    {
                        PageIndex = (ulong)pageIndex,
                    };
                _builder
                    ._playContext._playOptions.License = "premium";

                return new AlbumBuilder(_builder._playContext);
            }
        }
    }

    public class AlbumBuilder
    {
        private readonly PlayContext _ctx;

        internal AlbumBuilder(PlayContext context)
        {
            _ctx = context;
        }

        public AlbumBuilder(string id)
        {
            _ctx = new PlayContext();
            _ctx._spContext.Uri = id;
            _ctx._spContext.Url = $"context://{id}";
            _ctx._playOrigin = new PlayOrigin()
            {
                DeviceIdentifier = string.Empty,
                FeatureIdentifier = "album",
                FeatureVersion = "xpui_2023-12-04_1701707306292_36b715a",
                ViewUri = string.Empty,
                ExternalReferrer = string.Empty,
                ReferrerIdentifier = "search",
                FeatureClasses = { },
            };
            _ctx._playOptions = new PreparePlayOptions();
        }

        public TrackBuilder StartWithTrack(AlbumTrackViewModel track)
        {
            _ctx._playOptions.SkipTo??= new SkipToTrack();
            _ctx._playOptions.SkipTo.TrackIndex = (ulong)(track.Number -1);
            if (!string.IsNullOrEmpty(track.Id))
            {
                _ctx._playOptions.SkipTo.TrackUri = track.Id;
            }

            if (!string.IsNullOrEmpty(track.UniqueItemIdd))
            {
                _ctx._playOptions.SkipTo.TrackUid = track.UniqueItemIdd;
            }

            return new TrackBuilder(_ctx);
        }
    }

    public class TrackBuilder
    {
        private readonly PlayContext _ctx;

        internal TrackBuilder(PlayContext ctx)
        {
            _ctx = ctx;
        }
        public PlayContext Build()
        {
            return _ctx;
        }
    }
    public sealed class TopTrackBuilder
    {
        private readonly IEnumerable<ArtistTopTrackViewModel> _topTracks;
        private readonly ArtistBuilder _artistBuilder;
        public TopTrackBuilder(ArtistBuilder artistBuilder, IEnumerable<ArtistTopTrackViewModel> topTracks)
        {
            _artistBuilder = artistBuilder;
            _topTracks = topTracks;
        }

        public TrackBuilder StartWithTrack(ArtistTopTrackViewModel track)
        {
            var index = _topTracks.Select((x, i) => (x, i)).FirstOrDefault(f => f.x == track).i;
            _artistBuilder._playContext._playOptions ??= new PreparePlayOptions();
            _artistBuilder._playContext._playOptions.SkipTo = new SkipToTrack
            {
                TrackIndex = (ulong)index,
                TrackUri = track.Track.Id
            };
            return new TrackBuilder(_artistBuilder._playContext);
        }
    }
}
