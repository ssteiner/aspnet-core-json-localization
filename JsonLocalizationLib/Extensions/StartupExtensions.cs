using Microsoft.Extensions.Localization;

namespace JsonLocalizationLib.Extensions
{
    public static class StartupExtensions
    {
        public static IServiceCollection EnableJsonFileLocalization(this IServiceCollection services)
        {
            return services.AddLocalization()
                .AddHttpContextAccessor()
                .AddSingleton<LocalizationMiddleware>()
                .AddDistributedMemoryCache()
                .AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>()
                .AddSingleton<JsonFileCache>();
        }

        public static IApplicationBuilder ConfigureJsonFileLocalization(this IApplicationBuilder app)
        {
            var fileCache = app.ApplicationServices.GetService<JsonFileCache>();
            Task.Run(() => fileCache.Start());
            return app.UseMiddleware<LocalizationMiddleware>();
        }

        public static IApplicationBuilder ConfigureJsonFileLocalization(this IApplicationBuilder app, RequestLocalizationOptions options)
        {
            app.UseRequestLocalization(options);
            var fileCache = app.ApplicationServices.GetService<JsonFileCache>();
            Task.Run(() => fileCache.Start());
            return app.UseMiddleware<LocalizationMiddleware>();
        }
    }
}
