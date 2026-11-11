using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Domain;

namespace Orders.Api.Infrastructure;

/// <summary>
/// Translates expected exceptions into RFC 9457 problem details:
/// malformed input becomes 400, business rule violations and lost optimistic-concurrency
/// races become 409. Anything else falls through to the default 500 handler.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = "The request is invalid.",
                Detail = badRequest.Message,
            },
            DomainException domain => Conflict(domain.Message),
            DbUpdateConcurrencyException => Conflict("The order was modified by another request. Reload it and try again."),
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status400BadRequest;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    private static ProblemDetails Conflict(string detail) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "The request conflicts with the current state of the order.",
        Detail = detail,
    };
}
