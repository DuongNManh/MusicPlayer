using System;
using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using MusicPlayer.Models;
using System.Text.Json;

namespace MusicPlayer.Services
{
    public class MetadataCacheService
    {
        private readonly ConcurrentDictionary<string, ArtistInfo> artistCache;
        private readonly ConcurrentDictionary<string, AlbumInfo> albumCache;
        private readonly TimeSpan cacheExpiration = TimeSpan.FromDays(7); // Cache for 7 days
        private const int MaxCacheSize = 1000; // Limit cache size

        public MetadataCacheService()
        {
            artistCache = new ConcurrentDictionary<string, ArtistInfo>();
            albumCache = new ConcurrentDictionary<string, AlbumInfo>();
        }

        public ArtistInfo GetArtistInfo(string artistName)
        {
            if (string.IsNullOrEmpty(artistName)) return null;

            if (artistCache.TryGetValue(artistName, out ArtistInfo artistInfo))
            {
                if (DateTime.Now - artistInfo.LastUpdated < cacheExpiration)
                {
                    return artistInfo;
                }
                // Remove expired cache entry
                artistCache.TryRemove(artistName, out _);
            }
            return null;
        }

        public void CacheArtistInfo(string artistName, BitmapImage artistImage)
        {
            if (string.IsNullOrEmpty(artistName) || artistImage == null) return;

            var artistInfo = new ArtistInfo
            {
                Name = artistName,
                ArtistImage = artistImage,
                LastUpdated = DateTime.Now
            };

            artistCache.AddOrUpdate(artistName, artistInfo, (key, oldValue) => artistInfo);
        }

        public AlbumInfo GetAlbumInfo(string albumTitle, string artist)
        {
            string key = $"{artist}_{albumTitle}";
            if (albumCache.TryGetValue(key, out AlbumInfo albumInfo))
            {
                if (DateTime.Now - albumInfo.LastUpdated < cacheExpiration)
                {
                    return albumInfo;
                }
                albumCache.TryRemove(key, out _);
            }
            return null;
        }

        public void CacheAlbumInfo(string albumTitle, string artist, BitmapImage albumArt)
        {
            if (string.IsNullOrEmpty(albumTitle) || string.IsNullOrEmpty(artist) || albumArt == null) return;

            var albumInfo = new AlbumInfo
            {
                Title = albumTitle,
                Artist = artist,
                AlbumArt = albumArt,
                LastUpdated = DateTime.Now
            };

            string key = $"{artist}_{albumTitle}";
            albumCache.AddOrUpdate(key, albumInfo, (k, oldValue) => albumInfo);
        }

        public string SaveToJson()
        {
            var cacheData = new
            {
                Artists = artistCache.Where(x => DateTime.Now - x.Value.LastUpdated < cacheExpiration)
                                   .Take(MaxCacheSize)
                                   .ToDictionary(x => x.Key, x => x.Value),
                Albums = albumCache.Where(x => DateTime.Now - x.Value.LastUpdated < cacheExpiration)
                                 .Take(MaxCacheSize)
                                 .ToDictionary(x => x.Key, x => x.Value)
            };

            return JsonSerializer.Serialize(cacheData, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        public void LoadFromJson(string json)
        {
            try
            {
                var cacheData = JsonSerializer.Deserialize<dynamic>(json);

                // Clear existing caches
                artistCache.Clear();
                albumCache.Clear();

                // Load artists
                foreach (var artist in cacheData.Artists)
                {
                    artistCache.TryAdd(artist.Key, artist.Value);
                }

                // Load albums
                foreach (var album in cacheData.Albums)
                {
                    albumCache.TryAdd(album.Key, album.Value);
                }

                // Clean up expired entries
                CleanupExpiredEntries();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading cache from JSON: {ex.Message}");
            }
        }

        private void CleanupExpiredEntries()
        {
            var now = DateTime.Now;

            // Remove expired artist entries
            foreach (var artist in artistCache.Where(x => now - x.Value.LastUpdated >= cacheExpiration))
            {
                artistCache.TryRemove(artist.Key, out _);
            }

            // Remove expired album entries
            foreach (var album in albumCache.Where(x => now - x.Value.LastUpdated >= cacheExpiration))
            {
                albumCache.TryRemove(album.Key, out _);
            }

            // Limit cache size
            if (artistCache.Count > MaxCacheSize)
            {
                var oldestArtists = artistCache.OrderBy(x => x.Value.LastUpdated)
                                             .Take(artistCache.Count - MaxCacheSize);
                foreach (var artist in oldestArtists)
                {
                    artistCache.TryRemove(artist.Key, out _);
                }
            }

            if (albumCache.Count > MaxCacheSize)
            {
                var oldestAlbums = albumCache.OrderBy(x => x.Value.LastUpdated)
                                           .Take(albumCache.Count - MaxCacheSize);
                foreach (var album in oldestAlbums)
                {
                    albumCache.TryRemove(album.Key, out _);
                }
            }
        }

        // Add memory monitoring
        public long GetCacheSize()
        {
            return artistCache.Count + albumCache.Count;
        }

        public void ClearExpiredCache()
        {
            CleanupExpiredEntries();
        }

        public async Task<List<AlbumInfo>> GetAlbumGroupsAsync(IEnumerable<SongInfo> songs)
        {
            var albumGroups = songs
                .GroupBy(s => new { s.Album, s.Artist })
                .Select(g =>
                {
                    var albumInfo = GetAlbumInfo(g.Key.Album, g.Key.Artist) ?? new AlbumInfo
                    {
                        Title = g.Key.Album,
                        Artist = g.Key.Artist,
                        LastUpdated = DateTime.Now
                    };

                    // Update song count
                    albumInfo.SongCount = g.Count();

                    // Update album art if needed
                    if (albumInfo.AlbumArt == null)
                    {
                        var songWithArt = g.FirstOrDefault(s => s.AlbumArt != null);
                        if (songWithArt != null)
                        {
                            albumInfo.AlbumArt = songWithArt.AlbumArt as BitmapImage;
                            CacheAlbumInfo(g.Key.Album, g.Key.Artist, albumInfo.AlbumArt);
                        }
                    }

                    return albumInfo;
                })
                .ToList();

            return albumGroups;
        }

        public Task<List<ArtistInfo>> GetArtistGroupsAsync(IEnumerable<SongInfo> songs)
        {
            var artistGroups = songs
                .GroupBy(s => s.Artist)
                .Select(g =>
                {
                    var artistInfo = GetArtistInfo(g.Key) ?? new ArtistInfo
                    {
                        Name = g.Key,
                        LastUpdated = DateTime.Now
                    };

                    // Update song count
                    artistInfo.SongCount = g.Count();

                    // If no cached artist image, set default image temporarily
                    if (artistInfo.ArtistImage == null)
                    {
                        artistInfo.SetDefaultImage();
                    }

                    return artistInfo;
                })
                .ToList();

            return Task.FromResult(artistGroups);
        }
    }
}