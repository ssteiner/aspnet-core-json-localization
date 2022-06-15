using Microsoft.Extensions.Localization;
using System.Globalization;

namespace JsonLocalizationLib.Extensions
{
    public static class JsonStringLocalizerExtensions
    {
        /// <summary>
        /// gets a localized string in case the localizer is a cref="JsonStringLocalizer"
        /// </summary>
        /// <param name="localizer"></param>
        /// <param name="useParentCulture">also search the parent culture for a match</param>
        /// <param name="cultureName">specific culture name to use</param>
        /// <returns></returns>
        public static LocalizedString GetString(this IStringLocalizer localizer, string name, bool useParentCulture = true, CultureInfo culture = null)
        {
            if (localizer is JsonStringLocalizer jsonStringLocalizer)
            {
                var value = jsonStringLocalizer.GetString(name, false, culture);
                if (value == null && useParentCulture)
                    value = jsonStringLocalizer.GetString(name, true, culture);
                return JsonStringLocalizer.GenerateString(name, value);
            }
            return localizer.GetString(name);
        }

        /// <summary>
        /// gets all localized strings
        /// </summary>
        /// <param name="localizer"></param>
        /// <param name="includeParentCulture"></param>
        /// <param name="cultureName">the culture to use (overrides the current (UI) culture set in the CurrentThread</param>
        /// <param name="includedResources">other resources to include in the search (used for on-the-fly resource inheritance)</param>
        /// <returns></returns>
        public static IEnumerable<LocalizedString> GetAllStrings(this IStringLocalizer localizer, bool includeParentCulture, string cultureName = null, params string[] includedResources)
        {
            if (localizer is JsonStringLocalizer jsonStringLocalizer)
            {
                if (!string.IsNullOrEmpty(cultureName))
                {
                    try
                    {
                        var ci = new CultureInfo(cultureName);
                        return jsonStringLocalizer.GetAllStrings(includeParentCulture, ci, includedResources);
                    }
                    catch (Exception) { }
                }
                return jsonStringLocalizer.GetAllStrings(includeParentCulture, null, includedResources);
            }
            return localizer.GetAllStrings(includeParentCulture);
        }
    }
}
