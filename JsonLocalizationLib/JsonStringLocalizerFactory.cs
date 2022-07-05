using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using System.Collections.Concurrent;
using System.Reflection;

namespace JsonLocalizationLib
{
    public class JsonStringLocalizerFactory : IStringLocalizerFactory
    {
        private readonly IDistributedCache _cache;
        private readonly ConcurrentDictionary<Type, IStringLocalizer> localizerCache;

        public JsonStringLocalizerFactory(IDistributedCache cache)
        {
            _cache = cache;
            localizerCache = new ConcurrentDictionary<Type, IStringLocalizer>();
        }

        public IStringLocalizer Create(Type resourceSource)
        {
            return localizerCache.GetOrAdd(resourceSource, CreateLocalizer);
        }

        private IStringLocalizer CreateLocalizer(Type resourceSource)
        {
            string[] includedTranslations = null;
            try
            {
                var includedTranslationsAttribute = resourceSource.GetCustomAttributes<IncludedTranslationsAttribute>(true);
                if (includedTranslationsAttribute.Any())
                    includedTranslations = includedTranslationsAttribute.SelectMany(x => x.IncludedResources)?.Distinct()?.ToArray();
            }
            catch (Exception) { }
            return new JsonStringLocalizer(_cache, resourceSource.Name, includedTranslations);
        }

        public IStringLocalizer Create(string baseName, string location) =>
            new JsonStringLocalizer(_cache, baseName, location);
    }
}
