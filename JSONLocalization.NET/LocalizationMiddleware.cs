using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using System.Threading;
using System.Threading.Tasks;

namespace JSONLocalization.NET
{
    public class LocalizationMiddleware : IMiddleware
    {
        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            var feat = context.Features.Get<IRequestCultureFeature>();
            var requestCulture = feat.RequestCulture;
            if (requestCulture != null)
            {
                Thread.CurrentThread.CurrentCulture = requestCulture.Culture;
                Thread.CurrentThread.CurrentUICulture = requestCulture.UICulture;
            }
            await next(context);
        }

    }
}
