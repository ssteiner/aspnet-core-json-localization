using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using System.Reflection;

namespace JsonLocalizationLib
{
    public class JsonStringLocalizerFactory : IStringLocalizerFactory
    {
        private readonly IDistributedCache _cache;

        public JsonStringLocalizerFactory(IDistributedCache cache)
        {
            _cache = cache;
        }

        public IStringLocalizer Create(Type resourceSource)
        {
            string[] includedTranslations = null;
            try
            {
                var includedTranslationsAttribute = resourceSource.GetCustomAttribute<IncludedTranslationsAttribute>(true);
                includedTranslations = includedTranslationsAttribute?.IncludedResources;
            }
            catch (Exception) { }
            return new JsonStringLocalizer(_cache, resourceSource.Name, includedTranslations);
        }
            

        public IStringLocalizer Create(string baseName, string location) =>
            new JsonStringLocalizer(_cache, baseName, location);
    }
}
