# ADR 0006 — LLM sağlayıcı soyutlaması ve MCP

**Durum:** Kabul edildi — 2026-09-14

## Karar
- `Sense.Modules.Ai.Application` içinde `ILlmProvider` (chat/stream/tool-call/structured) ve `IEmbeddingProvider` portları; Infrastructure'da `AnthropicLlmProvider` (NuGet `Anthropic`, varsayılan model `claude-opus-5`, adaptive thinking, streaming, prompt caching, `fallbacks`).
- Chatbot araçları, MCP sunucusu araçları ve Anthropic `mcp_toolset` **tek katalogdan** (`IAiToolCatalog`) üretilir; her araç bir Application use case'ine delege eder ve çağıran kullanıcının izin/kapsamı aynen uygulanır. Yazma araçları kullanıcı onayı ister.
- MCP sunucusu: resmi `ModelContextProtocol` C# SDK, Streamable HTTP `/mcp`, Bearer `McpApiToken`.
- MCP istemcisi: tenant tanımlı dış sunucular (`McpClient`) + bulut sunucular için Anthropic MCP connector.
