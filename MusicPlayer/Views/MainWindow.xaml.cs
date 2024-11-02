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
using MusicPlayer.Views;
using System.Windows.Input;

namespace MusicPlayer
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
        private readonly int visualizationBars = 50;
        private System.Windows.Shapes.Rectangle[] visualizationRectangles;
        private readonly MusicMetadataService _musicMetadataService;

        public MainWindow()
        {
            InitializeComponent();
            _musicMetadataService = new MusicMetadataService();
            InitializePlayer();

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

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Media files (*.mp3;*.wav;*.flac)|*.mp3;*.wav;*.flac|All files (*.*)|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string file in openFileDialog.FileNames)
                {
                    AddSongToPlaylist(file);
                }

                if (PlaylistBox.Items.Count == 1)
                {
                    PlaylistBox.SelectedIndex = 0;
                    PlayMedia();
                }
            }
        }

        private void AddSongToPlaylist(string filePath)
        {
            var songInfo = new SongInfo(filePath);
            originalPlaylist.Add(songInfo);
            PlaylistBox.Items.Add(songInfo);

            // Update the ListView
            SongListView.ItemsSource = originalPlaylist;
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var files = Directory.GetFiles(dialog.SelectedPath, "*.*")
                    .Where(file => file.ToLower().EndsWith("mp3") ||
                                 file.ToLower().EndsWith("wav") ||
                                 file.ToLower().EndsWith("flac"));

                foreach (string file in files)
                {
                    AddSongToPlaylist(file);
                }

                if (PlaylistBox.Items.Count > 0 && PlaylistBox.SelectedIndex == -1)
                {
                    PlaylistBox.SelectedIndex = 0;
                    PlayMedia();
                }
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

        private void LoadPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Playlist files (*.json)|*.json"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string jsonString = File.ReadAllText(openFileDialog.FileName);
                var playlist = JsonSerializer.Deserialize<List<string>>(jsonString);

                PlaylistBox.Items.Clear();
                originalPlaylist.Clear();

                foreach (string file in playlist)
                {
                    if (File.Exists(file))
                    {
                        AddSongToPlaylist(file);
                    }
                }

                if (PlaylistBox.Items.Count > 0)
                {
                    PlaylistBox.SelectedIndex = 0;
                    PlayMedia();
                }
            }
        }

        private void ShuffleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            isShuffleEnabled = ShuffleMenuItem.IsChecked;
            if (isShuffleEnabled)
            {
                ShufflePlaylist();
            }
            else
            {
                RestoreOriginalPlaylist();
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

        private void RepeatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            isRepeatEnabled = RepeatMenuItem.IsChecked;
        }

        private void PlayMedia()
        {
            if (PlaylistBox.SelectedItem is SongInfo selectedSong)
            {
                try
                {
                    mediaPlayer.Open(new Uri(selectedSong.FilePath));
                    mediaPlayer.Play();
                    PlayButton.Content = "⏸";
                    timer.Start();
                    UpdateNowPlaying(selectedSong);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error playing media: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
                }

                // Refresh both views
                PlaylistBox.Items.Refresh();

                // Force ListView to refresh
                var currentList = originalPlaylist.ToList(); // Create a new copy
                SongListView.ItemsSource = currentList;
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
                CurrentTimeText.Text = mediaPlayer.Position.ToString(@"mm\:ss");
            }
        }

        private void MediaPlayer_MediaOpened(object sender, EventArgs e)
        {
            if (mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                ProgressSlider.Maximum = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                TotalTimeText.Text = mediaPlayer.NaturalDuration.TimeSpan.ToString(@"mm\:ss");
            }
        }

        private void MediaPlayer_MediaEnded(object sender, EventArgs e)
        {
            if (isRepeatEnabled)
            {
                mediaPlayer.Position = TimeSpan.Zero;
                mediaPlayer.Play();
                return;
            }

            if (PlaylistBox.SelectedIndex < PlaylistBox.Items.Count - 1)
            {
                PlaylistBox.SelectedIndex++;
            }
            else if (isShuffleEnabled)
            {
                ShufflePlaylist();
                PlaylistBox.SelectedIndex = 0;
            }
            else
            {
                mediaPlayer.Stop();
                PlayButton.Content = "▶";
                timer.Stop();
            }
        }

        private void PlaylistBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaylistBox.SelectedItem != null)
            {
                PlayMedia();
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
            mediaPlayer.Position = TimeSpan.FromSeconds(ProgressSlider.Value);
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaylistBox.SelectedIndex > 0)
            {
                PlaylistBox.SelectedIndex--;
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaylistBox.SelectedIndex < PlaylistBox.Items.Count - 1)
            {
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
                mediaPlayer.Stop();
                timer.Stop();
                mediaPlayer.Close();
            }
            catch { }
        }

        private void SongListItem_Click(object sender, MouseButtonEventArgs e)
        {
            var frameworkElement = sender as FrameworkElement;
            if (frameworkElement?.DataContext is SongInfo song)
            {
                ShowSongDetails(song);
                PlaylistBox.SelectedItem = song;
                PlayMedia();
            }
        }

        private void ShowSongDetails(SongInfo song)
        {
            if (song == null) return;

            // Update detail view content
            DetailTitleText.Text = song.Title;
            DetailArtistText.Text = song.Artist;
            DetailAlbumText.Text = song.Album;
            DetailAlbumArtImage.Source = song.AlbumArt;

            // Load artist image
            if (!string.IsNullOrEmpty(song.Artist) && song.Artist != "Unknown Artist")
            {
                LoadArtistImage(song.Artist);
            }

            // Switch views
            SongListView.Visibility = Visibility.Collapsed;
            SongDetailView.Visibility = Visibility.Visible;
        }

        private async void LoadArtistImage(string artist)
        {
            try
            {
                var artistImage = await _musicMetadataService.GetArtistImageAsync(artist);
                DetailArtistImage.Source = artistImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading artist image: {ex.Message}");
            }
        }

        private void BackToLibrary_Click(object sender, RoutedEventArgs e)
        {
            // Switch back to library view
            SongDetailView.Visibility = Visibility.Collapsed;
            SongListView.Visibility = Visibility.Visible;
        }
    }
}