using System;
using System.Windows.Media.Imaging;
using System.ComponentModel;

namespace MusicPlayer.Models
{
    public class ArtistInfo : INotifyPropertyChanged
    {
        private string name;
        private BitmapImage artistImage;
        private DateTime lastUpdated;
        private int songCount;

        public string Name
        {
            get => name;
            set
            {
                if (name != value)
                {
                    name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public BitmapImage ArtistImage
        {
            get => artistImage;
            set
            {
                if (artistImage != value)
                {
                    artistImage = value;
                    OnPropertyChanged(nameof(ArtistImage));
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

        public void SetDefaultImage()
        {
            try
            {
                var defaultImage = new BitmapImage();
                defaultImage.BeginInit();
                defaultImage.UriSource = new Uri("pack://application:,,,/MusicPlayer;component/Resources/default_artist.png", UriKind.Absolute);
                defaultImage.EndInit();
                defaultImage.Freeze();
                ArtistImage = defaultImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting default image: {ex.Message}");
            }
        }
    }
}