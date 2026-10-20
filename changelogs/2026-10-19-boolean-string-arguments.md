# Boolean Tool Arguments Accept "true"/"false" Strings

**Date**: October 19, 2026

---

## Summary

Some clients and models send booleans as strings, for example `"includeAttendeeDetails": "true"`.
System.Text.Json won't bind a JSON string to a `bool`, so the call failed before the tool ran:
`The JSON value could not be converted to System.Boolean`. RockBot hit this in live testing and
had to retry. Every tool now accepts `"true"` and `"false"` for its boolean parameters.

## What changed

### `BooleanArgumentCoercion` (new, `CalendarMcp.Core.Configuration`)

- A call-tool filter that runs before the SDK binds a tool's arguments. It reads the tool's input
  schema, and for each property typed `boolean` (or `boolean` or `null`, a nullable bool) it
  rewrites a `"true"`/`"false"` string argument to a JSON boolean. Case and surrounding spaces
  don't matter.
- It is schema-driven, so it covers every tool, current and future, with no per-tool code. The
  advertised schemas are unchanged and still say `boolean`.
- Other strings for a boolean parameter, such as `"yes"`, are left alone and still fail binding
  with the usual error. String parameters keep the text `"true"` as is.
- Each rewrite is logged at Information: `Read string arguments {Names} as booleans for {Tool}`.
- `WithBooleanStringArguments()` registers it. Both the HTTP and stdio servers call it.

### Tests

`BooleanArgumentCoercionTests` runs a real MCP server and client over in-memory pipes, so it covers
the SDK's argument binding as well as the filter. Without the filter, the string-boolean cases fail
with the same binding error.
