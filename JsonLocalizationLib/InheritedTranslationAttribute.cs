namespace JsonLocalizationLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class IncludedTranslationsAttribute: Attribute
    {

        public string[] IncludedResources { get; private set; }

        public IncludedTranslationsAttribute(params string[] includedResources)
        {
            IncludedResources = includedResources;
        }
    }
}
