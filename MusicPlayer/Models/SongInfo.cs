using System;
using System.IO;
using TagLib;
using System.Windows.Media;
using System.Windows;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Net.Http;
using System.ComponentModel;

namespace MusicPlayer.Models
{
    public class SongInfo : INotifyPropertyChanged
    {
        private bool isPlaying;
        public bool IsPlaying
        {
            get => isPlaying;
            set
            {
                if (isPlaying != value)
                {
                    isPlaying = value;
                    OnPropertyChanged(nameof(IsPlaying));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string FilePath { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public TimeSpan Duration { get; set; }
        public ImageSource AlbumArt { get; set; }

        public SongInfo(string filePath)
        {
            FilePath = filePath;
            LoadMetadata();
            _ = LoadAlbumArtAsync();
        }

        private void LoadMetadata()
        {
            try
            {
                if (!System.IO.File.Exists(FilePath))
                {
                    throw new FileNotFoundException("Audio file not found", FilePath);
                }

                using (var file = TagLib.File.Create(FilePath))
                {
                    // Handle FLAC-specific metadata if available
                    if (file is TagLib.Flac.File flacFile)
                    {
                        // Try to get more detailed FLAC metadata
                        var flacTag = flacFile.Tag;
                        Title = string.IsNullOrEmpty(flacTag.Title) ?
                            Path.GetFileNameWithoutExtension(FilePath) : flacTag.Title;
                        Artist = string.IsNullOrEmpty(flacTag.FirstPerformer) ?
                            "Unknown Artist" : flacTag.FirstPerformer;
                        Album = string.IsNullOrEmpty(flacTag.Album) ?
                            "Unknown Album" : flacTag.Album;
                        Duration = flacFile.Properties.Duration;
                    }
                    else
                    {
                        // Handle other formats
                        Title = string.IsNullOrEmpty(file.Tag.Title) ?
                            Path.GetFileNameWithoutExtension(FilePath) : file.Tag.Title;
                        Artist = string.IsNullOrEmpty(file.Tag.FirstPerformer) ?
                            "Unknown Artist" : file.Tag.FirstPerformer;
                        Album = string.IsNullOrEmpty(file.Tag.Album) ?
                            "Unknown Album" : file.Tag.Album;
                        Duration = file.Properties.Duration;
                    }
                }
            }
            catch (Exception ex)
            {
                Title = Path.GetFileNameWithoutExtension(FilePath);
                Artist = "Unknown Artist";
                Album = "Unknown Album";
                Duration = TimeSpan.Zero;
                System.Diagnostics.Debug.WriteLine($"Error loading metadata: {ex.Message}");
            }
        }

        public async Task LoadAlbumArtAsync()
        {
            try
            {
                using (var file = TagLib.File.Create(FilePath))
                {
                    if (file.Tag.Pictures.Length > 0)
                    {
                        var picture = file.Tag.Pictures[0];
                        using (var stream = new MemoryStream(picture.Data.Data))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.DecodePixelWidth = 1000;  // Set fixed width
                            bitmap.DecodePixelHeight = 1000; // Set fixed height
                            bitmap.StreamSource = stream;
                            bitmap.EndInit();
                            bitmap.Freeze(); // Make it thread-safe
                            AlbumArt = bitmap;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading album art: {ex.Message}");
            }
        }

        public override string ToString()
        {
            return $"{Title} - {Artist}";
        }
    }
}