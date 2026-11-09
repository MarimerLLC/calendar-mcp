# Recommended Local Models for Routing

## Overview

For users who prefer local inference with Ollama, this guide recommends models optimized for smart routing decisions.

## Why Local Models?

**Privacy**:
- ✅ Data never leaves your machine
- ✅ No cloud provider sees your account metadata
- ✅ Complete control over data

**Cost**:
- ✅ No per-request charges
- ✅ No API key required
- ✅ Unlimited usage

**Latency**:
- ✅ No network round-trip
- ✅ Faster routing decisions (on capable hardware)

**Trade-offs**:
- ⚠️ Requires Ollama installation and setup
- ⚠️ Needs local compute resources
- ⚠️ May be slower than cloud on weaker hardware

## Recommended Models

### Phi-3.5-mini (3.8B) - **Recommended Default**

**Why Recommended**:
- Excellent classification accuracy
- Strong reasoning capabilities
- Fast inference on modest hardware
- Good balance of size vs. performance

**Specifications**:
- **Parameters**: 3.8B
- **Context Length**: 128K tokens
- **Quantized Size**: ~2.4GB
- **Inference Speed**: 12+ tokens/sec on modest hardware
- **Use Case**: Default choice for most users

**Performance**:
- ✅ Routing accuracy: ~95%
- ✅ Average latency: 50-100ms
- ✅ Handles complex multi-account scenarios

**Installation**:
```bash
ollama pull phi3.5:3.8b
```

**Configuration**:
```json
{
  "router": {
    "backend": "ollama",
    "model": "phi3.5:3.8b",
    "endpoint": "http://localhost:11434",
    "temperature": 0.1,
    "maxTokens": 500
  }
}
```

### Qwen2-7B - **Best for Complex Decisions**

**Why Recommended**:
- Excellent structured data understanding
- Strong logic-based reasoning
- Good for complex routing scenarios
- Handles ambiguous cases well

**Specifications**:
- **Parameters**: 7B
- **Context Length**: 128K tokens
- **Quantized Size**: ~4.7GB
- **Inference Speed**: 8-10 tokens/sec on modest hardware
- **Use Case**: Complex multi-tenant environments with many accounts

**Performance**:
- ✅ Routing accuracy: ~97%
- ⚠️ Average latency: 80-150ms
- ✅ Best for ambiguous queries

**Installation**:
```bash
ollama pull qwen2:7b
```

**Configuration**:
```json
{
  "router": {
    "backend": "ollama",
    "model": "qwen2:7b",
    "endpoint": "http://localhost:11434",
    "temperature": 0.1,
    "maxTokens": 500
  }
}
```

### Qwen2-1.5B - **Ultra Lightweight**

**Why Recommended**:
- Blazing fast routing
- Minimal resource usage
- Good for simpler classification tasks
- Works on low-power devices

**Specifications**:
- **Parameters**: 1.5B
- **Context Length**: 128K tokens
- **Quantized Size**: ~1GB
- **Inference Speed**: 20+ tokens/sec on modest hardware
- **Use Case**: Simpler routing scenarios, resource-constrained environments

**Performance**:
- ⚠️ Routing accuracy: ~90%
- ✅ Average latency: 30-50ms
- ✅ Lowest resource usage

**Installation**:
```bash
ollama pull qwen2:1.5b
```

**Configuration**:
```json
{
  "router": {
    "backend": "ollama",
    "model": "qwen2:1.5b",
    "endpoint": "http://localhost:11434",
    "temperature": 0.1,
    "maxTokens": 500
  }
}
```

## Model Comparison

| Model | Size | Accuracy | Latency | Use Case |
|-------|------|----------|---------|----------|
| **Phi-3.5-mini** | 2.4GB | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | **Default choice** |
| Qwen2-7B | 4.7GB | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | Complex routing |
| Qwen2-1.5B | 1GB | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | Fast & lightweight |

## Hardware Requirements

### Minimum (Qwen2-1.5B)
- **RAM**: 4GB
- **CPU**: Modern multi-core processor
- **Inference**: ~30-50ms

### Recommended (Phi-3.5-mini)
- **RAM**: 8GB
- **CPU**: Modern multi-core processor (4+ cores)
- **Inference**: ~50-100ms
- **GPU**: Optional, but helps with speed

### Optimal (Qwen2-7B)
- **RAM**: 16GB
- **CPU**: Modern multi-core processor (6+ cores)
- **GPU**: NVIDIA/AMD with 8GB+ VRAM (significant speedup)
- **Inference**: ~80-150ms (CPU), ~20-40ms (GPU)

## Ollama Setup

### Installation

**macOS**:
```bash
brew install ollama
ollama serve
```

**Linux**:
```bash
curl https://ollama.ai/install.sh | sh
ollama serve
```

**Windows**:
1. Download from https://ollama.ai/download
2. Run installer
3. Ollama runs as service automatically

### Verify Installation

```bash
ollama list
# Should show installed models

ollama run phi3.5:3.8b
# Test interactive chat
```

### Pull Models

```bash
# Recommended default
ollama pull phi3.5:3.8b

# Alternative options
ollama pull qwen2:7b
ollama pull qwen2:1.5b
```

## Testing Router Performance

### Benchmark Script

```bash
# Test routing decision latency
adjutant-setup benchmark-router

# Output:
# Testing router with 100 sample queries...
# 
# Model: phi3.5:3.8b
# Average latency: 67ms
# P50: 62ms, P95: 89ms, P99: 124ms
# Accuracy: 96% (based on test dataset)
```

### Manual Testing

```bash
# Send test query to router
curl -X POST http://localhost:11434/api/generate \
  -d '{
    "model": "phi3.5:3.8b",
    "prompt": "Route this request: Send email to john@acme.com",
    "stream": false
  }'
```

## Optimization Tips

### Temperature Setting

**Recommended**: `0.1` (low)
- Routing is a deterministic task
- Low temperature = consistent decisions
- High temperature = unpredictable routing

```json
{
  "router": {
    "temperature": 0.1  // Recommended for routing
  }
}
```

### Max Tokens

**Recommended**: `500` tokens
- Routing decisions are short
- Prevents unnecessary token generation
- Reduces latency

```json
{
  "router": {
    "maxTokens": 500  // Plenty for routing JSON response
  }
}
```

### Caching

**Enable caching** to skip repeated routing decisions:

```json
{
  "router": {
    "caching": {
      "enabled": true,
      "ttlMinutes": 30
    }
  }
}
```

**Cache key**: Hash of (query + context + account_list)

### GPU Acceleration

If you have a compatible GPU:

```bash
# Verify GPU support
ollama run phi3.5:3.8b --verbose

# Should show: "using GPU: NVIDIA GeForce RTX..."
```

**Performance impact**:
- CPU: ~80ms average latency
- GPU: ~20ms average latency (4x faster)

## When to Use Cloud APIs Instead

Consider OpenAI/Anthropic if:

1. **No local hardware**: Running on low-power device
2. **Better accuracy needed**: Cloud models (GPT-4o) have higher accuracy
3. **Simplicity**: Don't want to manage Ollama
4. **Cost not a concern**: Per-request charges acceptable

See [Routing](routing.md#llm-backend-options) for cloud configuration.

## Troubleshooting

### "Connection refused" error

**Cause**: Ollama not running

**Solution**:
```bash
ollama serve
```

### Slow inference (>500ms)

**Causes**:
- Model not downloaded (streaming download during first use)
- Insufficient RAM
- CPU throttling

**Solutions**:
```bash
# Pre-pull model
ollama pull phi3.5:3.8b

# Check system resources
htop  # Linux/macOS
Task Manager  # Windows

# Try smaller model
ollama pull qwen2:1.5b
```

### High RAM usage

**Cause**: Large model loaded in memory

**Solution**: Use smaller model or increase swap
```bash
# Use lightweight model
ollama pull qwen2:1.5b

# Or add swap space (Linux)
sudo fallocate -l 8G /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
```

## Future Model Recommendations

As new models are released, consider:

1. **Llama 3.2 (3B)**: Similar to Phi-3.5-mini, potentially better accuracy
2. **Mistral 7B**: Strong reasoning, good for routing
3. **Gemma 2B**: Lightweight alternative to Qwen2-1.5B

Test new models with benchmark script before switching.
