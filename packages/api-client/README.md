# @senseik/api-client

TypeScript API client for SenseİK backend, auto-generated from OpenAPI specification.

## Usage

```typescript
import { apiClient } from "@senseik/api-client";

// Make API calls
const response = await apiClient.get("/companies");
const company = await apiClient.post("/companies", { name: "Acme Inc" });
```

## Generation

Generate or regenerate the API client from the backend's OpenAPI spec:

```bash
# From the root of the repository
pnpm generate:api-client
```

Or with a custom API URL:

```bash
pnpm generate:api-client http://your-api.com/swagger/v1/swagger.json
```

## Structure

```
src/
├── index.ts           # Main entry point with axios instance
└── generated/         # Auto-generated client code (do not edit)

scripts/
└── generate-from-openapi.mjs  # Generation script
```

## Configuration

The API client uses environment variables from the web app:

- `VITE_API_BASE_URL` - Backend API base URL (e.g., `http://localhost:5000/api`)

Authentication tokens are automatically included via axios interceptor. Token is read from `localStorage.auth_token`.

## Development

1. **Generate client** from running backend
2. **Commit** generated code to repository
3. **Import and use** in web app components/services

## Notes

- Generated code is in `src/generated/` and should not be edited manually
- API client handles authentication, error responses, and retries via axios interceptors
- Types are fully auto-generated from backend OpenAPI specification
- The client is published as a workspace package for use in the web app

---

See [web app README](../../web/README.md) for full setup instructions.
