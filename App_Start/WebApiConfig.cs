using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace VMeetTool
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Always include full exception details in responses (safe for dev/internal)
            config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
