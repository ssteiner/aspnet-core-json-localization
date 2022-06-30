using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using System.Globalization;

namespace JsonLocalizationLib
{
    public class JsonStringLocalizer : IStringLocalizer
    {

        private readonly IDistributedCache _cache;
        private readonly bool useUiCulture = true;
        private readonly string[] _inheritedResources;

        //private readonly JsonSerializer _serializer = new();

        private readonly string _resourceSource, _location = "Resources", _baseName;

        public JsonStringLocalizer(IDistributedCache cache, string resourceSource, params string[] inheritedResources)
        {
            _cache = cache;
            _resourceSource = resourceSource;
            _inheritedResources = inheritedResources;
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
                if (value == null)
                    value = GetString(name, true);
                return GenerateString(name, value);
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

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCulture)
        {
            var culture = GetCultureName();
            return GetAllStrings(includeParentCulture, culture, _inheritedResources);
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCulture, CultureInfo culture, params string[] includedResources)
        {
            List<LocalizedString> result = new();
            GetAllTranslations(ResourcePrefix, culture, includeParentCulture, result);
            if (includedResources?.Length > 0)
            {
                foreach (var resourcePrefix in includedResources)
                {
                    GetAllTranslations(resourcePrefix, culture, includeParentCulture, result);
                }
            }
            return result;
        }

        internal static LocalizedString GenerateString(string name, string value)
        {
            return new LocalizedString(name, value ?? name, value == null);
        }

        private void GetAllTranslations(string resourcePrefix, CultureInfo culture, bool includeParentCulture, List<LocalizedString> result)
        {
            var keyListKey = $"keys_{resourcePrefix}.{culture.Name}";
            List<string> existingKeys = null;
            var existingKeysString = _cache.GetString(keyListKey);
            if (existingKeysString != null)
                existingKeys = JsonConvert.DeserializeObject<List<string>>(existingKeysString);
            if (existingKeys != null)
            {
                foreach (var key in existingKeys)
                {
                    var fullKey = GetKeyName(key, culture.Name, resourcePrefix);
                    var value = _cache.GetString(fullKey) ?? fullKey;
                    if (!result.Any(x => x.Name == key))
                        result.Add(new LocalizedString(key, value, false));
                }
            }
            if (includeParentCulture && !culture.IsNeutralCulture)
            {
                var parentCulture = culture.Parent;
                if (parentCulture.Name != culture.Name)
                {
                    keyListKey = $"keys_{resourcePrefix}.{parentCulture.Name}";
                    existingKeysString = _cache.GetString(keyListKey);
                    if (existingKeysString != null)
                        existingKeys = JsonConvert.DeserializeObject<List<string>>(existingKeysString);
                    if (existingKeys != null)
                    {
                        foreach (var key in existingKeys)
                        {
                            var fullKey = GetKeyName(key, parentCulture.Name, resourcePrefix);
                            var value = _cache.GetString(fullKey) ?? fullKey;
                            if (!result.Any(x => x.Name == key))
                                result.Add(new LocalizedString(key, value, false));
                        }
                    }
                }
            }
        }

        public string GetString(string key, bool useParentCulture = false, CultureInfo culture = null)
        {
            var keyName = GetKeyName(key, useParentCulture, culture: culture);
            var value = _cache.GetString(keyName);
            return value ?? GetStringFromInheritedResources(keyName);
        }

        private string GetStringFromInheritedResources(string key, bool useParentCulture = false, CultureInfo culture = null)
        {
            if (_inheritedResources?.Length > 0)
            {
                foreach (var resourceName in _inheritedResources)
                {
                    var keyName = GetKeyName(key, useParentCulture, resourceName, culture);
                    var value = _cache.GetString(keyName);
                    if (value != null)
                        return value;
                }
            }
            return null;
        }

        private string GetKeyName(string key, bool useParentCulture = false, string resourceName = null, CultureInfo culture = null)
        {
            return GetKeyName(key, GetCultureName(useParentCulture, culture).Name, resourceName);
        }

        private string GetKeyName(string key, string cultureName, string resourceName = null)
        {
            return $"{resourceName ?? ResourcePrefix}.{cultureName}.{key}";
        }

        private CultureInfo GetCultureName(bool useParentCulture = false, CultureInfo culture = null)
        {
            if (culture != null)
            {
                if (useParentCulture && !culture.IsNeutralCulture)
                    return culture.Parent;
                return culture;
            }
            if (useParentCulture && !Thread.CurrentThread.CurrentCulture.IsNeutralCulture)
                return useUiCulture ? Thread.CurrentThread.CurrentUICulture.Parent : Thread.CurrentThread.CurrentCulture.Parent;
            else
                return useUiCulture ? Thread.CurrentThread.CurrentUICulture : Thread.CurrentThread.CurrentCulture.Parent;
        }

        private string ResourcePrefix => _resourceSource ?? _baseName ?? string.Empty;

    }
}