# 2026-06-09: Multipart/Alternative Email Sending

## Summary

Adds `bodyFormat: "multipart"` to `send_email`, allowing agents to provide
both a plain-text fallback and an HTML body in a single message
(`multipart/alternative` MIME). This eliminates formatting failures that
occurred when agents sent plain text with `bodyFormat: "html"` or vice versa.

## New parameters

| Parameter  | Required when             | Description                          |
|------------|---------------------------|--------------------------------------|
| `textBody` | `bodyFormat="multipart"`  | Plain-text fallback body             |
| `htmlBody` | `bodyFormat="multipart"`  | HTML body (must be real HTML markup) |

## Example

```json
{
  "to": ["recipient@example.com"],
  "subject": "Hello",
  "bodyFormat": "multipart",
  "textBody": "Plain text fallback for clients that cannot render HTML.",
  "htmlBody": "<html><body><h1>Hello</h1><p>Rich content.</p></body></html>"
}
```

## Provider behavior

| Provider       | Behavior                                                                 |
|----------------|--------------------------------------------------------------------------|
| Google (Gmail) | True `multipart/alternative` MIME via MimeKit `BodyBuilder`             |
| IMAP           | True `multipart/alternative` MIME via MimeKit `BodyBuilder`             |
| Microsoft 365  | `htmlBody` sent as `BodyType.Html`; `textBody` dropped (Graph SDK limit)|
| Outlook.com    | `htmlBody` sent as `BodyType.Html`; `textBody` dropped (Graph SDK limit)|

When `bodyFormat` is `"multipart"` on an M365 or Outlook.com account the
provider logs a `Warning` explaining that the plain-text body will not be
included.

## Backward compatibility

Existing calls using `body` + `bodyFormat: "html"` or `"text"` are unchanged.
`textBody` and `htmlBody` are ignored unless `bodyFormat` is `"multipart"`.
