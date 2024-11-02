using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Threading;
using System.Net.Http;
using System.Text.Json;

namespace MusicPlayer.Services
{


    public class MusicMetadataService
    {
        private readonly HttpClient _httpClient;

        public MusicMetadataService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MusicPlayer/1.0");
        }

        public async Task<ImageSource?> GetArtistImageAsync(string artistName)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Fetching image for artist: {artistName}");
                var deezerUrl = $"https://api.deezer.com/search/artist?q={Uri.EscapeDataString(artistName)}&limit=1";

                var response = await _httpClient.GetStringAsync(deezerUrl);
                System.Diagnostics.Debug.WriteLine($"Deezer API Response: {response}");

                var jsonDoc = JsonDocument.Parse(response);
                var dataArray = jsonDoc.RootElement.GetProperty("data");

                if (dataArray.GetArrayLength() > 0)
                {
                    var artist = dataArray[0];
                    var pictureUrl = artist.GetProperty("picture_big").GetString();
                    System.Diagnostics.Debug.WriteLine($"Found picture URL: {pictureUrl}");

                    if (!string.IsNullOrEmpty(pictureUrl))
                    {
                        try
                        {
                            // First verify if the URL is accessible
                            var request = await _httpClient.GetAsync(pictureUrl);
                            if (request.IsSuccessStatusCode)
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.UriSource = new Uri(pictureUrl);
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.DecodePixelWidth = 500;  // Set fixed width
                                bitmap.DecodePixelHeight = 500; // Set fixed height
                                bitmap.EndInit();

                                // Create a TaskCompletionSource to wait for the download
                                var tcs = new TaskCompletionSource<bool>();
                                bitmap.DownloadCompleted += (s, e) =>
                                {
                                    try
                                    {
                                        bitmap.Freeze();
                                        tcs.SetResult(true);
                                    }
                                    catch (Exception ex)
                                    {
                                        tcs.SetException(ex);
                                    }
                                };

                                // Wait for the download to complete
                                await tcs.Task;
                                return bitmap;
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to access URL: {pictureUrl}, Status: {request.StatusCode}");
                                return null;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading image: {ex.Message}");
                            return null;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"No images found for artist: {artistName}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching artist image: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }
    }
}