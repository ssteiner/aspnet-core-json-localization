using Microsoft.Extensions.Localization;

namespace JsonLocalizationLib.Extensions
{
    public static class StartupExtensions
    {
        public static IServiceCollection EnableJsonFileLocalization(this IServiceCollection services, Action<LocalizationOptions> setupAction = null)
        {
            var srv = services;
            if (setupAction != null)
                srv = services.AddLocalization(setupAction);
            else
                srv = services.AddLocalization();
            return srv
                .AddHttpContextAccessor()
                .AddSingleton<LocalizationMiddleware>()
                .AddDistributedMemoryCache()
                .AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>()
                .AddSingleton<JsonFileCache>();
        }

        public static IApplicationBuilder ConfigureJsonFileLocalization(this IApplicationBuilder app, RequestLocalizationOptions options = null)
        {
            var myApp = app;
            if (options != null)
                myApp = app.UseRequestLocalization(options);
            else
                myApp = app.UseRequestLocalization();
            return EnableLocalization(myApp);
        }

        private static IApplicationBuilder EnableLocalization(IApplicationBuilder app)
        {
            var fileCache = app.ApplicationServices.GetService<JsonFileCache>();
            var lifeTime = app.ApplicationServices.GetService<IHostApplicationLifetime>();
            if (lifeTime != null)
                lifeTime.ApplicationStopping.Register(() => OnShutDown(fileCache));
            Task.Run(() => fileCache.Start());
            return app.UseMiddleware<LocalizationMiddleware>();
        }

        private static void OnShutDown(JsonFileCache cache)
        {
            cache.Stop();
        }

    }
}
