using Microsoft.AspNetCore.Mvc.Filters;

namespace ProjectTracker.Web.Helpers;

/// <summary>
/// Turns any exception thrown by an API action into the standard JSON error (see <see cref="ApiResults.ServerError"/>),
/// so controller actions do not each need their own try/catch.
/// </summary>
public sealed class ApiExceptionFilter(ILogger<ApiExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var route = context.ActionDescriptor.AttributeRouteInfo?.Template ?? context.ActionDescriptor.DisplayName ?? "api";
        context.Result = ApiResults.ServerError(context.Exception, logger, route);
        context.ExceptionHandled = true;
    }
}
