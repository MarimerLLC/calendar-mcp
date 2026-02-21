# Smart Router

## Overview

The Smart Router determines which account(s) should handle a request when the user doesn't explicitly specify an account. It uses configurable LLM backends for intelligent classification.

## Key Design Principle

**Configurable AI Backend**: Users choose their routing backend based on privacy, cost, and performance preferences.

## Classification Strategy

### Priority Levels

1. **Explicit Account ID** (highest priority)
   - User specifies `accountId` in tool parameter
   - Router bypasses AI classification
   - Direct routing to specified account

2. **Domain Pattern Matching**
   - Email domain extraction (e.g., `@company.com`)
   - Lookup account with matching domain in configuration
   - Fast, deterministic routing for common cases

3. **LLM Classification** (when needed)
   - User intent analysis
   - Content keyword detection
   - Context-aware account selection
   - Fallback for ambiguous requests

4. **User Preferences**
   - Default account for unresolvable requests
   - Priority weights in account configuration
   - Historical pattern learning (future)

## LLM Backend Options

### Local Models (Ollama)

**Recommended Default: Phi-3.5-mini**
- Excellent classification accuracy
- 128K context length
- ~2.4GB quantized
- Fast inference (12+ tokens/sec on modest hardware)
- Strong reasoning capabilities

**Other Options**:
- **Qwen2-7B**: Better for complex decision-making
- **Qwen2-1.5B**: Ultra-lightweight, blazing fast

See [Local Models](local-models.md) for detailed recommendations.

### Cloud APIs

- **OpenAI**: GPT-4o-mini, GPT-4o
- **Anthropic**: Claude 3 Haiku, Claude 3.5 Sonnet
- **Azure OpenAI**: Deployed models

### Custom Endpoints

Any OpenAI-compatible API endpoint:
- Self-hosted models
- Custom inference servers
- Organization-specific deployments

## Router Interface

```csharp
public interface ISmartRouter
{
    /// <summary>
    /// Determines which account(s) should handle the request
    /// </summary>
    /// <param name="request">User's request with context</param>
    /// <returns>Account IDs and confidence scores</returns>
    Task<RoutingDecision> RouteAsync(RoutingRequest request);
}

public class RoutingRequest
{
    public string UserQuery { get; set; }
    public string? ExplicitAccountId { get; set; }
    public Dictionary<string, string> Context { get; set; }
    public string ToolName { get; set; }
}

public class RoutingDecision
{
    public List<string> AccountIds { get; set; }
    public string RoutingStrategy { get; set; } // "explicit", "domain", "llm", "default"
    public double Confidence { get; set; }
    public string Reasoning { get; set; }
}
```

## Classification Prompt Template

```
You are a smart router for a multi-account email and calendar system.

USER ACCOUNTS:
{account_list_with_domains_and_descriptions}

USER REQUEST:
"{user_query}"

TOOL: {tool_name}

TASK: Determine which account(s) should handle this request.

RULES:
1. If domain is mentioned (e.g., @company.com), prefer that account
2. For work-related keywords, prefer work accounts
3. For personal keywords, prefer personal accounts
4. If unclear, use the default account
5. For read operations (get_emails, list_calendars, get_contacts), you can return multiple accounts
6. For write operations (send_email, create_event, create_contact), return exactly ONE account

OUTPUT FORMAT (JSON):
{
  "account_ids": ["account-id-1", "account-id-2"],
  "confidence": 0.95,
  "reasoning": "Brief explanation"
}
```

## Routing Examples

### Example 1: Explicit Account ID
```json
Request: {
  "tool": "get_emails",
  "accountId": "work-account"
}
→ Router decision: Direct route to "work-account"
→ Strategy: "explicit"
```

### Example 2: Domain Pattern
```json
Request: {
  "tool": "search_emails",
  "query": "emails from john@acme.com"
}
→ Extract domain: "acme.com"
→ Lookup account with domain "acme.com"
→ Router decision: "acme-work"
→ Strategy: "domain"
```

### Example 3: LLM Classification
```json
Request: {
  "tool": "get_calendar_events",
  "query": "What's on my work calendar tomorrow?"
}
→ No explicit account
→ No clear domain pattern
→ LLM analyzes: "work calendar" → work-related
→ Router decision: "work-account" (work account)
→ Strategy: "llm"
→ Confidence: 0.92
```

### Example 4: Multi-Account Aggregation
```json
Request: {
  "tool": "get_emails",
  "query": "Show me all my unread emails"
}
→ No explicit account
→ "all" implies aggregation
→ Router decision: ["work-account", "tenant2-account", "personal-gmail"]
→ Strategy: "multi-account"
```

### Example 5: Write Operation Disambiguation
```json
Request: {
  "tool": "send_email",
  "to": "colleague@company.com",
  "body": "Meeting notes..."
}
→ Write operation requires single account
→ Extract domain: "company.com"
→ Router decision: "company-work"
→ Strategy: "domain"
```

## Backend Configuration

See [Configuration](configuration.md#router-configuration) for detailed configuration examples.

### Ollama Local
```json
{
  "router": {
    "backend": "ollama",
    "model": "phi3.5:3.8b",
    "endpoint": "http://localhost:11434",
    "temperature": 0.1
  }
}
```

### OpenAI Cloud
```json
{
  "router": {
    "backend": "openai",
    "model": "gpt-4o-mini",
    "apiKey": "sk-...",
    "temperature": 0.1
  }
}
```

### Azure OpenAI
```json
{
  "router": {
    "backend": "azure-openai",
    "endpoint": "https://your-resource.openai.azure.com/",
    "deploymentName": "gpt-4o-mini",
    "apiKey": "...",
    "temperature": 0.1
  }
}
```

## Performance Considerations

### Caching
- Cache routing decisions for repeated queries
- TTL-based cache invalidation
- Cache key: hash of (query + context + account_list)

### Fallback Strategy
- If LLM backend unavailable: Use domain matching + default account
- If domain matching fails: Use default account from config
- Always prefer working over waiting for slow LLM

### Latency Targets
- Domain pattern matching: <10ms
- Local Ollama: <100ms (Phi-3.5-mini)
- Cloud APIs: <500ms
- Cache hit: <5ms

## Telemetry

Router emits comprehensive telemetry for analysis:

```csharp
using var activity = ActivitySource.StartActivity("SmartRouter.Route");
activity?.SetTag("tool.name", request.ToolName);
activity?.SetTag("routing.strategy", decision.RoutingStrategy);
activity?.SetTag("routing.confidence", decision.Confidence);
activity?.SetTag("routing.account_count", decision.AccountIds.Count);
activity?.SetTag("routing.latency_ms", elapsed);
```

See [Telemetry](telemetry.md) for complete metrics.

## Future Enhancements

1. **Historical Learning**: Track successful routing decisions
2. **User Feedback**: Allow users to correct routing mistakes
3. **Pattern Recognition**: Learn user-specific routing patterns
4. **Time-Based Rules**: Route differently based on time of day
5. **Context Awareness**: Use previous conversation context
6. **Multi-Model Ensemble**: Combine multiple models for better accuracy
