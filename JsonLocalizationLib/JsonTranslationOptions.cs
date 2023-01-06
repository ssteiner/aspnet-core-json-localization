namespace JsonLocalizationLib
{
    public class JsonTranslationOptions
    {
        /// <summary>
        /// full path to the resource files
        /// </summary>
        public string ResourcePath { get; set; }

        /// <summary>
        /// path of the resources, relative to the application (empty = Resources)
        /// </summary>
        public string ResourceFolder { get; set; }

        /// <summary>
        /// two character ISO code of the fallback culture
        /// </summary>
        public string FallbackCulture { get; set; }
    }
}
