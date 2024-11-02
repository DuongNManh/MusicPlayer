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

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}