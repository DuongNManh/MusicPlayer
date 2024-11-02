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

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}