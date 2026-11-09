# OpenTelemetry Integration

## Overview

Adjutant uses OpenTelemetry for comprehensive observability across all components: structured logging, distributed tracing, and metrics collection.

## Key Features

- **Structured Logging**: Consistent log formatting with context
- **Distributed Tracing**: End-to-end request tracking across components
- **Metrics Collection**: Performance monitoring and usage analytics
- **Multiple Exporters**: Support for various backends (Console, OTLP, Jaeger, Prometheus, Azure Monitor)
- **Privacy-First**: Built-in redaction of sensitive data

## Configuration

See [Configuration](configuration.md#opentelemetry-configuration) for complete examples.

### Basic Setup (Development)

```json
{
  "telemetry": {
    "enabled": true,
    "serviceName": "adjutant",
    "console": {
      "enabled": true,
      "logLevel": "Debug"
    }
  }
}
```

### Production Setup

```json
{
  "telemetry": {
    "enabled": true,
    "serviceName": "adjutant",
    "serviceVersion": "1.0.0",
    "otlp": {
      "enabled": true,
      "endpoint": "http://collector:4317",
      "protocol": "grpc"
    },
    "sampling": {
      "samplingRate": 0.1
    },
    "redaction": {
      "enabled": true,
      "redactEmailContent": true,
      "redactTokens": true,
      "redactPii": true
    }
  }
}
```

## Instrumentation

### Activity Sources

```csharp
public static class Telemetry
{
    public static readonly ActivitySource ActivitySource = 
        new ActivitySource("Adjutant", "1.0.0");
}
```

### Tracing Examples

#### MCP Tool Execution

```csharp
public async Task<IEnumerable<EmailMessage>> GetEmailsAsync(
    string? accountId, 
    int count, 
    bool unreadOnly)
{
    using var activity = Telemetry.ActivitySource.StartActivity("MCP.GetEmails");
    activity?.SetTag("mcp.tool", "get_emails");
    activity?.SetTag("mcp.account_id", accountId ?? "all");
    activity?.SetTag("mcp.count", count);
    activity?.SetTag("mcp.unread_only", unreadOnly);
    
    try
    {
        var emails = await ExecuteAsync(accountId, count, unreadOnly);
        activity?.SetTag("mcp.result_count", emails.Count());
        return emails;
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        throw;
    }
}
```

#### Smart Router Decision

```csharp
public async Task<RoutingDecision> RouteAsync(RoutingRequest request)
{
    using var activity = Telemetry.ActivitySource.StartActivity("SmartRouter.Route");
    activity?.SetTag("router.tool", request.ToolName);
    activity?.SetTag("router.query_length", request.UserQuery.Length);
    
    var stopwatch = Stopwatch.StartNew();
    
    var decision = await ExecuteRoutingLogicAsync(request);
    
    stopwatch.Stop();
    activity?.SetTag("router.strategy", decision.RoutingStrategy);
    activity?.SetTag("router.confidence", decision.Confidence);
    activity?.SetTag("router.account_count", decision.AccountIds.Count);
    activity?.SetTag("router.latency_ms", stopwatch.ElapsedMilliseconds);
    
    return decision;
}
```

#### Provider Service API Call

```csharp
public async Task<IEnumerable<EmailMessage>> GetEmailsAsync(
    string accountId, 
    int count, 
    bool unreadOnly)
{
    using var activity = Telemetry.ActivitySource.StartActivity("M365Provider.GetEmails");
    activity?.SetTag("provider.type", "microsoft365");
    activity?.SetTag("provider.account_id", accountId);
    
    try
    {
        var token = await AcquireTokenAsync(accountId);
        activity?.SetTag("auth.cache_hit", token.Source == TokenSource.Cache);
        
        var emails = await CallGraphApiAsync(token, count, unreadOnly);
        activity?.SetTag("provider.result_count", emails.Count());
        
        return emails;
    }
    catch (MsalException ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, "Authentication failed");
        activity?.SetTag("error.type", "auth_failure");
        throw;
    }
}
```

### Logging

#### Structured Logging with Context

```csharp
_logger.LogInformation(
    "Account {AccountId} initialized successfully. Provider: {Provider}, Domains: {Domains}",
    account.Id,
    account.Provider,
    string.Join(", ", account.Domains));
```

#### Error Logging

```csharp
_logger.LogError(
    ex,
    "Failed to refresh token for account {AccountId}. Provider: {Provider}",
    accountId,
    provider);
```

#### Warning Logging

```csharp
_logger.LogWarning(
    "Router backend {Backend} took {LatencyMs}ms, exceeding target of {TargetMs}ms",
    backend,
    latency,
    targetLatency);
```

## Key Telemetry Metrics

### Router Metrics

**Decision Latency**:
- `router.decision_latency_ms`: Time to make routing decision
- Tags: `backend`, `strategy`, `tool_name`

**Decision Accuracy** (requires user feedback):
- `router.decision_accuracy`: Percentage of correct routing decisions
- Tags: `backend`, `strategy`

**Backend Performance**:
- `router.backend_latency_ms`: LLM backend response time
- `router.backend_errors`: Backend failure count
- Tags: `backend`, `model`

### Provider Service Metrics

**API Response Times**:
- `provider.api_latency_ms`: Time to complete API call
- Tags: `provider_type`, `operation`, `account_id`

**Authentication Metrics**:
- `provider.auth_success_count`: Successful authentications
- `provider.auth_failure_count`: Failed authentications
- `provider.token_cache_hit_rate`: Cache hit percentage
- Tags: `provider_type`, `account_id`

**API Call Volumes**:
- `provider.api_calls_total`: Total API calls
- Tags: `provider_type`, `operation`, `account_id`

### MCP Tool Metrics

**Tool Execution**:
- `mcp.tool_execution_count`: Tool invocation count
- `mcp.tool_execution_latency_ms`: Tool execution time
- Tags: `tool_name`, `account_id`

**Error Rates**:
- `mcp.tool_error_count`: Tool execution failures
- Tags: `tool_name`, `error_type`

### Multi-Account Aggregation Metrics

**Parallel Query Performance**:
- `aggregation.parallel_query_latency_ms`: Time to query all accounts
- `aggregation.account_query_latency_ms`: Per-account query time
- `aggregation.result_count`: Results per account
- Tags: `tool_name`, `account_count`

**Merge/Dedupe Performance**:
- `aggregation.merge_latency_ms`: Time to merge results
- `aggregation.dedupe_count`: Duplicates removed

## Trace Context Propagation

Traces flow through the entire request lifecycle:

```
AI Assistant
  → MCP Server (StartActivity: "MCP.GetEmails")
    → Smart Router (StartActivity: "SmartRouter.Route")
      → LLM Backend (StartActivity: "Router.LLM.CallApi")
    → Provider Service Factory (StartActivity: "Factory.ResolveProvider")
    → M365 Provider Service (StartActivity: "M365Provider.GetEmails")
      → Authentication (StartActivity: "M365Provider.AcquireToken")
      → Graph API Call (StartActivity: "M365Provider.CallGraphApi")
```

Each span includes:
- Unique span ID
- Parent span ID (for hierarchy)
- Start/end timestamps
- Tags (metadata)
- Status (OK, Error)
- Exceptions (if any)

## Exporters

### Console (Development)

**Output**:
```
[14:32:45 INF] Activity: MCP.GetEmails
  Tags:
    mcp.tool: get_emails
    mcp.account_id: work-account
    mcp.count: 20
    mcp.result_count: 15
  Duration: 234ms
```

### OTLP (OpenTelemetry Protocol)

**Use Case**: Send to OpenTelemetry Collector for routing to multiple backends

```json
{
  "telemetry": {
    "otlp": {
      "enabled": true,
      "endpoint": "http://localhost:4317",
      "protocol": "grpc"
    }
  }
}
```

### Jaeger (Distributed Tracing)

**Use Case**: Visualize trace spans in Jaeger UI

```json
{
  "telemetry": {
    "jaeger": {
      "enabled": true,
      "agentHost": "localhost",
      "agentPort": 6831
    }
  }
}
```

**Jaeger UI**: http://localhost:16686

### Prometheus (Metrics)

**Use Case**: Time-series metrics and alerting

```json
{
  "telemetry": {
    "prometheus": {
      "enabled": true,
      "port": 9090
    }
  }
}
```

**Metrics endpoint**: http://localhost:9090/metrics

### Azure Monitor / Application Insights

**Use Case**: Enterprise monitoring with Azure

```json
{
  "telemetry": {
    "azureMonitor": {
      "enabled": true,
      "connectionString": "InstrumentationKey=...;IngestionEndpoint=..."
    }
  }
}
```

## Privacy & Redaction

### Default Redaction Rules

**Always Redacted** (cannot be disabled):
- Access tokens
- Refresh tokens
- Client secrets
- API keys
- Authorization headers

**Configurable Redaction**:

```json
{
  "telemetry": {
    "redaction": {
      "enabled": true,
      "redactEmailContent": true,
      "redactTokens": true,
      "redactPii": true
    }
  }
}
```

### Redaction Implementation

```csharp
public static class TelemetryRedactor
{
    public static string RedactEmailAddress(string email)
    {
        if (!_config.RedactPii) return email;
        
        var domain = email.Split('@').LastOrDefault();
        return $"***@{domain}";
    }
    
    public static string RedactEmailContent(string content)
    {
        return _config.RedactEmailContent ? "[REDACTED]" : content;
    }
    
    public static string RedactToken(string token)
    {
        return "[REDACTED_TOKEN]";
    }
}
```

### Safe Metadata

**Never redacted** (safe for telemetry):
- Account IDs (e.g., "work-account")
- Provider types (e.g., "microsoft365")
- Email domains (e.g., "example.com")
- Message counts
- Timestamps
- Status codes
- Latency measurements

## Sampling

### Production Sampling

To reduce telemetry volume:

```json
{
  "telemetry": {
    "sampling": {
      "alwaysSample": false,
      "samplingRate": 0.1  // 10% of traces
    }
  }
}
```

### Adaptive Sampling

Sample based on request characteristics:

```csharp
var sampler = new CustomSampler();
sampler.AlwaysSampleErrors();  // Always trace errors
sampler.AlwaysSampleSlow(threshold: 1000);  // Always trace slow requests (>1s)
sampler.SampleNormal(rate: 0.1);  // 10% for normal requests
```

## Querying Telemetry

### Jaeger Queries

**Find slow router decisions**:
```
service="adjutant" 
operation="SmartRouter.Route" 
duration>500ms
```

**Find authentication failures**:
```
service="adjutant"
tag["error.type"]="auth_failure"
```

### Azure Monitor / KQL Queries

**Router performance by backend**:
```kql
customMetrics
| where name == "router.decision_latency_ms"
| summarize avg(value), percentiles(value, 50, 95, 99) by tostring(customDimensions.backend)
```

**Failed API calls by provider**:
```kql
dependencies
| where type == "HTTP"
| where success == false
| summarize count() by tostring(customDimensions.provider_type), resultCode
```

## Best Practices

1. **Start spans early**: Create activity at method entry
2. **Set tags before operations**: Tag before executing to ensure metadata even on exceptions
3. **Always record exceptions**: Use `activity?.RecordException(ex)`
4. **Use structured logging**: Include context in log messages
5. **Tag with account context**: Always include accountId for filtering
6. **Measure latency**: Use Stopwatch for timing
7. **Set meaningful status codes**: OK for success, Error for failures
8. **Keep tag names consistent**: Use namespaced tags (e.g., `mcp.*`, `router.*`, `provider.*`)
9. **Redact by default**: Privacy first, opt-in to more verbose telemetry
10. **Sample in production**: Don't trace 100% of requests at scale
