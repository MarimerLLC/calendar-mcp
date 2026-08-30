using CalendarMcp.Core.Services;

namespace CalendarMcp.HttpServer.Endpoints;

public static class AttachmentEndpoints
{
    /// <summary>
    /// Maps the attachment endpoints and returns the group, so the caller can attach the same
    /// authorization policy that guards the MCP endpoint.
    /// </summary>
    public static RouteGroupBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/attachments")
            .WithTags("Attachments")
            .DisableAntiforgery();

        group.MapPost("/", UploadAsync)
            .WithName("UploadAttachment")
            .WithSummary("Upload a binary attachment for use in send_email.")
            .Produces<UploadResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status507InsufficientStorage);

        group.MapGet("/{attachmentId}", DownloadAsync)
            .WithName("DownloadAttachment")
            .WithSummary("Read the bytes of a stored attachment without consuming it. Entry remains in the store until send_email consumes it, DELETE removes it, or TTL expires.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{attachmentId}", DeleteAsync)
            .WithName("DeleteAttachment")
            .WithSummary("Remove an attachment from the store before its TTL.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static IResult DownloadAsync(
        string attachmentId,
        IAttachmentStore store)
    {
        var entry = store.TryRead(attachmentId);
        if (entry == null)
        {
            return Results.Problem(
                title: "Attachment not found",
                detail: "The attachment ID is unknown, expired, or was already consumed by send_email.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.File(
            entry.Bytes,
            contentType: entry.ContentType ?? "application/octet-stream",
            fileDownloadName: entry.Name);
    }

    private static IResult DeleteAsync(
        string attachmentId,
        IAttachmentStore store)
    {
        return store.TryDelete(attachmentId)
            ? Results.NoContent()
            : Results.Problem(
                title: "Attachment not found",
                detail: "Nothing to delete; the attachment ID is unknown, expired, or was already consumed.",
                statusCode: StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        IAttachmentStore store,
        ILogger<InMemoryAttachmentStore> logger,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Results.Problem(
                title: "Multipart form-data required",
                detail: "POST a multipart/form-data body with exactly one file part.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Invalid multipart body",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (form.Files.Count != 1)
        {
            return Results.Problem(
                title: "Exactly one file required",
                detail: $"Got {form.Files.Count} file parts; expected 1.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var file = form.Files[0];

        // Read into memory; per-attachment cap is enforced by the store.
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        var stored = store.Put(file.FileName, file.ContentType, bytes);
        if (stored == null)
        {
            // Store rejected: either over per-attachment cap or global cap.
            // We can't tell which without more API surface; report the more
            // common one (per-attachment) when the file is itself oversized.
            logger.LogWarning("Attachment upload rejected: name={Name}, size={Size}", file.FileName, bytes.Length);
            if (bytes.Length > 10 * 1024 * 1024)
            {
                return Results.Problem(
                    title: "Attachment too large",
                    detail: "Each upload must be 10 MB or less.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }
            return Results.Problem(
                title: "Attachment store full",
                detail: "Server attachment cache is at capacity. Try again in a few minutes.",
                statusCode: StatusCodes.Status507InsufficientStorage);
        }

        return Results.Created(
            $"/attachments/{stored.Id}",
            new UploadResponse(stored.Id, stored.Name, stored.ContentType, bytes.Length, stored.ExpiresAt));
    }

    public sealed record UploadResponse(
        string AttachmentId,
        string Name,
        string? ContentType,
        long Size,
        DateTimeOffset ExpiresAt);
}
