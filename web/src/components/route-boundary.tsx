import * as React from "react";
import { useLocation } from "react-router";
import { useTranslation } from "react-i18next";
import { Button, Center, Loader, Stack, Text } from "@mantine/core";

/**
 * Route pages are code-split (see App.tsx), so each one arrives as its own chunk on first visit.
 * Two things have to be handled for that to be safe:
 *
 * 1. The chunk is in flight -> show a fallback instead of a blank frame.
 * 2. The chunk 404s -> after a deploy the hashed filenames in the loaded index.html no longer exist.
 *    React would keep the *previous* page on screen indefinitely, which reads as a frozen app, so the
 *    boundary reloads once to pick up the new index.html and only surfaces an error if that fails too.
 */

/** Set for the lifetime of the tab, so a chunk that is genuinely gone cannot cause a reload loop. */
const RELOAD_MARKER = "senseik.routeChunkReloaded";

function isChunkLoadError(error: unknown): boolean {
  const text = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
  return /dynamically imported module|Loading chunk|Importing a module script failed|error loading dynamic/i.test(text);
}

function alreadyReloaded(): boolean {
  try {
    return sessionStorage.getItem(RELOAD_MARKER) === "1";
  } catch {
    // Storage unavailable (private mode): treat as "already tried" so we never loop.
    return true;
  }
}

function markReloaded(): void {
  try {
    sessionStorage.setItem(RELOAD_MARKER, "1");
  } catch {
    // Ignored: the check above fails closed when storage is unavailable.
  }
}

function RouteFallback() {
  const { t } = useTranslation(["common"]);
  return (
    <Center mih={240} p="xl">
      <Stack align="center" gap="xs">
        <Loader size="sm" />
        <Text size="sm" c="dimmed">
          {t("common:loading")}
        </Text>
      </Stack>
    </Center>
  );
}

function RouteLoadError() {
  const { t } = useTranslation(["common"]);
  return (
    <Center mih={240} p="xl">
      <Stack align="center" gap="sm">
        <Text size="sm" c="dimmed">
          {t("common:errorGeneric")}
        </Text>
        <Button variant="default" onClick={() => window.location.reload()}>
          {t("common:reload")}
        </Button>
      </Stack>
    </Center>
  );
}

interface BoundaryProps {
  children: React.ReactNode;
  /** Changes on every navigation; clearing the error here keeps one bad page from poisoning the rest. */
  resetKey: string;
}

class RouteErrorBoundary extends React.Component<BoundaryProps, { failed: boolean; shownFor: string }> {
  state = { failed: false, shownFor: this.props.resetKey };

  // Without this the boundary latches: the first page that throws would leave every later route
  // showing the error screen until a manual reload, which looks exactly like a frozen app.
  static getDerivedStateFromProps(props: BoundaryProps, state: { failed: boolean; shownFor: string }) {
    return props.resetKey === state.shownFor ? null : { failed: false, shownFor: props.resetKey };
  }

  static getDerivedStateFromError(error: unknown) {
    if (isChunkLoadError(error) && !alreadyReloaded()) {
      markReloaded();
      window.location.reload();
    }
    return { failed: true };
  }

  render() {
    return this.state.failed ? <RouteLoadError /> : this.props.children;
  }
}

/** Wraps the routed area: suspends on the page chunk and recovers from a stale or broken one. */
export default function RouteBoundary({ children }: { children: React.ReactNode }) {
  const { pathname } = useLocation();
  return (
    <RouteErrorBoundary resetKey={pathname}>
      <React.Suspense fallback={<RouteFallback />}>{children}</React.Suspense>
    </RouteErrorBoundary>
  );
}
