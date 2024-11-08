using System;
using System.Windows.Media.Imaging;
using System.ComponentModel;

namespace MusicPlayer.Models
{
    public class AlbumInfo : INotifyPropertyChanged
    {
        private string title;
        private string artist;
        private BitmapImage albumArt;
        private DateTime lastUpdated;
        private int songCount;

        public string Title
        {
            get => title;
            set
            {
                if (title != value)
                {
                    title = value;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        public string Artist
        {
            get => artist;
            set
            {
                if (artist != value)
                {
                    artist = value;
                    OnPropertyChanged(nameof(Artist));
                }
            }
        }

        public BitmapImage AlbumArt
        {
            get => albumArt;
            set
            {
                if (albumArt != value)
                {
                    albumArt = value;
                    OnPropertyChanged(nameof(AlbumArt));
                }
            }
        }

        public DateTime LastUpdated
        {
            get => lastUpdated;
            set
            {
                lastUpdated = value;
                OnPropertyChanged(nameof(LastUpdated));
            }
        }

        public int SongCount
        {
            get => songCount;
            set
            {
                if (songCount != value)
                {
                    songCount = value;
                    OnPropertyChanged(nameof(SongCount));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void SetDefaultAlbumArt()
        {
            try
            {
                var defaultImage = new BitmapImage();
                defaultImage.BeginInit();
                defaultImage.UriSource = new Uri("pack://application:,,,/MusicPlayer;component/Resources/default_album.png", UriKind.Absolute);
                defaultImage.EndInit();
                defaultImage.Freeze();
                AlbumArt = defaultImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting default album art: {ex.Message}");
                AlbumArt = null;
            }
        }
    }
}