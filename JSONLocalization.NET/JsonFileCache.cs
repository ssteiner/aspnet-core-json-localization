using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace JSONLocalization.NET
{
    public class JsonFileCache
    {

        private readonly IDistributedCache cache;
        private readonly ILogger logger;
        private readonly ConcurrentDictionary<string, string> fileHashes;
        private readonly string resourceLocation = "Resources";
        private readonly JsonSerializer _serializer = new JsonSerializer();
        private readonly FileSystemWatcher resourceFileWatcher;
        private readonly string resourceFilePattern = "*.json";

        public JsonFileCache(IDistributedCache cache, ILogger<JsonFileCache> logger)
        {
            this.cache = cache;
            this.logger = logger;
            fileHashes = new ConcurrentDictionary<string, string>();
            if (!Environment.UserInteractive) // app running as a service
            {
                var path = Path.GetDirectoryName(GetType().Assembly.Location);
                resourceLocation = Path.Combine(path, resourceLocation);
            }
            resourceFileWatcher = new FileSystemWatcher(resourceLocation, resourceFilePattern);
            resourceFileWatcher.Changed += ResourceFileWatcher_Changed;
            resourceFileWatcher.Created += ResourceFileWatcher_Created;
            resourceFileWatcher.Renamed += ResourceFileWatcher_Renamed;
            resourceFileWatcher.Deleted += ResourceFileWatcher_Deleted;
            resourceFileWatcher.Error += ResourceFileWatcher_Error;
        }

        public void Start()
        {
            try
            {
                var resourceFiles = Directory.GetFiles(resourceLocation, resourceFilePattern);
                foreach (var resourceFile in resourceFiles)
                    FileChanged(resourceFile);
            }
            catch (Exception e)
            {
                logger.LogError($"Unhandled error filling initial cache: {e.Message}");
            }
            resourceFileWatcher.EnableRaisingEvents = true;
        }

        private void ResourceFileWatcher_Deleted(object sender, FileSystemEventArgs e)
        {
            FileRemoved(e.FullPath);
        }

        private void ResourceFileWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            logger.LogInformation($"Resource file has been renamed from {e.OldFullPath} to {e.FullPath}");
            FileChanged(e.FullPath);
            FileRemoved(e.OldFullPath);
        }

        private void ResourceFileWatcher_Created(object sender, FileSystemEventArgs e)
        {
            logger.LogInformation($"New resource file detected: {e.FullPath}");
            FileChanged(e.FullPath);
        }

        private void ResourceFileWatcher_Error(object sender, ErrorEventArgs e)
        {
            logger.LogError($"Error in resource file system watcher file watcher: {e.GetException().Message}", 2);
        }

        private void ResourceFileWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            logger.LogInformation($"Resource file has changed: {e.FullPath}");
            FileChanged(e.FullPath);
        }

        private void FileChanged(string fileNameAndPath)
        {
            string hash;
            try
            {
                hash = ComputeFileChecksum(fileNameAndPath);
            }
            catch (Exception x)
            {
                logger.LogError($"Unable to compute file hash of new file {fileNameAndPath}: {x.Message}");
                return;
            }
            string previousHash = null;
            fileHashes.AddOrUpdate(fileNameAndPath, hash, (key, existingValue) =>
            {
                previousHash = existingValue;
                return hash;
            });
            if (hash != previousHash)
            {
                var newStrings = ReadFile(fileNameAndPath);
                if (newStrings.Count > 0)
                {
                    try
                    {
                        List<string> existingKeys = null;
                        var fileName = Path.GetFileNameWithoutExtension(fileNameAndPath);
                        var locale = Path.GetExtension(fileName).TrimStart('.');
                        var resourceName = Path.GetFileNameWithoutExtension(fileName);
                        var existingKeysKey = $"keys_{resourceName}.{locale}";
                        var existingKeysString = cache.GetString(existingKeysKey);
                        if (existingKeysString != null)
                            existingKeys = JsonConvert.DeserializeObject<List<string>>(existingKeysString);
                        foreach (var newString in newStrings)
                        {
                            var key = $"{resourceName}.{locale}.{newString.Name}";
                            cache.SetString(key, newString.Value);
                        }
                        var newKeys = newStrings.Select(x => x.Name).ToList();
                        if (existingKeys != null)
                        {
                            var toBeRemoved = newKeys.Where(x => !existingKeys.Contains(x)).ToList();
                            foreach (var key in toBeRemoved)
                                cache.Remove(key);
                        }
                        var newKeysString = JsonConvert.SerializeObject(newKeys);
                        cache.SetString(existingKeysKey, newKeysString);
                    }
                    catch (Exception e)
                    {
                        logger.LogError($"Unhandled error processing added/changed file {fileNameAndPath}: {e.Message}");
                    }
                }
            }
        }

        private void FileRemoved(string fileNameAndPath)
        {
            fileHashes.TryRemove(fileNameAndPath, out _);
            try
            {
                List<string> existingKeys = null;
                var fileName = Path.GetFileNameWithoutExtension(fileNameAndPath);
                var locale = Path.GetExtension(fileName);
                var resourceName = Path.GetFileNameWithoutExtension(fileName);
                var existingKeysKey = cache.GetString($"keys_{locale}_{resourceName}");
                var existingKeysString = cache.GetString(existingKeysKey);
                if (existingKeysString != null)
                    existingKeys = JsonConvert.DeserializeObject<List<string>>(existingKeysString);
                if (existingKeys != null)
                {
                    foreach (var key in existingKeys)
                        cache.Remove(key);
                }
            }
            catch (Exception e)
            {
                logger.LogError($"Unhandled error processing removed/renamed file {fileNameAndPath}: {e.Message}");
            }
        }

        private List<LocalizedString> ReadFile(string fileName)
        {
            var result = new List<LocalizedString>();
            try
            {
                using var str = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var sReader = new StreamReader(str);
                using var reader = new JsonTextReader(sReader);
                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.PropertyName)
                        continue;
                    string key = (string)reader.Value;
                    reader.Read();
                    string value = _serializer.Deserialize<string>(reader);
                    result.Add(new LocalizedString(key, value, false));
                }
            }
            catch (Exception e)
            {
                logger.LogError($"Unable to read {fileName}: {e.Message}");
            }
            return result;
        }

        public static string ComputeFileChecksum(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                using (var sha1 = new SHA1Managed())
                {
                    byte[] hash = sha1.ComputeHash(fs);
                    var formatted = new StringBuilder(2 * hash.Length);
                    foreach (byte b in hash)
                    {
                        formatted.AppendFormat("{0:X2}", b);
                    }
                    return formatted.ToString();
                }
            }
        }


    }
}
