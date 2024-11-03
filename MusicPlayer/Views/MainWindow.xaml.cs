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
        private WaveOutEvent _waveOutDevice;
        private AudioFileReader _audioFileReader;
        private DispatcherTimer visualizationTimer;
        private WaveOut silentOutput;

        public MainWindow()
        {
            InitializeComponent();
            _cacheService = new MetadataCacheService();
            _musicMetadataService = new MusicMetadataService(_cacheService);
            InitializePlayer();
            InitializeVisualization();
            LoadCachedData();

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
            _waveOutDevice = new WaveOutEvent();
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

            var currentSong = PlaylistBox.SelectedItem as SongInfo;
            var shuffledList = originalPlaylist.OrderBy(x => random.Next()).ToList();

            PlaylistBox.Items.Clear();
            foreach (var song in shuffledList)
            {
                if (song != null)
                    PlaylistBox.Items.Add(song);
            }

            if (currentSong != null && shuffledList.Contains(currentSong))
            {
                PlaylistBox.SelectedItem = currentSong;
            }
            else if (PlaylistBox.Items.Count > 0)
            {
                PlaylistBox.SelectedIndex = 0;
            }
        }

        private void RestoreOriginalPlaylist()
        {
            var currentSong = PlaylistBox.SelectedItem as SongInfo;

            PlaylistBox.Items.Clear();
            foreach (var song in originalPlaylist)
            {
                PlaylistBox.Items.Add(song);
            }

            if (currentSong != null)
            {
                PlaylistBox.SelectedItem = currentSong;
            }
        }

        private void PlayMedia()
        {
            if (PlaylistBox.SelectedItem is SongInfo selectedSong)
            {
                try
                {
                    // Reset previous playing song
                    foreach (var song in originalPlaylist)
                    {
                        song.IsPlaying = false;
                    }

                    // Set new playing song
                    selectedSong.IsPlaying = true;

                    // Stop and cleanup previous audio resources
                    StopAndCleanupAudio();

                    // Setup MediaPlayer for audio playback
                    mediaPlayer.Open(new Uri(selectedSong.FilePath));
                    mediaPlayer.Play();

                    // Setup visualization capture
                    _audioFileReader = new AudioFileReader(selectedSong.FilePath);
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

                    // Update UI
                    PlayButton.Content = "⏸";
                    timer.Start();
                    UpdateNowPlaying(selectedSong);

                    // Refresh views to update UI
                    PlaylistBox.Items.Refresh();
                    SongListView.Items.Refresh();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error playing media: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
            originalPlaylist.Remove(song);
            PlaylistBox.Items.Remove(song);
        }

        private async void UpdateNowPlaying(SongInfo song)
        {
            if (song == null) return;

            try
            {
                // Update song info in playlist view
                if (PlaylistBox.SelectedItem is SongInfo selectedSong)
                {
                    selectedSong.Title = song.Title;
                    selectedSong.Artist = song.Artist;
                    selectedSong.Album = song.Album;

                    // Ensure album art is loaded
                    if (selectedSong.AlbumArt == null)
                    {
                        await selectedSong.LoadAlbumArtAsync();
                    }

                    // Update now playing bar
                    NowPlayingTitle.Text = song.Title;
                    NowPlayingArtist.Text = song.Artist;
                    NowPlayingImage.Source = song.AlbumArt;
                    PlayButton.Content = "⏸";
                }

                // Refresh both views
                PlaylistBox.Items.Refresh();
                SongListView.ItemsSource = new List<SongInfo>(originalPlaylist);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating UI: {ex.Message}");
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
            if (PlaylistBox.SelectedItem is SongInfo currentSong)
            {
                currentSong.IsPlaying = false;
            }

            if (isRepeatEnabled)
            {
                // If repeat is enabled, just replay the current song
                mediaPlayer.Position = TimeSpan.Zero;
                mediaPlayer.Play();
            }
            else if (isShuffleEnabled)
            {
                // If shuffle is enabled, play a random song
                var currentIndex = PlaylistBox.SelectedIndex;
                var remainingIndices = Enumerable.Range(0, PlaylistBox.Items.Count)
                    .Where(i => i != currentIndex)
                    .ToList();

                if (remainingIndices.Any())
                {
                    var randomIndex = remainingIndices[random.Next(remainingIndices.Count)];
                    PlaylistBox.SelectedIndex = randomIndex;
                }
                else
                {
                    // If no more songs, stop playback
                    mediaPlayer.Stop();
                    PlayButton.Content = "▶";
                    timer.Stop();
                }
            }
            else if (PlaylistBox.SelectedIndex < PlaylistBox.Items.Count - 1)
            {
                // Normal sequential playback
                PlaylistBox.SelectedIndex++;
            }
            else
            {
                // End of playlist reached
                mediaPlayer.Stop();
                PlayButton.Content = "▶";
                timer.Stop();
            }
        }

        private void PlaylistBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaylistBox.SelectedItem is SongInfo selectedSong)
            {
                try
                {
                    // Only update UI if the selected song is different from the currently playing song
                    bool isCurrentlyPlaying = selectedSong.IsPlaying;

                    // Reset previous playing song indicators
                    foreach (var s in originalPlaylist)
                    {
                        s.IsPlaying = false;
                    }

                    // Only start playing if no song is currently playing
                    if (!isCurrentlyPlaying)
                    {
                        PlayMedia();
                    }

                    // Refresh both views to update UI
                    PlaylistBox.Items.Refresh();
                    SongListView.Items.Refresh();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error handling playlist selection: {ex.Message}");
                }
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
            }
            else if (isShuffleEnabled)
            {
                // If shuffle is enabled, play a random previous song
                var currentIndex = PlaylistBox.SelectedIndex;
                var previousIndices = Enumerable.Range(0, currentIndex).ToList();

                if (previousIndices.Any())
                {
                    var randomIndex = previousIndices[random.Next(previousIndices.Count)];
                    PlaylistBox.SelectedIndex = randomIndex;
                }
            }
            else if (PlaylistBox.SelectedIndex > 0)
            {
                // Normal sequential navigation
                PlaylistBox.SelectedIndex--;
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRepeatEnabled)
            {
                // If repeat is enabled, restart the current song
                mediaPlayer.Position = TimeSpan.Zero;
            }
            else if (isShuffleEnabled)
            {
                // If shuffle is enabled, play a random next song
                var currentIndex = PlaylistBox.SelectedIndex;
                var nextIndices = Enumerable.Range(currentIndex + 1, PlaylistBox.Items.Count - currentIndex - 1).ToList();

                if (nextIndices.Any())
                {
                    var randomIndex = nextIndices[random.Next(nextIndices.Count)];
                    PlaylistBox.SelectedIndex = randomIndex;
                }
            }
            else if (PlaylistBox.SelectedIndex < PlaylistBox.Items.Count - 1)
            {
                // Normal sequential navigation
                PlaylistBox.SelectedIndex++;
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
                // If the song is already playing, just show details
                if (song.IsPlaying)
                {
                    ShowSongDetails(song);
                }
                // Otherwise, select it in playlist and play it
                else
                {
                    PlaylistBox.SelectedItem = song;
                    ShowSongDetails(song);
                    PlayMedia();
                }
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

        private void ShowSongDetails(SongInfo song)
        {
            if (song == null) return;

            try
            {
                // Clear previous song details first
                ClearSongDetails();

                // Update background and album art
                if (song.AlbumArt != null)
                {
                    DetailBackgroundImage.Source = song.AlbumArt;
                    DetailAlbumArtImage.Source = song.AlbumArt;
                    MainBackgroundImage.Source = song.AlbumArt;
                }

                // Update text content
                DetailTitleText.Text = song.Title;
                DetailArtistText.Text = song.Artist;
                DetailAlbumText.Text = song.Album;

                // Load artist image if available
                if (!string.IsNullOrEmpty(song.Artist) && song.Artist != "Unknown Artist")
                {
                    LoadArtistImage(song.Artist);
                }

                // Switch views
                SongListView.Visibility = Visibility.Collapsed;
                AlbumsView.Visibility = Visibility.Collapsed;
                ArtistsView.Visibility = Visibility.Collapsed;
                SongDetailView.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing song details: {ex.Message}");
            }
        }

        private async void LoadArtistImage(string artist)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading image for artist: {artist}");
                // Clear previous artist image first
                DetailArtistImage.Source = null;

                // Load new artist image
                var artistImage = await _musicMetadataService.GetArtistImageAsync(artist);
                if (artistImage != null)
                {
                    DetailArtistImage.Source = artistImage;
                    System.Diagnostics.Debug.WriteLine("Artist image loaded successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No artist image found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading artist image: {ex.Message}");
            }
        }

        private void BackToLibrary_Click(object sender, RoutedEventArgs e)
        {
            // Clear song details when going back to library
            ClearSongDetails();

            // Switch back to library view
            SongDetailView.Visibility = Visibility.Collapsed;
            SongListView.Visibility = Visibility.Visible;
        }

        private void ViewSongs_Click(object sender, RoutedEventArgs e)
        {
            SongListView.Visibility = Visibility.Visible;
            AlbumsView.Visibility = Visibility.Collapsed;
            ArtistsView.Visibility = Visibility.Collapsed;
            SongDetailView.Visibility = Visibility.Collapsed;

            // Always show all songs in Songs view
            SongListView.ItemsSource = originalPlaylist;
        }

        private async void ViewAlbums_Click(object sender, RoutedEventArgs e)
        {
            SongListView.Visibility = Visibility.Collapsed;
            AlbumsView.Visibility = Visibility.Visible;
            ArtistsView.Visibility = Visibility.Collapsed;
            SongDetailView.Visibility = Visibility.Collapsed;

            var albums = await _cacheService.GetAlbumGroupsAsync(PlaylistBox.Items.Cast<SongInfo>());
            AlbumsView.ItemsSource = albums;
        }

        private async void ViewArtists_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SongListView.Visibility = Visibility.Collapsed;
                AlbumsView.Visibility = Visibility.Collapsed;
                ArtistsView.Visibility = Visibility.Visible;
                SongDetailView.Visibility = Visibility.Collapsed;

                var artists = await _cacheService.GetArtistGroupsAsync(PlaylistBox.Items.Cast<SongInfo>());
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
                SongListView.ItemsSource = originalPlaylist;

                // Update Albums View with proper song count
                var albumGroups = originalPlaylist
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
                var artistGroups = originalPlaylist
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

                // Keep PlaylistBox in sync
                PlaylistBox.Items.Clear();
                foreach (var song in originalPlaylist)
                {
                    PlaylistBox.Items.Add(song);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating views: {ex.Message}");
            }
        }

        private void AddSongToPlaylist(string filePath)
        {
            if (!originalPlaylist.Any(s => s.FilePath == filePath))
            {
                var songInfo = new SongInfo(filePath);
                originalPlaylist.Add(songInfo);
                PlaylistBox.Items.Add(songInfo);

                // Update all views
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

        }

        private void ViewArtistSongs(ArtistInfo artistInfo)
        {
            if (artistInfo == null) return;

            // Filter songs by artist
            var artistSongs = originalPlaylist
                .Where(s => s.Artist == artistInfo.Name)
                .ToList();

            // Update Songs view with filtered songs
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
            SongListView.ItemsSource = albumSongs;

            // Switch to Songs view
            SongListView.Visibility = Visibility.Visible;
            AlbumsView.Visibility = Visibility.Collapsed;
            ArtistsView.Visibility = Visibility.Collapsed;
            SongDetailView.Visibility = Visibility.Collapsed;
        }

        private void ViewPlaylist_Click(object sender, RoutedEventArgs e)
        {
            SongListView.Visibility = Visibility.Visible;
            AlbumsView.Visibility = Visibility.Collapsed;
            ArtistsView.Visibility = Visibility.Collapsed;
            SongDetailView.Visibility = Visibility.Collapsed;

            // Show upcoming songs in order
            var upcomingPlaylist = GetUpcomingPlaylist();
            SongListView.ItemsSource = upcomingPlaylist;
        }

        private List<SongInfo> GetUpcomingPlaylist()
        {
            var playlist = new List<SongInfo>();
            var currentIndex = PlaylistBox.SelectedIndex;

            if (currentIndex == -1) return playlist;

            // If shuffle is enabled, generate shuffled playlist
            if (isShuffleEnabled)
            {
                var remainingSongs = new List<SongInfo>();
                for (int i = 0; i < PlaylistBox.Items.Count; i++)
                {
                    if (i != currentIndex)
                    {
                        remainingSongs.Add(PlaylistBox.Items[i] as SongInfo);
                    }
                }

                // Shuffle the remaining songs
                var rng = new Random();
                var shuffledSongs = remainingSongs.OrderBy(x => rng.Next()).ToList();

                // Add current song at the beginning
                playlist.Add(PlaylistBox.Items[currentIndex] as SongInfo);
                playlist.AddRange(shuffledSongs);
            }
            else
            {
                // Add songs in order starting from current
                for (int i = currentIndex; i < PlaylistBox.Items.Count; i++)
                {
                    playlist.Add(PlaylistBox.Items[i] as SongInfo);
                }

                // If repeat is enabled, add songs from beginning
                if (isRepeatEnabled && currentIndex > 0)
                {
                    for (int i = 0; i < currentIndex; i++)
                    {
                        playlist.Add(PlaylistBox.Items[i] as SongInfo);
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
            if (SongListView.IsVisible && SongListView.ItemsSource != originalPlaylist)
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
            if (PlaylistBox.SelectedItem is SongInfo currentSong)
            {
                ShowSongDetails(currentSong);
            }
        }
    }
}