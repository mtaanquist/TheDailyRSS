namespace TheDailyRSS.Server.Endpoints;

/// <summary>Uniform RFC7807 error responses. The framework already emits ProblemDetails for
/// validation failures (AddValidation) and unhandled exceptions (AddProblemDetails/UseExceptionHandler);
/// these helpers make hand-written endpoint failures use the same shape instead of an ad-hoc
/// <c>{ error = ... }</c> object, so clients see a single error schema.</summary>
public static class ApiResults
{
    /// <summary>A 400 Bad Request carrying <paramref name="detail"/> as the ProblemDetails detail.</summary>
    public static IResult Fail(string detail) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>A 409 Conflict carrying <paramref name="detail"/> as the ProblemDetails detail.</summary>
    public static IResult Conflict(string detail) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict);

    /// <summary>A 403 Forbidden carrying <paramref name="detail"/> as the ProblemDetails detail.</summary>
    public static IResult Forbidden(string detail) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status403Forbidden);
}
