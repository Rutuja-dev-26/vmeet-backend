using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using VMeetTool.Helpers;

namespace VMeetTool.Filters
{
    public class JwtAuthorizeAttribute : AuthorizationFilterAttribute
    {
        public override void OnAuthorization(HttpActionContext actionContext)
        {
            var authHeader = actionContext.Request.Headers.Authorization;

            if (authHeader == null || !authHeader.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            {
                Deny(actionContext, "Authorization header missing or not Bearer.");
                return;
            }

            var token = authHeader.Parameter;

            if (string.IsNullOrWhiteSpace(token))
            {
                Deny(actionContext, "Token is empty.");
                return;
            }

            var principal = JwtHelper.ValidateToken(token);

            if (principal == null)
            {
                Deny(actionContext, "Invalid or expired token.");
                return;
            }

            Thread.CurrentPrincipal = principal;
            actionContext.RequestContext.Principal = principal;
        }

        private static void Deny(HttpActionContext ctx, string reason)
        {
            ctx.Response = ctx.Request.CreateErrorResponse(
                HttpStatusCode.Unauthorized, reason);
        }
    }
}