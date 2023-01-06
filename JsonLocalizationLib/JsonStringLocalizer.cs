using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Globalization;

namespace JsonLocalizationLib
{
    public class JsonStringLocalizer : IStringLocalizer
    {

        private readonly IDistributedCache _cache;
        private readonly IOptionsMonitor<JsonTranslationOptions> _optionsMonitor;
        private readonly bool useUiCulture = true;
        private readonly string[] _inheritedResources;

        //private readonly JsonSerializer _serializer = new();

        private readonly string _resourceSource, _location = "Resources", _baseName;
        private readonly CultureInfo fallbackCulture;

        public JsonStringLocalizer(IDistributedCache cache, string resourceSource, IOptionsMonitor<JsonTranslationOptions> options, params string[] inheritedResources)
        {
            _cache = cache;
            _optionsMonitor = options;
            _resourceSource = resourceSource;
            _inheritedResources = inheritedResources;
            if (options.CurrentValue.FallbackCulture != null)
                fallbackCulture = CultureInfo.CreateSpecificCulture(options.CurrentValue.FallbackCulture);
        }

        public JsonStringLocalizer(IDistributedCache cache, string baseName, string location, IOptionsMonitor<JsonTranslationOptions> options)
        {
            _cache = cache;
            _optionsMonitor = options;
            _location = location;
            _baseName = baseName;
            if (options.CurrentValue.FallbackCulture != null)
                fallbackCulture = CultureInfo.CreateSpecificCulture(options.CurrentValue.FallbackCulture);
        }

        public LocalizedString this[string name]
        {
            get
            {
                string value = GetString(name);
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
            bool sort = false, attemptFallback = result.Count == 0 && fallbackCulture != null && fallbackCulture.Name != culture.Name;
            if (includedResources?.Length > 0)
            {
                sort = true;
                foreach (var resourcePrefix in includedResources)
                {
                    GetAllTranslations(resourcePrefix, culture, includeParentCulture, result, true);
                }
            }
            else if (_inheritedResources?.Length > 0)
            {
                sort = true;
                foreach (var resourcePrefix in _inheritedResources)
                {
                    GetAllTranslations(resourcePrefix, culture, includeParentCulture, result, true);
                }
            }
            if (attemptFallback)
            {
                var fallbackResult = GetAllStrings(includeParentCulture, fallbackCulture, includedResources);
                result.AddRange(fallbackResult);
                sort = true;
            }
            if (sort)
                result = result.OrderBy(n => n.Name).ToList();
            return result;
        }

        internal static LocalizedString GenerateString(string name, string value)
        {
            return new LocalizedString(name, value ?? name, value == null);
        }

        private void GetAllTranslations(string resourcePrefix, CultureInfo culture, bool includeParentCulture, List<LocalizedString> result, bool isInherited = false)
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
                        result.Add(new LocalizedString(key, value, false, isInherited ? $"{resourcePrefix}.{culture.Name}": null));
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
                                result.Add(new LocalizedString(key, value, false, isInherited ? $"{resourcePrefix}.{parentCulture.Name}": null));
                        }
                    }
                }
            }
        }

        public string GetString(string key, CultureInfo culture = null)
        {
            var keyName = GetKeyName(key, false, culture: culture);
            var value = _cache.GetString(keyName);
            value ??= GetStringFromInheritedResources(keyName);
            if (string.IsNullOrEmpty(value)) // stil not found.. now try again, this time also checking the parent culture
            {
                keyName = GetKeyName(key, true, culture: culture);
                value = _cache.GetString(keyName);
                value ??= GetStringFromInheritedResources(keyName);
            }
            if (string.IsNullOrEmpty(value) && fallbackCulture != null && fallbackCulture.Name != culture?.Name) // try the fallback culture
                return GetString(key, fallbackCulture);
            return value;
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
            if (culture == null)
            {
                if (useParentCulture && !Thread.CurrentThread.CurrentCulture.IsNeutralCulture)
                    culture = useUiCulture ? Thread.CurrentThread.CurrentUICulture.Parent : Thread.CurrentThread.CurrentCulture.Parent;
                else
                    culture = useUiCulture ? Thread.CurrentThread.CurrentUICulture : Thread.CurrentThread.CurrentCulture.Parent;
            }
            if (useParentCulture && !culture.IsNeutralCulture)
                return culture.Parent;
            return culture;
        }

        private string ResourcePrefix => _resourceSource ?? _baseName ?? string.Empty;

    }
}