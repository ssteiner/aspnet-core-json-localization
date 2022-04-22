using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace JSONLocalization.NET
{
    public class JsonStringLocalizer : IStringLocalizer
    {

        private readonly IDistributedCache _cache;

        private readonly JsonSerializer _serializer = new JsonSerializer();

        private readonly string _resourceSource, _location = "Resources", _baseName;

        public JsonStringLocalizer(IDistributedCache cache, string resourceSource)
        {
            _cache = cache;
            _resourceSource = resourceSource;
        }

        public JsonStringLocalizer(IDistributedCache cache, string baseName, string location)
        {
            _cache = cache;
            _location = location;
            _baseName = baseName;
        }

        public LocalizedString this[string name]
        {
            get
            {
                string value = GetString(name);
                return new LocalizedString(name, value ?? name, value == null);
            }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                var actualValue = this[name];
                return !actualValue.ResourceNotFound
                    ? new LocalizedString(name, string.Format(actualValue.Value, arguments), false)
                    : actualValue;
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            List<LocalizedString> result = new List<LocalizedString>();
            var cultureName = GetCultureName();
            var keyListKey = $"keys_{ResourcePrefix}.{cultureName}";
            List<string> existingKeys = null;
            var existingKeysString = _cache.GetString(keyListKey);
            if (existingKeysString != null)
                existingKeys = JsonConvert.DeserializeObject<List<string>>(existingKeysString);
            if (existingKeys != null)
            {
                foreach (var key in existingKeys)
                {
                    result.Add(new LocalizedString(key, _cache.GetString(GetKeyName(key, cultureName)), false));
                }
            }
            if (includeParentCultures && !Thread.CurrentThread.CurrentCulture.IsNeutralCulture)
            {
                cultureName = GetCultureName(true);
                keyListKey = $"keys_{ResourcePrefix}.{cultureName}";
                existingKeysString = _cache.GetString(keyListKey);
                if (existingKeysString != null)
                    existingKeys = JsonConvert.DeserializeObject<List<string>>(existingKeysString);
                if (existingKeys != null)
                {
                    foreach (var key in existingKeys)
                    {
                        result.Add(new LocalizedString(key, _cache.GetString(GetKeyName(key, cultureName)), false));
                    }
                }
            }
            return result;

            //string filePath = GetResourceFilePath();
            //if (File.Exists(filePath))
            //    return GetAllStrings(filePath);
            //else
            //{
            //    var parentFilePath = GetResourceFilePath(true);
            //    if (parentFilePath != filePath)
            //        return GetAllStrings(filePath);
            //    return new List<LocalizedString>();
            //}
        }

        //private IEnumerable<LocalizedString> GetAllStrings(string filePath)
        //{
        //    using (var str = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        //    using (var sReader = new StreamReader(str))
        //    using (var reader = new JsonTextReader(sReader))
        //    {
        //        while (reader.Read())
        //        {
        //            if (reader.TokenType != JsonToken.PropertyName)
        //                continue;
        //            string key = (string)reader.Value;
        //            reader.Read();
        //            string value = _serializer.Deserialize<string>(reader);
        //            yield return new LocalizedString(key, value, false);
        //        }
        //    }
        //}

        private string GetString(string key)
        {
            var keyName = GetKeyName(key);
            return _cache.GetString(keyName);
            //string relativeFilePath = GetResourceFilePath();
            //string fullFilePath = Path.GetFullPath(relativeFilePath);
            //if (File.Exists(fullFilePath))
            //{
            //    string cacheKey = $"locale_{Thread.CurrentThread.CurrentCulture.Name}_{key}";
            //    string cacheValue = _cache.GetString(cacheKey);
            //    if (!string.IsNullOrEmpty(cacheValue)) return cacheValue;
            //    string result = GetValueFromJSON(key, Path.GetFullPath(relativeFilePath));
            //    if (!string.IsNullOrEmpty(result)) _cache.SetString(cacheKey, result);
            //    return result;
            //}
            //return default(string);
        }

        private string GetKeyName(string key, bool useParentCulture = false)
        {
            return GetKeyName(key, GetCultureName(useParentCulture));
        }

        private string GetKeyName(string key, string cultureName)
        {
            return $"{ResourcePrefix}.{cultureName}.{key}";
        }

        private string GetCultureName(bool useParentCulture = false)
        {
            if (useParentCulture && !Thread.CurrentThread.CurrentCulture.IsNeutralCulture)
                return Thread.CurrentThread.CurrentCulture.Parent.Name;
            else
                return Thread.CurrentThread.CurrentCulture.Name;
        }

        private string ResourcePrefix => _resourceSource ?? _baseName ?? string.Empty;

        //private string GetResourceFilePath(bool useParentCulture = false)
        //{
        //    string relativeFilePath = string.Empty;
        //    if (!string.IsNullOrEmpty(_location))
        //        relativeFilePath = $"{_location}/";
        //    if (!string.IsNullOrEmpty(_resourceSource))
        //        relativeFilePath = $"{relativeFilePath}{_resourceSource}.";
        //    else if (!string.IsNullOrEmpty(_baseName))
        //        relativeFilePath = $"{relativeFilePath}{_baseName}.";
        //    if (useParentCulture && !Thread.CurrentThread.CurrentCulture.IsNeutralCulture)
        //        relativeFilePath = $"{relativeFilePath}{Thread.CurrentThread.CurrentCulture.Parent.Name}";
        //    else
        //        relativeFilePath = $"{relativeFilePath}{Thread.CurrentThread.CurrentCulture.Name}";
        //    return $"{relativeFilePath}.json";
        //}

        //private string GetValueFromJSON(string propertyName, string filePath)
        //{
        //    if (propertyName == null) return default;
        //    if (filePath == null) return default;
        //    using (var str = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        //    using (var sReader = new StreamReader(str))
        //    using (var reader = new JsonTextReader(sReader))
        //    {
        //        while (reader.Read())
        //        {
        //            if (reader.TokenType == JsonToken.PropertyName && (string)reader.Value == propertyName)
        //            {
        //                reader.Read();
        //                return _serializer.Deserialize<string>(reader);
        //            }
        //        }
        //        return default;
        //    }
        //}
    }
}