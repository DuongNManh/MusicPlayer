using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Shapes;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using MusicPlayer.Services;
using MusicPlayer.Models;
using System.Windows.Input;
using System.Windows.Forms;
using NAudio.Wave;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using NAudio.Gui;
using System.Windows.Controls.Primitives;
using MusicPlayer.Animations;

namespace MusicPlayer.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MediaPlayer mediaPlayer = new MediaPlayer();
        private bool isDraggingSlider = false;
        private DispatcherTimer timer;
        private Random random = new Random();
        private List<SongInfo> originalPlaylist = new List<SongInfo>();
        private bool isShuffleEnabled = false;
        private bool isRepeatEnabled = false;
        private readonly MusicMetadataService _musicMetadataService;
        private readonly MetadataCacheService _cacheService;
        private AudioVisualizationService _visualizationService;
        //private WaveOutEvent _waveOutDevice;
        private AudioFileReader _audioFileReader;
        private DispatcherTimer visualizationTimer;
        private WaveOut silentOutput;
        private readonly BitmapCache _backgroundBitmapCache = new BitmapCache();
        private readonly BitmapCache _detailBackgroundBitmapCache = new BitmapCache();
        private SongInfo CurrentSong { get; set; }
        private int CurrentSongIndex { get; set; } = -1;
        private List<SongInfo> CurrentPlaylist => originalPlaylist;
        private bool isCurrentlyPlaying
        {
            get => mediaPlayer?.Source != null && PlayButton.Content.ToString() == "⏸";
            set
            {
                PlayButton.Content = value ? "⏸" : "▶";
                if (value)
                {
                    timer.Start();
                }
                else
                {
                    timer.Stop();
                }
            }
        }

        private bool isNavigationVisible = true;
        private const double NAV_WIDTH_EXPANDED = 200;
        private const double NAV_WIDTH_COLLAPSED = 45;
        private const double NAV_WIDTH_HIDDEN = 0;

        // Add this field to track the current view type
        private enum ViewType
        {
            AllSongs,
            ArtistSongs,
            AlbumSongs
        }
        private ViewType currentViewType = ViewType.AllSongs;

        public MainWindow()
        {
            InitializeComponent();
            InitializeBackgroundCaching();
            _cacheService = new MetadataCacheService();
            _musicMetadataService = new MusicMetadataService(_cacheService);
            InitializePlayer();
            InitializeVisualization();
            LoadCachedData();
            LoadSavedPlaylist();

            // Set initial navigation state
            NavColumn.Width = new GridLength(200);
            isNavigationVisible = true;

            // Add cleanup when window closes
            this.Closing += MainWindow_Closing;
        }

        private void InitializePlayer()
        {
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            timer.Tick += Timer_Tick;

            mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;

            this.Loaded += (s, e) => mediaPlayer.Volume = VolumeSlider.Value;
        }

        private void InitializeVisualization()
        {
            // Initialize visualization with 256 bars
            _visualizationService = new AudioVisualizationService(VisualizationCanvas, 256);
            //_waveOutDevice = new WaveOutEvent();
        }

        private void LoadCachedData()
        {
            try
            {
                // Load cached data from file if exists
                string cacheFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "metadata_cache.json");
                if (File.Exists(cacheFile))
                {
                    string jsonData = File.ReadAllText(cacheFile);
                    _cacheService.LoadFromJson(jsonData);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading cache: {ex.Message}");
            }
        }

        private void SavePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Playlist files (*.json)|*.json",
                DefaultExt = "json"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                var playlist = originalPlaylist.Select(s => s.FilePath).ToList();
                string jsonString = JsonSerializer.Serialize(playlist);
                File.WriteAllText(saveFileDialog.FileName, jsonString);
            }
        }

        private void ShufflePlaylist()
        {
            if (originalPlaylist == null || originalPlaylist.Count == 0)
                return;

            var currentSong = CurrentSong;
            var shuffledList = originalPlaylist.OrderBy(x => random.Next()).ToList();
            originalPlaylist = shuffledList;

            // Update current song index
            if (currentSong != null && shuffledList.Contains(currentSong))
            {
                CurrentSongIndex = shuffledList.IndexOf(currentSong);
            }
            else if (shuffledList.Count > 0)
            {
                CurrentSongIndex = 0;
                CurrentSong = shuffledList[0];
            }

            // Update any visible views
            UpdateViews();
        }

        private void RestoreOriginalPlaylist()
        {
            var currentSong = CurrentSong;
            originalPlaylist = originalPlaylist.OrderBy(s => s.FilePath).ToList();

            if (currentSong != null)
            {
                CurrentSongIndex = originalPlaylist.IndexOf(currentSong);
            }

            UpdateViews();
        }

        private void PlayMedia()
        {
            if (CurrentSong == null) return;

            try
            {
                // Get current view's playlist
                var currentViewList = GetCurrentSongList();

                // Verify the current song belongs to the current view context
                if (!currentViewList.Contains(CurrentSong))
                {
                    // If current song is not in the current view's context, stop playback
                    StopAndCleanupAudio();
                    return;
                }

                // Reset previous playing song
                foreach (var song in CurrentPlaylist)
                {
                    song.IsPlaying = false;
                }

                // Set new playing song
                CurrentSong.IsPlaying = true;

                // Stop and cleanup previous audio resources
                StopAndCleanupAudio();

                // Setup MediaPlayer for audio playback
                mediaPlayer.Open(new Uri(CurrentSong.FilePath));
                mediaPlayer.Play();

                // Setup visualization capture
                _audioFileReader = new AudioFileReader(CurrentSong.FilePath);
                var waveProvider = new WaveFloatTo16Provider(_audioFileReader);
                var captureProvider = new WasapiLoopbackCapture();

                captureProvider.DataAvailable += (s, e) =>
                {
                    if (e.Buffer.Length > 0)
                    {
                        var buffer = new float[e.Buffer.Length / 4];
                        Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.Buffer.Length);
                        _visualizationService.UpdateSpectrum(buffer);
                    }
                };

                captureProvider.StartRecording();

                // Update UI with transitions
                PlayButton.Content = "⏸";
                timer.Start();

                // Update now playing info
                NowPlayingTitle.Text = CurrentSong.Title;
                NowPlayingArtist.Text = CurrentSong.Artist;
                if (CurrentSong.AlbumArt != null && SongDetailView.Visibility != Visibility.Visible)
                {
                    NowPlayingImage.Source = CurrentSong.AlbumArt;
                    NowPlayingImage.Visibility = Visibility.Visible;
                }

                // If in detail view, update with transitions
                if (SongDetailView.Visibility == Visibility.Visible)
                {
                    ShowSongDetails(CurrentSong);
                }
                else
                {
                    // Update background for main view
                    UpdateBackgroundImage(CurrentSong.AlbumArt);
                }

                UpdateViews();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in PlayMedia: {ex.Message}");
                StopAndCleanupAudio();
            }
        }

        private void StopAndCleanupAudio()
        {
            try
            {
                silentOutput?.Stop();
                silentOutput?.Dispose();
                silentOutput = null;

                _audioFileReader?.Dispose();
                _audioFileReader = null;

                visualizationTimer?.Stop();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during audio cleanup: {ex.Message}");
            }
        }

        private void RemoveFromPlaylist(SongInfo song)
        {
            CurrentPlaylist.Remove(song);
        }

        private async void UpdateNowPlaying(SongInfo song)
        {
            if (song == null) return;

            try
            {
                // Update song info in playlist view
                if (CurrentSong == song)
                {
                    CurrentSong.Title = song.Title;
                    CurrentSong.Artist = song.Artist;
                    CurrentSong.Album = song.Album;

                    // Update UI elements
                    NowPlayingTitle.Text = song.Title;
                    NowPlayingArtist.Text = song.Artist;

                    // Update now playing image only if not in detail view
                    if (song.AlbumArt != null && SongDetailView.Visibility != Visibility.Visible)
                    {
                        NowPlayingImage.Source = song.AlbumArt;
                        NowPlayingImage.Visibility = Visibility.Visible;
                        await UpdateBackgroundImage(song.AlbumArt);
                    }

                    // Update detail view if it's visible
                    if (SongDetailView.Visibility == Visibility.Visible)
                    {
                        await UpdateSongDetailView(song);
                    }

                    // Update views to reflect changes
                    UpdateViews();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating now playing info: {ex.Message}");
            }
        }

        private async Task UpdateSongDetailView(SongInfo song)
        {
            try
            {
                // Update text content
                DetailTitleText.Text = song.Title;
                DetailArtistText.Text = song.Artist;
                DetailAlbumText.Text = song.Album;

                // Clear artist image first
                DetailArtistImage.Source = null;
                DetailArtistImageBrush.ImageSource = null;

                // Update album art and background
                if (song.AlbumArt != null)
                {
                    DetailAlbumArtImage.Source = song.AlbumArt;
                    await UpdateBackgroundImage(song.AlbumArt);
                }
                else
                {
                    await LoadAlbumArtAsync(song);
                }

                // Load artist image if available
                if (!string.IsNullOrEmpty(song.Artist) && song.Artist != "Unknown Artist")
                {
                    var artistImage = await LoadArtistImageAsync(song.Artist);
                    if (artistImage != null)
                    {
                        DetailArtistImage.Source = artistImage;
                        DetailArtistImageBrush.ImageSource = artistImage;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating song detail view: {ex.Message}");
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlayButton.Content.ToString() == "▶")
            {
                mediaPlayer.Play();
                PlayButton.Content = "⏸";
                timer.Start();
            }
            else
            {
                mediaPlayer.Pause();
                PlayButton.Content = "▶";
                timer.Stop();
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!isDraggingSlider && mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                ProgressSlider.Value = mediaPlayer.Position.TotalSeconds;
                TimeSpan currentPosition = TimeSpan.FromSeconds(Math.Ceiling(mediaPlayer.Position.TotalSeconds));
                TimeSpan totalDuration = TimeSpan.FromSeconds(Math.Ceiling(mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds));

                CurrentTimeText.Text = currentPosition.ToString(@"mm\:ss");
                TotalTimeText.Text = totalDuration.ToString(@"mm\:ss");

                // Keep visualization in sync with audio position
                if (_audioFileReader != null)
                {
                    var targetPosition = (long)(mediaPlayer.Position.TotalSeconds * _audioFileReader.WaveFormat.AverageBytesPerSecond);
                    if (Math.Abs(_audioFileReader.Position - targetPosition) > _audioFileReader.WaveFormat.AverageBytesPerSecond)
                    {
                        _audioFileReader.Position = targetPosition;
                    }
                }
            }
        }

        private void MediaPlayer_MediaOpened(object sender, EventArgs e)
        {
            if (mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                double totalSeconds = Math.Ceiling(mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds);
                ProgressSlider.Maximum = totalSeconds;
                TimeSpan duration = TimeSpan.FromSeconds(totalSeconds);
                TotalTimeText.Text = duration.ToString(@"mm\:ss");
            }
        }

        private void MediaPlayer_MediaEnded(object sender, EventArgs e)
        {
            if (isRepeatEnabled)
            {
                PlayMedia(); // Replay current song
                return;
            }

            if (isShuffleEnabled)
            {
                PlayNextShuffledSong();
                return;
            }

            var currentViewList = GetCurrentSongList();
            int nextIndex = currentViewList.IndexOf(CurrentSong) + 1;

            if (nextIndex >= currentViewList.Count)
            {
                nextIndex = 0; // Loop back to start
            }

            if (nextIndex < currentViewList.Count)
            {
                CurrentSong = currentViewList[nextIndex];
                PlayMedia();
            }
            else
            {
                StopAndCleanupAudio();
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            mediaPlayer.Volume = VolumeSlider.Value;
        }

        private void ProgressSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            isDraggingSlider = true;
        }

        private void ProgressSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            isDraggingSlider = false;
            var position = TimeSpan.FromSeconds(ProgressSlider.Value);
            mediaPlayer.Position = position;

            if (_audioFileReader != null)
            {
                _audioFileReader.Position = (long)(position.TotalSeconds * _audioFileReader.WaveFormat.AverageBytesPerSecond);
            }
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRepeatEnabled)
            {
                // If repeat is enabled, restart the current song
                mediaPlayer.Position = TimeSpan.Zero;
                PlayMedia();
                return;
            }

            if (isShuffleEnabled)
            {
                PlayPreviousShuffledSong();
                return;
            }

            var currentViewList = GetCurrentSongList();
            int prevIndex = currentViewList.IndexOf(CurrentSong) - 1;

            if (prevIndex < 0)
            {
                prevIndex = currentViewList.Count - 1; // Loop to end
            }

            if (prevIndex >= 0 && prevIndex < currentViewList.Count)
            {
                CurrentSong = currentViewList[prevIndex];
                PlayMedia();
            }
        }

        private void PlayPreviousShuffledSong()
        {
            var currentViewList = GetCurrentSongList();
            var previousSongs = currentViewList
                .Where(s => s != CurrentSong)
                .OrderBy(x => random.Next())
                .ToList();

            if (previousSongs.Any())
            {
                CurrentSong = previousSongs.First();
                CurrentSongIndex = currentViewList.IndexOf(CurrentSong);
                PlayMedia();
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRepeatEnabled)
            {
                PlayMedia(); // Replay current song
                return;
            }

            if (isShuffleEnabled)
            {
                PlayNextShuffledSong();
                return;
            }

            var currentViewList = GetCurrentSongList();
            int nextIndex = currentViewList.IndexOf(CurrentSong) + 1;

            if (nextIndex >= currentViewList.Count)
            {
                nextIndex = 0; // Loop back to start
            }

            if (nextIndex < currentViewList.Count)
            {
                CurrentSong = currentViewList[nextIndex];
                PlayMedia();
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Save cache to file
                string cacheFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "metadata_cache.json");
                string jsonData = _cacheService.SaveToJson();
                File.WriteAllText(cacheFile, jsonData);

                // Stop playback first
                mediaPlayer.Stop();
                timer?.Stop();

                // Cleanup visualization resources
                if (_visualizationService != null)
                {
                    _visualizationService.Dispose();
                    _visualizationService = null;
                }

                // Cleanup audio resources in order
                StopAndCleanupAudio();

                // Final cleanup
                mediaPlayer.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        private void SongListItem_Click(object sender, MouseButtonEventArgs e)
        {
            var frameworkElement = sender as FrameworkElement;
            if (frameworkElement?.DataContext is SongInfo song)
            {
                // If clicking the currently playing song, just show details
                if (song == CurrentSong && song.IsPlaying)
                {
                    ShowSongDetails(song);
                    return;
                }

                // Reset previous playing song indicators
                foreach (var s in CurrentPlaylist)
                {
                    s.IsPlaying = false;
                }

                // Get the current view's song list
                var currentViewList = GetCurrentSongList();

                // Set new song as current and update index based on the current view
                CurrentSong = song;
                CurrentSongIndex = currentViewList.IndexOf(song);

                // Only update the original playlist if we're in AllSongs view
                if (currentViewType == ViewType.AllSongs)
                {
                    originalPlaylist = new List<SongInfo>(currentViewList);
                }

                song.IsPlaying = true;

                // Play the selected song
                PlayMedia();

                // Show song details
                ShowSongDetails(song);
            }
        }

        // Modify GetCurrentSongList to use the current view type
        private List<SongInfo> GetCurrentSongList()
        {
            switch (currentViewType)
            {
                case ViewType.ArtistSongs:
                    return originalPlaylist
                        .Where(s => s.Artist == CurrentSong?.Artist)
                        .ToList();
                case ViewType.AlbumSongs:
                    return originalPlaylist
                        .Where(s => s.Album == CurrentSong?.Album && s.Artist == CurrentSong?.Artist)
                        .ToList();
                case ViewType.AllSongs:
                default:
                    return originalPlaylist;
            }
        }

        private void ClearSongDetails()
        {
            try
            {
                // Clear background and album art
                DetailBackgroundImage.Source = null;
                DetailAlbumArtImage.Source = null;
                DetailArtistImage.Source = null;

                // Clear text content
                DetailTitleText.Text = string.Empty;
                DetailArtistText.Text = string.Empty;
                DetailAlbumText.Text = string.Empty;

                // Clear MainBackgroundImage if it exists
                MainBackgroundImage.Source = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing song details: {ex.Message}");
            }
        }

        private async void ShowSongDetails(SongInfo song)
        {
            if (song == null) return;

            try
            {
                // Clear previous artist image first
                ClearArtistImage();

                // Disable visualization when showing song details
                _visualizationService?.SetActive(true);

                // Disable navigation panel completely
                var animation = new GridLengthAnimation
                {
                    Duration = TimeSpan.FromMilliseconds(100),
                    From = new GridLength(NavColumn.ActualWidth),
                    To = new GridLength(NAV_WIDTH_HIDDEN),
                    EasingFunction = new QuadraticEase()
                };

                NavColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
                isNavigationVisible = false;
                ToggleNavButton.Content = "☰";

                // Hide navigation text
                UpdateNavigationTextVisibility(Visibility.Collapsed);

                // Create fade animations
                var fadeOutAnimation = CreateFadeAnimation(1.0, 0.0, 300);
                var fadeInAnimation = CreateFadeAnimation(0.0, 1.0, 300);

                // Fade out current content
                UpdateTransitionAnimations(fadeOutAnimation);

                // Wait for fade out to complete
                await Task.Delay(300);

                // Clear previous song details
                ClearSongDetails();

                // Update text content
                UpdateDetailViewContent(song);

                // Load album art
                if (song.AlbumArt != null)
                {
                    DetailAlbumArtImage.Source = song.AlbumArt;
                    await UpdateBackgroundImage(song.AlbumArt);
                }
                else
                {
                    await LoadAlbumArtAsync(song);
                }

                // Load artist image if available
                if (!string.IsNullOrEmpty(song.Artist) && song.Artist != "Unknown Artist")
                {
                    var artistImage = await LoadArtistImageAsync(song.Artist);
                    if (artistImage != null)
                    {
                        DetailArtistImage.Source = artistImage;
                        DetailArtistImageBrush.ImageSource = artistImage;
                    }
                }

                // Switch views if needed
                if (SongDetailView.Visibility != Visibility.Visible)
                {
                    SwitchView(Visibility.Collapsed, Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible);

                    // Hide now playing image when entering detail view
                    NowPlayingImage.Visibility = Visibility.Collapsed;
                }

                // Fade in new content
                UpdateTransitionAnimations(fadeInAnimation);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing song details: {ex.Message}");
            }
        }

        private void UpdateTransitionAnimations(AnimationTimeline animationType)
        {
            DetailAlbumArtImage.BeginAnimation(UIElement.OpacityProperty, animationType);
            DetailBackgroundImage.BeginAnimation(UIElement.OpacityProperty, animationType);
            DetailArtistImage.BeginAnimation(UIElement.OpacityProperty, animationType);
            DetailTitleText.BeginAnimation(UIElement.OpacityProperty, animationType);
            DetailArtistText.BeginAnimation(UIElement.OpacityProperty, animationType);
            DetailAlbumText.BeginAnimation(UIElement.OpacityProperty, animationType);
        }

        // Add this helper method to properly dispose of artist images
        private void ClearArtistImage()
        {
            try
            {
                // Clear both the image source and brush
                DetailArtistImage.Source = null;
                DetailArtistImageBrush.ImageSource = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing artist image: {ex.Message}");
            }
        }

        // Update the LoadArtistImageAsync method
        private async Task<BitmapImage> LoadArtistImageAsync(string artist)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading image for artist: {artist}");

                // Clear previous artist image first
                ClearArtistImage();

                // Load new artist image
                var artistImage = await _musicMetadataService.GetArtistImageAsync(artist);
                if (artistImage != null)
                {
                    System.Diagnostics.Debug.WriteLine("Artist image loaded successfully");
                    return artistImage;
                }

                System.Diagnostics.Debug.WriteLine("No artist image found");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading artist image: {ex.Message}");
                return null;
            }
        }

        // Update the LoadArtistImage method to use the async version
        private async void LoadArtistImage(string artist)
        {
            DetailArtistImage.Source = null;
            await LoadArtistImageAsync(artist);
        }

        private async Task LoadAlbumArtAsync(SongInfo song)
        {
            try
            {
                await song.LoadAlbumArtAsync();
                if (song.AlbumArt != null)
                {
                    DetailBackgroundImage.Source = song.AlbumArt;
                    DetailAlbumArtImage.Source = song.AlbumArt;
                    MainBackgroundImage.Source = song.AlbumArt;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading album art: {ex.Message}");
            }
        }

        private void BackToLibrary_Click(object sender, RoutedEventArgs e)
        {
            // close visualization when returning to library
            _visualizationService?.SetActive(false);

            // Restore navigation panel to its previous state (expanded or collapsed)
            var targetWidth = isNavigationVisible ? NAV_WIDTH_EXPANDED : NAV_WIDTH_COLLAPSED;
            var animation = new GridLengthAnimation
            {
                Duration = TimeSpan.FromMilliseconds(100),
                From = new GridLength(NavColumn.ActualWidth),
                To = new GridLength(targetWidth),
                EasingFunction = new QuadraticEase()
            };

            NavColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);

            // Update text visibility based on navigation state
            UpdateNavigationTextVisibility(isNavigationVisible ? Visibility.Visible : Visibility.Collapsed);

            // Switch back to library view
            SongDetailView.Visibility = Visibility.Collapsed;
            SongListView.Visibility = Visibility.Visible;

            // Show the now playing image if there's a song playing
            if (CurrentSong != null && CurrentSong.AlbumArt != null)
            {
                NowPlayingImage.Source = CurrentSong.AlbumArt;
                NowPlayingImage.Visibility = Visibility.Visible;
            }
            else
            {
                NowPlayingImage.Visibility = Visibility.Collapsed;
            }
        }

        private void ViewSongs_Click(object sender, RoutedEventArgs e)
        {
            SwitchView(Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, Visibility.Collapsed);
            currentViewType = ViewType.AllSongs;
            SongListView.ItemsSource = originalPlaylist;
        }

        private async void ViewAlbums_Click(object sender, RoutedEventArgs e)
        {
            SwitchView(Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed);
            var albums = await _cacheService.GetAlbumGroupsAsync(CurrentPlaylist);
            AlbumsView.ItemsSource = albums;
        }

        private async void ViewArtists_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SwitchView(Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed);
                var artists = await _cacheService.GetArtistGroupsAsync(CurrentPlaylist);
                ArtistsView.ItemsSource = artists;

                // Fetch artist images asynchronously
                foreach (var artist in artists)
                {
                    try
                    {
                        // Check if we need to fetch a new image
                        bool needsNewImage = artist.ArtistImage == null;
                        if (artist.ArtistImage?.UriSource != null)
                        {
                            string imageSource = artist.ArtistImage.UriSource.ToString();
                            needsNewImage = imageSource.Contains("default_artist");
                        }

                        if (needsNewImage)
                        {
                            var artistImage = await _musicMetadataService.GetArtistImageAsync(artist.Name);
                            if (artistImage != null)
                            {
                                artist.ArtistImage = artistImage;
                            }
                            else
                            {
                                // If we couldn't get an image from the API, ensure we have at least the default
                                artist.SetDefaultImage();
                            }
                        }
                    }
                    catch (Exception artistEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading image for artist {artist.Name}: {artistEx.Message}");
                        // Set default image if there's an error
                        artist.SetDefaultImage();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ViewArtists_Click: {ex.Message}");
            }
        }

        private void ArtistItem_Click(object sender, MouseButtonEventArgs e)
        {
            var frameworkElement = sender as FrameworkElement;
            if (frameworkElement?.DataContext is ArtistInfo artistInfo)
            {
                ViewArtistSongs(artistInfo);
            }
        }

        private void AlbumItem_Click(object sender, MouseButtonEventArgs e)
        {
            var frameworkElement = sender as FrameworkElement;
            if (frameworkElement?.DataContext is AlbumInfo albumInfo)
            {
                ViewAlbumSongs(albumInfo);
            }
        }

        private void UpdateViews()
        {
            try
            {
                // Update Songs View
                SongListView.ItemsSource = null;
                SongListView.ItemsSource = CurrentPlaylist;

                // Update Albums View with proper song count
                var albumGroups = CurrentPlaylist
                    .GroupBy(s => new { s.Album, s.Artist })
                    .Select(g => new AlbumInfo
                    {
                        Title = g.Key.Album,
                        Artist = g.Key.Artist,
                        AlbumArt = g.First().AlbumArt as BitmapImage,
                        SongCount = g.Count(),
                        LastUpdated = DateTime.Now
                    })
                    .ToList();
                AlbumsView.ItemsSource = albumGroups;

                // Update Artists View with proper song count
                var artistGroups = CurrentPlaylist
                    .GroupBy(s => s.Artist)
                    .Select(g => new ArtistInfo
                    {
                        Name = g.Key,
                        ArtistImage = g.First().AlbumArt as BitmapImage,
                        SongCount = g.Count(),
                        LastUpdated = DateTime.Now
                    })
                    .ToList();
                ArtistsView.ItemsSource = artistGroups;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating views: {ex.Message}");
            }
        }

        private void AddSongToPlaylist(string filePath)
        {
            if (!CurrentPlaylist.Any(s => s.FilePath == filePath))
            {
                var songInfo = new SongInfo(filePath);
                originalPlaylist.Add(songInfo);
                UpdateViews();
            }
        }

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Audio Files|*.mp3;*.m4a;*.wav;*.flac|All Files|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string filename in openFileDialog.FileNames)
                {
                    AddSongToPlaylist(filename);
                }
                UpdateViews();
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string[] files = Directory.GetFiles(folderDialog.SelectedPath, "*.*", SearchOption.AllDirectories)
                    .Where(file => file.ToLower().EndsWith(".mp3") ||
                                  file.ToLower().EndsWith(".m4a") ||
                                  file.ToLower().EndsWith(".flac") ||
                                  file.ToLower().EndsWith(".wav"))
                    .ToArray();

                foreach (string file in files)
                {
                    AddSongToPlaylist(file);
                }
                UpdateViews();
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = ((System.Windows.Controls.TextBox)sender).Text.ToLower();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                // If search is empty, restore original playlist
                SongListView.ItemsSource = originalPlaylist;
                return;
            }

            // Filter songs based on search text
            var filteredSongs = originalPlaylist.Where(song =>
                song.Title?.ToLower().Contains(searchText) == true ||
                song.Artist?.ToLower().Contains(searchText) == true ||
                song.Album?.ToLower().Contains(searchText) == true
            ).ToList();

            // Update the ListView with filtered results
            SongListView.ItemsSource = filteredSongs;

            // Switch to Songs view if not already visible
            SongListView.Visibility = Visibility.Visible;
            AlbumsView.Visibility = Visibility.Collapsed;
            ArtistsView.Visibility = Visibility.Collapsed;
            SongDetailView.Visibility = Visibility.Collapsed;
        }

        private void ViewArtistSongs(ArtistInfo artistInfo)
        {
            if (artistInfo == null) return;

            // Filter songs by artist
            var artistSongs = originalPlaylist
                .Where(s => s.Artist == artistInfo.Name)
                .ToList();

            // Update Songs view with filtered songs
            currentViewType = ViewType.ArtistSongs;
            SongListView.ItemsSource = artistSongs;

            // Switch to Songs view
            SongListView.Visibility = Visibility.Visible;
            AlbumsView.Visibility = Visibility.Collapsed;
            ArtistsView.Visibility = Visibility.Collapsed;
            SongDetailView.Visibility = Visibility.Collapsed;
        }

        private void ViewAlbumSongs(AlbumInfo albumInfo)
        {
            if (albumInfo == null) return;

            // Filter songs for this album
            var albumSongs = originalPlaylist
                .Where(s => s.Album == albumInfo.Title && s.Artist == albumInfo.Artist)
                .ToList();

            // Update Songs view with filtered songs
            currentViewType = ViewType.AlbumSongs;
            SongListView.ItemsSource = albumSongs;

            // Switch to Songs view
            SongListView.Visibility = Visibility.Visible;
            AlbumsView.Visibility = Visibility.Collapsed;
            ArtistsView.Visibility = Visibility.Collapsed;
            SongDetailView.Visibility = Visibility.Collapsed;
        }

        private void ViewPlaylist_Click(object sender, RoutedEventArgs e)
        {
            SwitchView(Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, Visibility.Collapsed);
            var upcomingPlaylist = GetUpcomingPlaylist();
            SongListView.ItemsSource = upcomingPlaylist;
        }

        private List<SongInfo> GetUpcomingPlaylist()
        {
            var playlist = new List<SongInfo>();
            var currentIndex = CurrentSongIndex;

            if (currentIndex == -1) return playlist;

            // If shuffle is enabled, generate shuffled playlist
            if (isShuffleEnabled)
            {
                var remainingSongs = new List<SongInfo>();
                for (int i = 0; i < CurrentPlaylist.Count; i++)
                {
                    if (i != currentIndex)
                    {
                        remainingSongs.Add(CurrentPlaylist[i]);
                    }
                }

                // Shuffle the remaining songs
                var rng = new Random();
                var shuffledSongs = remainingSongs.OrderBy(x => rng.Next()).ToList();

                // Add current song at the beginning
                playlist.Add(CurrentPlaylist[currentIndex]);
                playlist.AddRange(shuffledSongs);
            }
            else
            {
                // Add songs in order starting from current
                for (int i = currentIndex; i < CurrentPlaylist.Count; i++)
                {
                    playlist.Add(CurrentPlaylist[i]);
                }

                // If repeat is enabled, add songs from beginning
                if (isRepeatEnabled && currentIndex > 0)
                {
                    for (int i = 0; i < currentIndex; i++)
                    {
                        playlist.Add(CurrentPlaylist[i]);
                    }
                }
            }

            return playlist;
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            // If repeat is enabled, disable it first
            if (isRepeatEnabled)
            {
                isRepeatEnabled = false;
                UpdateRepeatButtonStyle();
            }

            isShuffleEnabled = !isShuffleEnabled;
            UpdateShuffleButtonStyle();

            // If shuffle is enabled, shuffle the playlist
            if (isShuffleEnabled)
            {
                ShufflePlaylist();
            }
            else
            {
                RestoreOriginalPlaylist();
            }

            // If we're in playlist view, refresh it
            if (SongListView.IsVisible && SongListView.ItemsSource != originalPlaylist)
            {
                ViewPlaylist_Click(sender, e);
            }
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            // If shuffle is enabled, disable it first
            if (isShuffleEnabled)
            {
                isShuffleEnabled = false;
                UpdateShuffleButtonStyle();
                RestoreOriginalPlaylist();
            }

            isRepeatEnabled = !isRepeatEnabled;
            UpdateRepeatButtonStyle();

            // If we're in playlist view, refresh it
            if (SongListView.IsVisible && SongListView.ItemsSource != CurrentPlaylist)
            {
                ViewPlaylist_Click(sender, e);
            }
        }

        private void UpdateShuffleButtonStyle()
        {
            ShuffleButton.Background = isShuffleEnabled ?
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 215, 96)) :
                new SolidColorBrush(Colors.Transparent);
        }

        private void UpdateRepeatButtonStyle()
        {
            RepeatButton.Background = isRepeatEnabled ?
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 215, 96)) :
                new SolidColorBrush(Colors.Transparent);
        }

        private void NowPlaying_Click(object sender, MouseButtonEventArgs e)
        {
            if (CurrentSong != null)
            {
                ShowSongDetails(CurrentSong);
            }
        }

        private void LoadSavedPlaylist()
        {
            try
            {
                string playlistFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "saved_playlist.json");
                if (File.Exists(playlistFile))
                {
                    string jsonString = File.ReadAllText(playlistFile);
                    var savedPaths = JsonSerializer.Deserialize<List<string>>(jsonString);

                    foreach (string path in savedPaths)
                    {
                        if (File.Exists(path)) // Only add if file still exists
                        {
                            AddSongToPlaylist(path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading saved playlist: {ex.Message}");
            }
        }

        private void SaveCurrentPlaylist()
        {
            try
            {
                string playlistFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "saved_playlist.json");
                var playlist = originalPlaylist.Select(s => s.FilePath).ToList();
                string jsonString = JsonSerializer.Serialize(playlist);
                File.WriteAllText(playlistFile, jsonString);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving playlist: {ex.Message}");
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveCurrentPlaylist();
            base.OnClosing(e);
        }

        private void InitializeBackgroundCaching()
        {
            // Set up bitmap caching for background images
            if (MainBackgroundImage != null)
            {
                MainBackgroundImage.CacheMode = _backgroundBitmapCache;
            }
            if (DetailBackgroundImage != null)
            {
                DetailBackgroundImage.CacheMode = _detailBackgroundBitmapCache;
            }
        }

        private async Task UpdateBackgroundImage(ImageSource source)
        {
            try
            {
                if (source == null)
                {
                    MainBackgroundImage.Source = null;
                    DetailBackgroundImage.Source = null;
                    return;
                }

                // Create a smaller version for background
                var bitmap = source as BitmapSource;
                if (bitmap != null)
                {
                    // Scale down the image for background use
                    var scaledBitmap = new TransformedBitmap(bitmap,
                        new ScaleTransform(0.5, 0.5)); // Reduce size by 50%

                    // Freeze the bitmap to improve performance
                    if (scaledBitmap.CanFreeze)
                    {
                        scaledBitmap.Freeze();
                    }
                    DetailAlbumArtImage.Source = bitmap;
                    MainBackgroundImage.Source = scaledBitmap;
                    DetailBackgroundImage.Source = scaledBitmap;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating background: {ex.Message}");
            }
        }

        //// Update PlayNextSong method
        //private void PlayNextSong()
        //{
        //    if (originalPlaylist.Count == 0) return;

        //    if (isRepeatEnabled && CurrentSong != null)
        //    {
        //        PlaySong(CurrentSong);
        //        return;
        //    }

        //    // Get the current filtered playlist
        //    var currentViewList = GetCurrentSongList();
        //    int nextIndex = currentViewList.IndexOf(CurrentSong) + 1;

        //    if (nextIndex >= currentViewList.Count)
        //    {
        //        nextIndex = 0; // Loop back to start
        //    }

        //    if (nextIndex < currentViewList.Count)
        //    {
        //        var nextSong = currentViewList[nextIndex];
        //        PlaySong(nextSong);
        //    }
        //}

        //// Update PlayPreviousSong method
        //private void PlayPreviousSong()
        //{
        //    if (originalPlaylist.Count == 0) return;

        //    // Get the current filtered playlist
        //    var currentViewList = GetCurrentSongList();
        //    int prevIndex = currentViewList.IndexOf(CurrentSong) - 1;

        //    if (prevIndex < 0)
        //    {
        //        prevIndex = currentViewList.Count - 1; // Loop to end
        //    }

        //    if (prevIndex >= 0 && prevIndex < currentViewList.Count)
        //    {
        //        var prevSong = currentViewList[prevIndex];
        //        PlaySong(prevSong);
        //    }
        //}

        private void PlaySong(SongInfo song)
        {
            if (song == null) return;

            CurrentSong = song;
            // Get the current filtered playlist based on view type
            var currentViewList = GetCurrentSongList();
            CurrentSongIndex = currentViewList.IndexOf(song);

            // Update UI to show current song is playing
            foreach (var s in originalPlaylist)
            {
                s.IsPlaying = (s == song);
            }

            // Play the song
            mediaPlayer.Open(new Uri(song.FilePath));
            mediaPlayer.Play();
            UpdateNowPlaying(song);

            // If in detail view, update with transitions
            if (SongDetailView.Visibility == Visibility.Visible)
            {
                ShowSongDetails(song);
            }

            UpdateViews();
        }

        private void PlayNextShuffledSong()
        {
            // Get the current filtered playlist
            var currentViewList = GetCurrentSongList();
            var remainingSongs = currentViewList
                .Where(s => s != CurrentSong)
                .OrderBy(x => random.Next())
                .ToList();

            if (remainingSongs.Any())
            {
                CurrentSong = remainingSongs.First();
                CurrentSongIndex = currentViewList.IndexOf(CurrentSong);
                PlayMedia();
            }
            else
            {
                // If no more songs, stop playback
                mediaPlayer.Stop();
                PlayButton.Content = "▶";
                timer.Stop();
            }
        }

        private void ToggleNavigation_Click(object sender, RoutedEventArgs e)
        {
            if (SongDetailView.Visibility == Visibility.Visible)
            {
                // Don't toggle if in detail view
                return;
            }

            isNavigationVisible = !isNavigationVisible;
            var targetWidth = isNavigationVisible ? NAV_WIDTH_EXPANDED : NAV_WIDTH_COLLAPSED;

            // Create animation for GridLength
            var animation = new GridLengthAnimation
            {
                Duration = TimeSpan.FromMilliseconds(250),
                From = new GridLength(NavColumn.ActualWidth),
                To = new GridLength(targetWidth),
                EasingFunction = new QuadraticEase()
            };

            // Update visibility of text
            UpdateNavigationTextVisibility(isNavigationVisible ? Visibility.Visible : Visibility.Collapsed);

            // Update toggle button content
            ToggleNavButton.Content = isNavigationVisible ? "≡" : "☰";

            NavColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
        }

        // Add this helper method to consolidate view switching logic
        private void SwitchView(Visibility songList, Visibility albums, Visibility artists, Visibility details)
        {
            SongListView.Visibility = songList;
            AlbumsView.Visibility = albums;
            ArtistsView.Visibility = artists;
            SongDetailView.Visibility = details;
        }

        // Add helper method for creating animations
        private DoubleAnimation CreateFadeAnimation(double from, double to, double durationMs)
        {
            return new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase()
            };
        }

        //private GridLengthAnimation CreateGridLengthAnimation(GridLength from, GridLength to, double durationMs)
        //{
        //    return new GridLengthAnimation
        //    {
        //        Duration = TimeSpan.FromMilliseconds(durationMs),
        //        From = from,
        //        To = to,
        //        EasingFunction = new QuadraticEase()
        //    };
        //}

        // Add helper method for updating detail view elements
        private void UpdateDetailViewContent(SongInfo song)
        {
            DetailTitleText.Text = song?.Title ?? string.Empty;
            DetailArtistText.Text = song?.Artist ?? string.Empty;
            DetailAlbumText.Text = song?.Album ?? string.Empty;
            DetailAlbumArtImage.Source = song?.AlbumArt;
        }

        // Use this helper in ShowSongDetails and UpdateSongDetailView

        // Add helper method for common playback operations
        //private void UpdatePlaybackState(bool isPlaying)
        //{
        //    PlayButton.Content = isPlaying ? "⏸" : "▶";
        //    if (isPlaying)
        //    {
        //        timer.Start();
        //        mediaPlayer.Play();
        //    }
        //    else
        //    {
        //        timer.Stop();
        //        mediaPlayer.Pause();
        //    }
        //}

        //// Use this in PlayButton_Click and other playback control methods

        //// Add helper method for updating background images
        //private void UpdateBackgroundImages(ImageSource source)
        //{
        //    MainBackgroundImage.Source = source;
        //    DetailBackgroundImage.Source = source;
        //}

        // Use this helper instead of setting sources individually

        private void UpdateNavigationTextVisibility(Visibility visibility)
        {
            AddFilesText.Visibility = visibility;
            AddFolderText.Visibility = visibility;
            SongsText.Visibility = visibility;
            ArtistsText.Visibility = visibility;
            AlbumsText.Visibility = visibility;
            PlaylistText.Visibility = visibility;
        }
    }
}