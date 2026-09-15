# SenseİK Web Frontend

Modern React 18 web frontend for the SenseİK HR/Payroll SaaS platform.

## Technology Stack

- **Framework:** React 18 with TypeScript
- **Build Tool:** Vite (fast bundler & dev server)
- **UI Components:** Mantine 9 (`@mantine/core`, `dates`, `notifications`, `dropzone`, `charts`)
- **Styling:** Mantine props for components, Tailwind CSS v4 for layout utilities
- **Routing:** React Router v6
- **State Management:** Zustand
- **Internationalization:** i18next + react-i18next (Turkish/English)
- **Dark Mode:** Mantine color scheme (light/dark/system from the UI store)
- **PWA:** vite-plugin-pwa
- **Code Quality:** ESLint + Prettier + TypeScript strict mode
- **Testing:** Vitest + React Testing Library (configured, optional)

## Quick Start

### Prerequisites

- Node.js 20.x or higher
- pnpm 9.0+ (package manager)
- Docker Desktop with backend running (see main README)

### Setup

```bash
# Clone and navigate to repository root
cd /path/to/senseik

# Install dependencies (runs for all workspaces)
pnpm install

# Create .env.local from .env.example
cp web/.env.local.example web/.env.local

# Start development server
pnpm dev

# In another terminal, start the backend (if not already running)
dotnet watch --project src/Sense.Api
```

The app will be available at `http://localhost:3000` (or next available port if 3000 is taken).

## Project Structure

```
src/
├── components/           # Reusable React components
│   ├── ui/              # App composites on Mantine (data table, date/time pickers, file dropzone)
│   └── protected-route  # Auth wrapper
├── pages/               # Page components (screens)
│   ├── auth/
│   │   └── login.tsx
│   ├── dashboard.tsx
│   └── not-found.tsx
├── layouts/             # Common layouts (Item 20)
├── hooks/               # Custom React hooks
├── lib/                 # Utility functions
│   └── mantine-theme.ts # Mantine theme (colors, fonts, radius)
├── services/            # API calls (using @senseik/api-client)
├── store/              # Zustand stores
│   ├── auth.store.ts   # Authentication state
│   └── ui.store.ts     # Theme, sidebar, notifications
├── types/              # TypeScript interfaces/types
├── styles/             # Global CSS
│   └── index.css       # Mantine styles + Tailwind (layout utilities) with CSS layer order
├── i18n.ts             # i18next configuration
├── App.tsx             # Root component with routing
└── main.tsx            # Entry point

public/
├── locales/            # i18n translation files
│   ├── tr/
│   │   ├── common.json # Turkish common strings
│   │   └── auth.json   # Turkish auth strings
│   └── en/
│       ├── common.json # English common strings
│       └── auth.json   # English auth strings
├── pwa-*.png           # PWA icons
└── manifest.json       # PWA manifest (auto-generated)
```

## Key Files & Configuration

| File | Purpose |
|------|---------|
| `vite.config.ts` | Vite bundler configuration, API proxy, PWA plugin |
| `src/lib/mantine-theme.ts` | Mantine theme: colors, fonts, radius, dark palette |
| `postcss.config.cjs` | PostCSS: Mantine preset (`light-dark()`, `rem()`, mixins) + Tailwind |
| `tsconfig.json` | TypeScript compiler options, path aliases |
| `eslint.config.js` | Code linting rules |
| `.prettierrc.json` | Code formatting rules |
| `package.json` | Dependencies, scripts |
| `.env.example` | Environment variable template |

## Development

### Start Dev Server

```bash
pnpm dev
```

Vite serves your app at `http://localhost:3000` with hot module replacement (HMR).

### Build for Production

```bash
pnpm build
```

Optimized bundle is output to `dist/`.

### Type Check

```bash
pnpm type-check
```

Runs TypeScript compiler without emitting files (catches type errors).

### Lint & Format

```bash
# Lint files
pnpm lint

# Fix formatting
pnpm format
```

## Environment Variables

Create `.env.local` in the `web/` directory:

```env
# API Configuration
VITE_API_BASE_URL=http://localhost:5000/api

# App Configuration
VITE_APP_NAME=SenseİK
VITE_APP_VERSION=0.1.0

# Feature Flags
VITE_ENABLE_PWA=true
VITE_ENABLE_ANALYTICS=false

# i18n
VITE_DEFAULT_LANGUAGE=tr
```

All `VITE_*` variables are injected at build time and accessible via `import.meta.env`.

## Authentication

Authentication state is managed via the `useAuthStore` hook (Zustand):

```typescript
import { useAuthStore } from "@/store/auth.store";

// In a component:
const user = useAuthStore((state) => state.user);
const logout = useAuthStore((state) => state.logout);
const isAuthenticated = useAuthStore((state) => state.isAuthenticated());
```

Protected routes wrap components with `<ProtectedRoute>`, which redirects unauthenticated users to `/login`.

## Dark Mode

Dark mode toggle is stored in `useUIStore`:

```typescript
import { useUIStore } from "@/store/ui.store";

const theme = useUIStore((state) => state.theme); // "light" | "dark" | "system"
const setTheme = useUIStore((state) => state.setTheme);
```

The App component automatically applies the `dark` class to `<html>` based on the theme setting.

## Internationalization (i18n)

Translations are organized in `public/locales/{language}/{namespace}.json`:

```
public/locales/
├── tr/
│   ├── common.json
│   └── auth.json
└── en/
    ├── common.json
    └── auth.json
```

Use translations in components:

```typescript
import { useTranslation } from "react-i18next";

export default function MyComponent() {
  const { t } = useTranslation(["common", "auth"]);
  
  return <p>{t("common:welcome")}</p>;
}
```

The language preference is saved to localStorage and detected on app load.

## API Client Generation

The API client is generated from the backend's OpenAPI specification:

```bash
# After backend is running at http://localhost:5000
pnpm generate:api-client
```

This generates TypeScript types and client methods in `packages/api-client/src/generated/`.

Generated clients are imported and used in service files:

```typescript
// src/services/company.service.ts
import { apiClient } from "@senseik/api-client";

export async function getCompanies() {
  return apiClient.get("/companies");
}
```

## Mantine & Tailwind

Components come from Mantine; Tailwind is used for layout (`flex`, `grid`, `gap-*`, `p-*`). Rules and the component mapping are in `docs/implementation/mantine-gecis.md`.

```typescript
import { Button, Card, Text, Title } from "@mantine/core";

export function MyCard() {
  return (
    <Card withBorder>
      <Title order={4}>Title</Title>
      <Text c="dimmed" size="sm">Description</Text>
      <Button mt="md">Click me</Button>
    </Card>
  );
}
```

App-level composites stay in `src/components/ui/`: `useDataTable`/`DataTable`, `DatePicker` ("yyyy-MM-dd"), `TimePicker` ("HH:mm") and `FileDropzone`. Notifications go through `toast()` from `@/hooks/use-toast`.

### Theme & Dark Mode

`src/components/mantine-root.tsx` wires `MantineProvider` (theme from `src/lib/mantine-theme.ts`), `DatesProvider` and `Notifications`. The color scheme follows the UI store (light/dark/system) and Mantine sets `data-mantine-color-scheme` on `<html>`. There are no app CSS tokens or `dark:` classes: use Mantine props and variables (`var(--mantine-color-body)`, `var(--mantine-color-default-border)`, `var(--mantine-color-dimmed)`), or a colocated `*.module.css` with `light-dark()` for custom surfaces (see `layouts/app-layout.module.css`).

## State Management (Zustand)

Stores are in `src/store/`. Example creating a new store:

```typescript
import { create } from "zustand";
import { persist } from "zustand/middleware";

interface MyStore {
  count: number;
  increment: () => void;
}

export const useMyStore = create<MyStore>()(
  persist(
    (set) => ({
      count: 0,
      increment: () => set((state) => ({ count: state.count + 1 })),
    }),
    { name: "my-store" } // Persisted to localStorage
  )
);
```

## PWA Support

The app is configured as a Progressive Web App:

1. **Service Worker:** Automatically handled by `vite-plugin-pwa`
2. **Manifest:** Generated from config in `vite.config.ts`
3. **Icons:** Add PWA icons to `public/` (192x192 and 512x512)

Users can install the app on mobile or desktop. For development, enable DevTools → Application → Service Workers to inspect.

## Component Development Pattern

When building new components (Item 20):

1. **Functional Components:** Use React hooks (prefer composition over class components)
2. **Props:** Typed with TypeScript interfaces
3. **Styling:** Mantine component props; Tailwind classes only for layout
4. **Accessibility:** Mantine components (labels via `label`/`error` props, `aria-label` on `ActionIcon`s)
5. **Internationalization:** Use `useTranslation()` for user-facing text
6. **State:** Zustand for global, React hooks for local state

Example:

```typescript
import { useTranslation } from "react-i18next";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

interface MyComponentProps {
  title: string;
  isActive?: boolean;
}

export function MyComponent({ title, isActive = false }: MyComponentProps) {
  const { t } = useTranslation();

  return (
    <div className={cn("p-4 rounded-lg", isActive && "bg-blue-100")}>
      <h3 className="font-semibold">{title}</h3>
      <Button>{t("common:save")}</Button>
    </div>
  );
}
```

## Testing

Tests are optional for MVP but configured with Vitest + React Testing Library:

```bash
pnpm test
```

Example test:

```typescript
// src/components/button.test.tsx
import { render, screen } from "@testing-library/react";
import { Button } from "@/components/ui/button";

describe("Button", () => {
  it("renders", () => {
    render(<Button>Click me</Button>);
    expect(screen.getByText("Click me")).toBeInTheDocument();
  });
});
```

## Troubleshooting

### Port Already in Use

If `localhost:3000` is in use, Vite will try the next available port. Check the terminal output.

### Module Resolution Errors

Ensure path aliases in `tsconfig.json` match files in `src/`. Example error → check that `@/components` resolves to `src/components`.

### TypeScript Errors

Run `pnpm type-check` to see all errors. Most are import/type issues or missing `tsconfig.json` paths.

### API Connection Refused

Ensure backend is running (`dotnet watch --project src/Sense.Api`) and `VITE_API_BASE_URL` in `.env.local` is correct.

### i18n Translations Missing

Check that JSON files exist in `public/locales/{lang}/{namespace}.json` and namespace is included in `useTranslation(['namespace'])`.

## Next Steps

- **Item 20:** Implement 6 main screens (company profile, organization, locations, positions, users/roles, audit log)
- **Item 21:** Build mobile app (Expo)
- **Item 22:** CI/CD pipeline
- **Item 23:** Observability (logging, monitoring, tracing)

## Resources

- [React Documentation](https://react.dev)
- [Vite Documentation](https://vitejs.dev)
- [Tailwind CSS Documentation](https://tailwindcss.com)
- [Mantine Documentation](https://mantine.dev)
- [Zustand Documentation](https://github.com/pmndrs/zustand)
- [i18next Documentation](https://www.i18next.com)
- [React Router Documentation](https://reactrouter.com)

---

**Questions?** Check the main repository README or ADRs in `docs/adr/`.
