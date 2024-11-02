using MusicPlayer.Models;
using MusicPlayer.Services;
using System.Windows;

namespace MusicPlayer.Views
{
    public partial class SongDetailWindow : Window
    {
        private readonly MusicMetadataService _musicMetadataService = new();

        public SongDetailWindow(SongInfo song)
        {
            InitializeComponent();
            LoadSongDetails(song);
        }

        private async void LoadSongDetails(SongInfo song)
        {
            if (song == null) return;

            TitleText.Text = song.Title;
            ArtistText.Text = song.Artist;
            AlbumText.Text = song.Album;

            // Load album art
            if (song.AlbumArt == null)
            {
                await song.LoadAlbumArtAsync();
            }
            AlbumArtImage.Source = song.AlbumArt;

            // Load artist image
            if (!string.IsNullOrEmpty(song.Artist) && song.Artist != "Unknown Artist")
            {
                var artistImage = await _musicMetadataService.GetArtistImageAsync(song.Artist);
                ArtistImage.Source = artistImage;
            }
        }
    }
}