using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace JsonLocalizationLib
{
    public class JsonFileCache
    {

        private readonly IDistributedCache cache;
        private readonly ILogger logger;
        private readonly ConcurrentDictionary<string, string> fileHashes;
        private readonly string resourceLocation = "Resources";
        private readonly JsonSerializer _serializer = new();
        private readonly FileSystemWatcher resourceFileWatcher;
        private readonly string resourceFilePattern = "*.json";
        private readonly int fileProcessingDelay;
        private readonly ConcurrentDictionary<string, DateTime> lastFileUpdates;
        private readonly ConcurrentDictionary<string, object> fileAccessLocks;

        public JsonFileCache(IDistributedCache cache, ILogger<JsonFileCache> logger, IOptions<JsonTranslationOptions> options)
        {
            this.cache = cache;
            this.logger = logger;
            fileHashes = new ConcurrentDictionary<string, string>();
            lastFileUpdates = new ConcurrentDictionary<string, DateTime>();
            fileAccessLocks = new ConcurrentDictionary<string, object>();
            if (!string.IsNullOrEmpty(options.Value?.ResourceFolder))
                resourceLocation = options.Value.ResourceFolder;
            if (!string.IsNullOrEmpty(options.Value?.ResourcePath))
                resourceLocation = options.Value.ResourcePath;
            else if (!Environment.UserInteractive) // app running as a service
            {
                var path = Path.GetDirectoryName(GetType().Assembly.Location);
                resourceLocation = Path.Combine(path, resourceLocation);
            }
            if (Directory.Exists(resourceLocation))
            {
                resourceFileWatcher = new FileSystemWatcher(resourceLocation, resourceFilePattern);
                resourceFileWatcher.Changed += ResourceFileWatcher_Changed;
                resourceFileWatcher.Created += ResourceFileWatcher_Created;
                resourceFileWatcher.Renamed += ResourceFileWatcher_Renamed;
                resourceFileWatcher.Deleted += ResourceFileWatcher_Deleted;
                resourceFileWatcher.Error += ResourceFileWatcher_Error;
            }
            else
                logger.LogCritical($"Resource location {resourceLocation} does not exist");
            fileProcessingDelay = 1000;
        }

        public void Start()
        {
            if (resourceFileWatcher == null) return;
            logger.LogInformation($"Starting to watch directory {resourceFileWatcher.Path}");
            try
            {
                var resourceFiles = Directory.GetFiles(resourceLocation, resourceFilePattern);
                foreach (var resourceFile in resourceFiles)
                    FileChanged(resourceFile, true);
            }
            catch (Exception e)
            {
                logger.LogError($"Unhandled error filling initial cache: {e.Message}");
            }
            resourceFileWatcher.EnableRaisingEvents = true;
        }

        public void Stop()
        {
            if (resourceFileWatcher == null) return;
            try
            {
                resourceFileWatcher.EnableRaisingEvents = false;
                resourceFileWatcher.Changed -= ResourceFileWatcher_Changed;
                resourceFileWatcher.Created -= ResourceFileWatcher_Created;
                resourceFileWatcher.Renamed -= ResourceFileWatcher_Renamed;
                resourceFileWatcher.Deleted -= ResourceFileWatcher_Deleted;
                resourceFileWatcher.Error -= ResourceFileWatcher_Error;
                resourceFileWatcher.Dispose();
            }
            catch (Exception) { }
        }

        private void ResourceFileWatcher_Deleted(object sender, FileSystemEventArgs e)
        {
            logger.LogInformation($"Resource file {e.FullPath} has been removed");
            FileRemoved(e.FullPath);
        }

        private void ResourceFileWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            logger.LogInformation($"Resource file has been renamed from {e.OldFullPath} to {e.FullPath}");
            Task.Run(() => FileRenamed(e.OldFullPath, e.FullPath));
        }

        private void FileRenamed(string oldName, string newName)
        {
            FileChanged(newName);
            FileRemoved(oldName);
        }

        private void ResourceFileWatcher_Created(object sender, FileSystemEventArgs e)
        {
            logger.LogInformation($"New resource file detected: {e.FullPath}");
            Task.Run(() => FileChanged(e.FullPath));
        }

        private void ResourceFileWatcher_Error(object sender, ErrorEventArgs e)
        {
            logger.LogError($"Error in resource file system watcher file watcher: {e.GetException().Message}", 2);
        }

        private void ResourceFileWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            logger.LogInformation($"Resource file has changed: {e.FullPath}");
            Task.Run(() => FileChanged(e.FullPath));
        }

        private void FileChanged(string fileNameAndPath, bool isInitial = false)
        {
            string hash = null;
            var time = DateTime.Now;
            var fileLock = fileAccessLocks.GetOrAdd(fileNameAndPath, () => new object());
            lock (fileLock)
            {
                var previousUpdate = lastFileUpdates.GetValueOrDefault(fileNameAndPath);
                int nbDelay = 2;
                if (previousUpdate.AddSeconds(nbDelay) > time)
                {
                    logger.LogInformation($"{fileNameAndPath} was last updated less than {nbDelay} seconds ago, skipping update");
                    return;
                }
                var lastUpdate = lastFileUpdates.AddOrUpdate(fileNameAndPath, time, (key, existingValue) => time);
            }
            if (!isInitial)
                Delay(fileNameAndPath);
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
                lastFileUpdates.AddOrUpdate(fileNameAndPath, time, (key, existingValue) => time);
                if (previousHash != null)
                    logger.LogInformation($"Resource file {fileNameAndPath} has a different hash, reloading file");
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
                        var addedKeys = new List<string>();
                        var updatedKeys = new List<string>();
                        foreach (var newString in newStrings)
                        {
                            var key = $"{resourceName}.{locale}.{newString.Name}";
                            var existingValue = cache.GetString(key);
                            if (existingValue == null)
                                addedKeys.Add(key);
                            else if (string.Compare(existingValue, newString.Value, StringComparison.OrdinalIgnoreCase) != 0)
                                updatedKeys.Add(key);
                            cache.SetString(key, newString.Value);
                        }
                        if (addedKeys.Count > 0 && previousHash != null)
                            logger.LogInformation($"Adding {addedKeys.Count} new strings for {resourceName}, locale {locale}: {string.Join(",", addedKeys)}");
                        if (updatedKeys.Count > 0 && previousHash != null)
                            logger.LogInformation($"Updated {updatedKeys.Count} strings for {resourceName}, locale {locale}: {string.Join(",", updatedKeys)}");
                        var newKeys = newStrings.Select(x => x.Name).ToList();
                        if (existingKeys != null)
                        {
                            var toBeRemoved = newKeys.Where(x => !existingKeys.Contains(x)).ToList();
                            foreach (var key in toBeRemoved)
                                cache.Remove(key);
                            if (toBeRemoved.Count > 0)
                                logger.LogInformation($"Removed file {toBeRemoved.Count} strings from {fileName}, locale {locale}: {string.Join(",", toBeRemoved)}");
                        }
                        var newKeysString = JsonConvert.SerializeObject(newKeys);
                        cache.SetString(existingKeysKey, newKeysString);
                    }
                    catch (Exception e)
                    {
                        logger.LogError($"Unhandled error processing added/changed file {fileNameAndPath}: {e.Message}");
                    }
                }
                else
                    logger.LogWarning($"Resource file {fileNameAndPath} contains 0 strings");
            }
        }

        private void Delay(string fileName)
        {
            if (fileProcessingDelay > 0)
            {
                try
                {
                    Task.Delay(fileProcessingDelay).Wait();
                }
                catch (Exception e)
                {
                    logger.LogWarning($"exception delaying processing of {fileName}: {e.Message}", 2);
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
                if (existingKeys.Count > 0)
                    logger.LogInformation($"Removed file {existingKeys.Count} strings from {fileName}, locale {locale} because file was deleted");
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

        public static string ComputeFileChecksum(string fileName)
        {
            using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha1 = SHA1.Create();
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
