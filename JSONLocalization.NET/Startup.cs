using JsonLocalizationLib;
using JsonLocalizationLib.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System.Globalization;

namespace JSONLocalization.NET
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<JsonTranslationOptions>(Configuration.GetSection("JsonLocalization"));
            services.EnableJsonFileLocalization(options => 
            {
                options.ResourcesPath = "Resources";
            });
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "JSONLocalization.NET", Version = "v1" });
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var options = GetLocalizationOptions();
            app.ConfigureJsonFileLocalization(options);
            app.UseStaticFiles();
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "JSONLocalization.NET v1"));
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }

        private static RequestLocalizationOptions GetLocalizationOptions()
        {
            //set supported cultures and default culture
            var supportedCultures = new[]
            {
                new CultureInfo("en"),
                new CultureInfo("de"),
                new CultureInfo("fr"),
                new CultureInfo("it"),
            };
            var supportedUICultures = new[]
            {
                new CultureInfo("en-US"),
                new CultureInfo("de-DE"),
                new CultureInfo("fr-CH"),
                new CultureInfo("it-CH"),
            };
            return new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("en-US"),
                SupportedUICultures = supportedUICultures,
                SupportedCultures = supportedCultures
            };
        }

    }
}
