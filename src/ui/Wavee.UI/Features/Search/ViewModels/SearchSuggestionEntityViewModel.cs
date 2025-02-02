﻿using Mediator;
using Wavee.Spotify.Common;
using Wavee.UI.Features.Artist.ViewModels;
using Wavee.UI.Features.Navigation;
using Wavee.UI.Test;

namespace Wavee.UI.Features.Search.ViewModels;

public sealed class SearchSuggestionEntityViewModel : SearchSuggestionViewModel
{
    public string Id { get; init; }
    public SpotifyItemType Type { get; init; }
    public string Name { get; init; }
    public string? Subtitle { get; init; }
    public string? ImageUrl { get; init; }
}