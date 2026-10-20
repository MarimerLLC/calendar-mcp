# Tool Arguments Accept Quoted Booleans and Numbers

**Date**: October 19, 2026
**Version**: 1.8.4

---

## Summary

Some clients and models send booleans and numbers as strings, for example
`"includeAttendeeDetails": "true"` or `"count": "50"`. System.Text.Json won't bind a JSON string to
a `bool` or an `int`, so the call failed before the tool ran:
`The JSON value could not be converted to System.Boolean`. RockBot hit this in live testing and had
to retry. Every tool now accepts quoted values for its boolean and numeric parameters.

## What changed

### `StringArgumentCoercion` (new, `CalendarMcp.Core.Configuration`)

- A call-tool filter that runs before the SDK binds a tool's arguments. It reads the tool's input
  schema and rewrites string arguments for properties typed `boolean`, `integer` or `number`, or one
  of those with `null` (a nullable parameter):
  - `"true"`/`"false"`, in any case, become JSON booleans.
  - A JSON number literal such as `"50"`, `"-3"` or `"0.25"` becomes that number. Its digits are
    kept exactly, so binding still applies the parameter's own checks: `"2.5"` for an `int`, or a
    value out of range, is rejected as before.
  - Surrounding spaces are ignored.
- It is schema-driven, so it covers every tool, current and future, with no per-tool code. The
  advertised schemas are unchanged.
- Other strings, such as `"yes"`, `"fifty"`, `"0x10"` or `""`, are left alone and still fail binding
  with the usual error. String parameters keep their text as is, even when it looks like `"true"`
  or `"50"`.
- Each rewrite is logged at Information:
  `Read string arguments {Names} as booleans or numbers for {Tool}`.
- `WithStringScalarArguments()` registers it. Both the HTTP and stdio servers call it.

### Tests

`StringArgumentCoercionTests` runs a real MCP server and client over in-memory pipes, so it covers
the SDK's argument binding as well as the filter. Without the filter, the quoted-boolean and
quoted-number cases fail with the binding error.
