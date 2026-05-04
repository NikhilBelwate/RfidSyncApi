using System.Diagnostics;
using System.Text.Json.Serialization;

namespace RfidSyncApi.Application.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
//  Structured JSON error response — returned on unhandled exceptions
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Root error envelope returned as the JSON body for 4xx / 5xx responses.
/// Includes the source location (file + method + line) extracted from the
/// exception's stack trace — available when PDB / debug symbols are present.
/// </summary>
public class ApiErrorDetail
{
    /// <summary>Short machine-readable code, e.g. INTERNAL_SERVER_ERROR.</summary>
    [JsonPropertyName("error_code")]
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>Human-readable description of what went wrong.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>CLR exception type name (e.g. InvalidOperationException).</summary>
    [JsonPropertyName("exception_type")]
    public string? ExceptionType { get; set; }

    /// <summary>
    /// The exact source location where the exception originated,
    /// extracted from the innermost stack frame with symbol info.
    /// Line number is 0 when PDB files are not deployed.
    /// </summary>
    [JsonPropertyName("location")]
    public ErrorLocation? Location { get; set; }

    /// <summary>All stack frames with available symbol information.</summary>
    [JsonPropertyName("stack_frames")]
    public List<ErrorLocation>? StackFrames { get; set; }

    /// <summary>Unwrapped inner exception chain (recursive).</summary>
    [JsonPropertyName("inner_exception")]
    public InnerExceptionDetail? InnerException { get; set; }

    /// <summary>ASP.NET Core trace identifier — correlates with server logs.</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    /// <summary>UTC timestamp of the error.</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a fully-populated <see cref="ApiErrorDetail"/> from a live exception.
    /// Extracts file names, method names, and line numbers from stack frames via PDB symbols.
    /// </summary>
    public static ApiErrorDetail From(Exception ex, string errorCode, string requestId)
    {
        var st = new StackTrace(ex, fNeedFileInfo: true);
        var frames = st.GetFrames() ?? Array.Empty<StackFrame>();

        // Innermost frame with a real line number gets promoted to "location"
        var originFrame = frames.FirstOrDefault(f => f.GetFileLineNumber() > 0)
                       ?? frames.FirstOrDefault();

        return new ApiErrorDetail
        {
            ErrorCode     = errorCode,
            Message       = ex.Message,
            ExceptionType = ex.GetType().Name,
            RequestId     = requestId,
            Timestamp     = DateTime.UtcNow,
            Location      = originFrame is not null ? ErrorLocation.From(originFrame) : null,
            StackFrames   = frames
                                .Where(f => f.GetFileLineNumber() > 0)
                                .Select(ErrorLocation.From)
                                .ToList(),
            InnerException = ex.InnerException is not null
                                ? InnerExceptionDetail.From(ex.InnerException)
                                : null,
        };
    }
}

/// <summary>One resolved stack frame — file, method, and line number.</summary>
public class ErrorLocation
{
    /// <summary>Source file name only (no full path — avoids leaking directory structure).</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    /// <summary>Declaring type + method name.</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>1-based line number. 0 when PDB symbols are not available.</summary>
    [JsonPropertyName("line")]
    public int Line { get; set; }

    internal static ErrorLocation From(StackFrame frame)
    {
        var method = frame.GetMethod();
        var declaring = method?.DeclaringType?.Name ?? string.Empty;
        var methodName = method?.Name ?? string.Empty;

        return new ErrorLocation
        {
            File   = System.IO.Path.GetFileName(frame.GetFileName() ?? string.Empty),
            Method = string.IsNullOrEmpty(declaring) ? methodName : $"{declaring}.{methodName}",
            Line   = frame.GetFileLineNumber(),
        };
    }
}

/// <summary>Condensed representation of an inner / wrapped exception.</summary>
public class InnerExceptionDetail
{
    [JsonPropertyName("exception_type")]
    public string ExceptionType { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Recursively unwraps further inner exceptions.</summary>
    [JsonPropertyName("inner_exception")]
    public InnerExceptionDetail? InnerException { get; set; }

    internal static InnerExceptionDetail From(Exception ex) => new()
    {
        ExceptionType  = ex.GetType().Name,
        Message        = ex.Message,
        InnerException = ex.InnerException is not null ? From(ex.InnerException) : null,
    };
}
