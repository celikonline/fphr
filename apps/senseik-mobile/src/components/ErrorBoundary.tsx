import React, { Component, type ReactNode } from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { fontFor, palette, radius, text } from "@/theme/theme";

type Props = {
  children?: ReactNode;
};

type State = {
  hasError: boolean;
  resetKey: number;
};

/**
 * App-wide render error boundary. Wraps `<RootNavigator />` in `App.tsx` so an unexpected render
 * error in any one screen shows a "yeniden dene" fallback instead of unmounting the whole tree
 * (blank/frozen screen, or a native crash in production).
 *
 * "Yeniden dene" bumps `resetKey`, which re-keys the wrapped subtree so React fully remounts it
 * (a plain `setState({ hasError: false })` would just re-render the same failed instance).
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false, resetKey: 0 };

  static getDerivedStateFromError(): Partial<State> {
    return { hasError: true };
  }

  componentDidCatch(): void {
    // Never log the raw error/stack (may carry PII from screen state) - app-wide rule, see
    // AuthService.ts. Only a fixed, generic message, and only in dev.
    if (__DEV__) {
      console.error("Uygulama beklenmeyen bir render hatasıyla karşılaştı.");
    }
  }

  handleRetry = (): void => {
    this.setState((previous) => ({ hasError: false, resetKey: previous.resetKey + 1 }));
  };

  render(): ReactNode {
    if (this.state.hasError) {
      return <ErrorFallback onRetry={this.handleRetry} />;
    }
    return <React.Fragment key={this.state.resetKey}>{this.props.children}</React.Fragment>;
  }
}

type FallbackProps = {
  onRetry: () => void;
};

function ErrorFallback({ onRetry }: FallbackProps) {
  return (
    <View style={styles.container}>
      <Text style={styles.title}>Bir şeyler ters gitti</Text>
      <Text style={styles.description}>Beklenmeyen bir hata oluştu. Yeniden deneyebilirsiniz.</Text>
      <TouchableOpacity
        style={styles.button}
        onPress={onRetry}
        accessibilityRole="button"
        accessibilityLabel="Yeniden dene"
      >
        <Text style={styles.buttonLabel}>Yeniden dene</Text>
      </TouchableOpacity>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: palette.night,
    paddingHorizontal: 32,
    gap: 12,
  },
  title: {
    ...text.title,
    textAlign: "center",
  },
  description: {
    ...text.bodyMuted,
    textAlign: "center",
  },
  button: {
    marginTop: 12,
    minHeight: 48,
    paddingHorizontal: 24,
    justifyContent: "center",
    alignItems: "center",
    borderRadius: radius.button,
    backgroundColor: palette.brand,
  },
  buttonLabel: {
    ...text.button,
    ...fontFor(800),
  },
});
