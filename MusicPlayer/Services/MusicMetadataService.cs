using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Threading;
using System.Net.Http;
using System.Text.Json;
using System.IO;

namespace MusicPlayer.Services
{


    public class MusicMetadataService
    {
        private readonly HttpClient _httpClient;
        private readonly MetadataCacheService _cacheService;

        public MusicMetadataService(MetadataCacheService metadataCacheService)
        {
            _httpClient = new HttpClient();
            _cacheService = metadataCacheService;
        }

        public async Task<BitmapImage> GetArtistImageAsync(string artist)
        {
            if (string.IsNullOrEmpty(artist)) return null;

            try
            {
                // Check cache first
                var cachedArtist = _cacheService.GetArtistInfo(artist);
                if (cachedArtist?.ArtistImage != null)
                {
                    // Safely check if it's a default image
                    try
                    {
                        var uriSource = cachedArtist.ArtistImage.UriSource;
                        if (uriSource != null && !uriSource.ToString().Contains("default_artist"))
                        {
                            return cachedArtist.ArtistImage;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error checking cached image: {ex.Message}");
                        // Continue to fetch new image if there's an error with cached image
                    }
                }

                // If not in cache or is default image, fetch from Deezer API
                var artistImage = await FetchArtistImageFromApi(artist);
                if (artistImage != null)
                {
                    _cacheService.CacheArtistInfo(artist, artistImage);
                }
                return artistImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetArtistImageAsync: {ex.Message}");
                return null;
            }
        }

        private async Task<BitmapImage> FetchArtistImageFromApi(string artist)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Fetching image for artist: {artist}");
                // Search for the artist on Deezer
                string[] separators = new[] { "feat", "ft.", "&" };
                artist = artist.Split(separators, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                var deezerUrl = $"https://api.deezer.com/search/artist?q={Uri.EscapeDataString(artist)}&limit=1";

                var response = await _httpClient.GetStringAsync(deezerUrl);
                System.Diagnostics.Debug.WriteLine($"Deezer API Response: {response}");
                var jsonDoc = JsonDocument.Parse(response);
                var dataArray = jsonDoc.RootElement.GetProperty("data");

                if (dataArray.GetArrayLength() > 0)
                {
                    var artistData = dataArray[0];
                    var pictureUrl = artistData.GetProperty("picture_big").GetString();

                    if (!string.IsNullOrEmpty(pictureUrl))
                    {
                        var imageBytes = await _httpClient.GetByteArrayAsync(pictureUrl);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = new MemoryStream(imageBytes);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 500;
                        bitmap.DecodePixelHeight = 500;
                        bitmap.EndInit();
                        bitmap.Freeze(); // Make it thread-safe
                        return bitmap;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in FetchArtistImageFromApi: {ex.Message}");
                return null;
            }
        }
    }
}