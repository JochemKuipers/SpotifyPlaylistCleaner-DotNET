using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI;
using SpotifyPlaylistCleaner_DotNET.Models;
using SpotifyPlaylistCleaner_DotNET.Services;

namespace SpotifyPlaylistCleaner_DotNET.ViewModels;

public class DuplicatesViewModel : ViewModelBase, IDisposable
{
    private readonly IDuplicatesService _duplicatesService;
    private readonly ISpotifyService _spotifyService;
    private CancellationTokenSource? _cancellationTokenSource;

    private ObservableCollection<DuplicateGroup> _duplicateGroups = [];
    private ObservableCollection<DuplicateGroup> _filteredDuplicateGroups = [];
    private string _searchQuery = string.Empty;
    private bool _isDuplicatesViewVisible;
    private PlaylistModel? _currentPlaylist;
    private HierarchicalTreeDataGridSource<ITreeNode>? _source;

    public ObservableCollection<DuplicateGroup> DuplicateGroups
    {
        get => _duplicateGroups;
        private set => this.RaiseAndSetIfChanged(ref _duplicateGroups, value);
    }

    private ObservableCollection<DuplicateGroup> FilteredDuplicateGroups
    {
        get => _filteredDuplicateGroups;
        set => this.RaiseAndSetIfChanged(ref _filteredDuplicateGroups, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            FilterDuplicates();
        }
    }

    public bool IsDuplicatesViewVisible
    {
        get => _isDuplicatesViewVisible;
        set => this.RaiseAndSetIfChanged(ref _isDuplicatesViewVisible, value);
    }

    private PlaylistModel? CurrentPlaylist
    {
        get => _currentPlaylist;
        set => this.RaiseAndSetIfChanged(ref _currentPlaylist, value);
    }

    public HierarchicalTreeDataGridSource<ITreeNode> Source
    {
        get
        {
            if (_source != null) return _source;
            _source = new HierarchicalTreeDataGridSource<ITreeNode>(
                [.. FilteredDuplicateGroups]
            );

            // Album Image Column
            _source.Columns.Add(new TemplateColumn<ITreeNode>(
                "Album",
                new AlbumImageTemplate(),
                width: new GridLength(60, GridUnitType.Pixel)
            ));

            _source.Columns.Add(new HierarchicalExpanderColumn<ITreeNode>(
                new TextColumn<ITreeNode, string>(
                    "Track Name",
                    item => GetDisplayName(item),
                    new GridLength(1, GridUnitType.Star),
                    new TextColumnOptions<ITreeNode>
                    {
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                ),
                GetChildren
            ));

            _source.Columns.Add(new TextColumn<ITreeNode, string>(
                "Artists",
                item => GetDisplayArtist(item),
                new GridLength(1, GridUnitType.Star),
                new TextColumnOptions<ITreeNode>
                {
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            ));

            _source.Columns.Add(new TextColumn<ITreeNode, string>(
                "Duration/Count",
                item => GetDurationOrCount(item),
                new GridLength(100, GridUnitType.Pixel)
            ));

            _source.Columns.Add(new TemplateColumn<ITreeNode>(
                "Delete",
                new DeleteButtonTemplate(this),
                width: new GridLength(60, GridUnitType.Pixel)
            ));
            return _source;
        }
    }

    private static IEnumerable<ITreeNode>? GetChildren(ITreeNode item)
    {
        if (item is DuplicateGroup group)
        {
            return group.Tracks;
        }
        return null;
    }

    private static string GetDisplayName(ITreeNode item)
    {
        if (item is DuplicateGroup group)
            return group.DisplayName;
        if (item is TrackModel track)
            return track.Name;
        return string.Empty;
    }

    private static string GetDisplayArtist(ITreeNode item)
    {
        if (item is DuplicateGroup group)
            return group.DisplayArtist;
        if (item is TrackModel track)
            return track.ArtistNames;
        return string.Empty;
    }

    private static string GetDurationOrCount(ITreeNode item)
    {
        if (item is DuplicateGroup group)
            return $"{group.DuplicateCount} track(s)";
        if (item is TrackModel track)
            return track.Duration;
        return string.Empty;
    }

    private class DeleteButtonTemplate(DuplicatesViewModel viewModel) : IDataTemplate
    {
        public Control Build(object? param)
        {
            if (param is TrackModel track)
            {
                var button = new Button
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Width = 32,
                    Height = 32,
                    Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Command = viewModel.DeleteDuplicateCommand,
                    CommandParameter = track
                };

                var viewbox = new Viewbox
                {
                    Width = 16,
                    Height = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(-2, -2, 2, 2)
                };

                var path = new Path
                {
                    Data = Geometry.Parse("M6,19C6,20.1 6.9,21 8,21H16C17.1,21 18,20.1 18,19V7H6V19M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19V4Z"),
                    Fill = new SolidColorBrush(Color.Parse("#ff5252"))
                };

                viewbox.Child = path;
                button.Content = viewbox;
                return button;
            }
            return new Border();
        }

        public bool Match(object? data)
        {
            return data is ITreeNode;
        }
    }

    private class AlbumImageTemplate : IDataTemplate
    {
        public Control Build(object? param)
        {
            var image = new Image 
            { 
                Width = 40, 
                Height = 40, 
                Stretch = Stretch.Uniform 
            };
            
            if (param is ITreeNode node && !string.IsNullOrEmpty(node.DisplayImage))
            {
                ImageLoader.SetSource(image, node.DisplayImage);
            }
            
            return new Border
            {
                Width = 50,
                Height = 50,
                Margin = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = image
            };
        }

        public bool Match(object? data)
        {
            return data is ITreeNode;
        }
    }

    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler? BackToTracksView;
    public event EventHandler? DuplicatesRemoved;

    public DuplicatesViewModel(
        IDuplicatesService duplicatesService,
        ISpotifyService spotifyService
        )
    {
        _duplicatesService = duplicatesService;
        _spotifyService = spotifyService;

        FindDuplicatesCommand = ReactiveCommand.Create<ObservableCollection<TrackModel>>(FindDuplicates);
        RemoveAllDuplicatesCommand = ReactiveCommand.Create(RemoveAllDuplicates);
        DeleteDuplicateCommand = ReactiveCommand.Create<TrackModel>(DeleteDuplicate);
        BackToTracksCommand = ReactiveCommand.Create(() =>
        {
            IsDuplicatesViewVisible = false;
            BackToTracksView?.Invoke(this, EventArgs.Empty);
        });
        ResetFiltersCommand = ReactiveCommand.Create(() => SearchQuery = string.Empty);
    }

    public ICommand FindDuplicatesCommand { get; }
    public ICommand RemoveAllDuplicatesCommand { get; }
    private ICommand DeleteDuplicateCommand { get; }
    public ICommand BackToTracksCommand { get; }
    public ICommand ResetFiltersCommand { get; }

    private void FindDuplicates(ObservableCollection<TrackModel> tracks)
    {
        if (tracks.Count == 0)
        {
            OnStatusMessageChanged("No tracks to analyze for duplicates");
            return;
        }

        try
        {
            if (CurrentPlaylist == null && tracks.Any())
            {
                var mainViewModel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow?.DataContext as MainWindowViewModel
                    : null;

                if (mainViewModel != null)
                {
                    CurrentPlaylist = mainViewModel.TrackListViewModel.CurrentPlaylist;
                }
            }

            OnStatusMessageChanged("Finding duplicates...");
            var duplicates = _duplicatesService.FindDuplicates(tracks);

            DuplicateGroups = new ObservableCollection<DuplicateGroup>(duplicates);
            FilteredDuplicateGroups = new ObservableCollection<DuplicateGroup>(duplicates);
            SearchQuery = string.Empty;

            _source = null;
            this.RaisePropertyChanged(nameof(Source));

            if (DuplicateGroups.Count > 0)
            {
                IsDuplicatesViewVisible = true;
                OnStatusMessageChanged($"Found {DuplicateGroups.Count} duplicate groups");
            }
            else
            {
                OnStatusMessageChanged("No duplicates found in this playlist");
            }
        }
        catch (Exception ex)
        {
            OnStatusMessageChanged($"Error finding duplicates: {ex.Message}");
        }
    }

    private async void RemoveAllDuplicates()
    {
        try
        {
            if (DuplicateGroups.Count == 0 || CurrentPlaylist == null)
            {
                OnStatusMessageChanged("No duplicates to remove");
                return;
            }

            CancelCurrentOperations();
            var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

            try
            {
                var tracksToDelete = CollectTracksToDelete();

                if (tracksToDelete.Count == 0)
                {
                    OnStatusMessageChanged("No tracks to delete after applying rules");
                    return;
                }

                var successCount = 0;
                foreach (var track in tracksToDelete)
                {
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        if (CurrentPlaylist.IsLikedSongs)
                        {
                            await _spotifyService.RemoveTrackFromLikedSongs(track.Id, cancellationToken);
                        }
                        else
                        {
                            await _spotifyService.RemoveTrackFromPlaylist(CurrentPlaylist.Id, track.Uri, cancellationToken);
                        }

                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error removing track {track.Name}: {ex.Message}");
                    }
                }

                DuplicateGroups.Clear();
                IsDuplicatesViewVisible = false;
                OnStatusMessageChanged($"Successfully removed {successCount} of {tracksToDelete.Count} duplicate tracks using smart selection");

                DuplicatesRemoved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                OnStatusMessageChanged($"Error removing duplicates: {ex.Message}");
            }
        }
        catch (Exception e)
        {
            OnStatusMessageChanged($"Error processing duplicates: {e.Message}");
        }
    }

    private void DeleteDuplicate(TrackModel track)
    {
        if (CurrentPlaylist == null)
            return;

        CancelCurrentOperations();
        var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

        try
        {
            var group = DuplicateGroups.FirstOrDefault(g => g.Tracks.Contains(track));
            if (group == null)
                return;

            // Remove track from the group
            group.Tracks.Remove(track);

            // Create a brand-new source to force a complete refresh
            _source = null;
            
            // Create a new collection to force change notification
            DuplicateGroups = new ObservableCollection<DuplicateGroup>([.. DuplicateGroups]);

            // Explicitly raise property changed for the Source property
            this.RaisePropertyChanged(nameof(Source));

            Task.Run(async () =>
            {
                try
                {
                    if (CurrentPlaylist.IsLikedSongs)
                    {
                        await _spotifyService.RemoveTrackFromLikedSongs(track.Id, cancellationToken);
                    }
                    else
                    {
                        await _spotifyService.RemoveTrackFromPlaylist(CurrentPlaylist.Id, track.Uri, cancellationToken);
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        OnStatusMessageChanged($"Removed track: {track.Name}");

                        // Get reference to the TrackListViewModel to refresh the tracks
                        var mainViewModel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                            ? desktop.MainWindow?.DataContext as MainWindowViewModel
                            : null;

                        // Refresh the tracks without clearing the cache
                        mainViewModel?.TrackListViewModel.RefreshTracks(true);

                        if (group.Tracks.Count <= 1)
                        {
                            DuplicateGroups.Remove(group);

                            // Force refresh again if we remove a group
                            _source = null;
                            DuplicateGroups = new ObservableCollection<DuplicateGroup>([.. DuplicateGroups]);
                            this.RaisePropertyChanged(nameof(Source));
                        }

                        if (DuplicateGroups.Count != 0) return;
                        IsDuplicatesViewVisible = false;
                        BackToTracksView?.Invoke(this, EventArgs.Empty);
                        DuplicatesRemoved?.Invoke(this, EventArgs.Empty);
                    });
                }
                catch (Exception ex)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        OnStatusMessageChanged($"Error removing track: {ex.Message}");
                    });
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            OnStatusMessageChanged($"Error processing duplicate: {ex.Message}");
        }
    }

    private List<TrackModel> CollectTracksToDelete()
    {
        var tracksToDelete = new List<TrackModel>();

        foreach (var group in DuplicateGroups)
        {
            if (group.Tracks.Count <= 1)
                continue;

            var indexToKeep = _duplicatesService.GetTrackToKeepIndex(group);

            tracksToDelete.AddRange(group.Tracks.Where((_, i) => i != indexToKeep));
        }

        return tracksToDelete;
    }

    private void CancelCurrentOperations()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    private void OnStatusMessageChanged(string message)
    {
        StatusMessageChanged?.Invoke(this, message);
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void FilterDuplicates()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            FilteredDuplicateGroups = new ObservableCollection<DuplicateGroup>(DuplicateGroups);
        }
        else
        {
            var lowerCaseQuery = SearchQuery.ToLowerInvariant();
            FilteredDuplicateGroups = new ObservableCollection<DuplicateGroup>(
                DuplicateGroups.Where(group =>
                    group.DisplayName.ToLowerInvariant().Contains(lowerCaseQuery) ||
                    group.DisplayArtist.ToLowerInvariant().Contains(lowerCaseQuery) ||
                    group.Tracks.Any(track =>
                        track.Name.ToLowerInvariant().Contains(lowerCaseQuery) ||
                        track.ArtistNames.ToLowerInvariant().Contains(lowerCaseQuery)
                    )
                )
            );
        }

        // Refresh the source to reflect the filtered groups
        _source = null;
        this.RaisePropertyChanged(nameof(Source));
    }
}