import React, { useEffect, useState } from "react";
import { Image, StyleSheet, Text, View } from "react-native";
import { apiClient } from "@/lib/apiClient";
import { SecureStorage } from "@/lib/storage";
import { accents, fontFor, palette } from "@/theme/theme";
import { accentForName, initialsOf } from "./avatarUtils";

type Props = {
  name: string;
  employeeId?: string;
  self?: boolean;
  size?: number;
  radius?: number;
};

/**
 * Employee photo when one can be fetched (`employeeId` or `self`), initials otherwise. The access
 * token is only ever read (never cached at module scope, `SecureStorage.getAccessToken` has no
 * synchronous getter) while this instance is mounted, and dropped again on unmount/prop change.
 */
export function Avatar({ name, employeeId, self = false, size = 58, radius = 16 }: Props) {
  const canLoadPhoto = self || Boolean(employeeId);
  const [token, setToken] = useState<string | null>(null);
  const [imageFailed, setImageFailed] = useState(false);

  useEffect(() => {
    setImageFailed(false);
    if (!canLoadPhoto) {
      setToken(null);
      return;
    }
    let cancelled = false;
    void SecureStorage.getAccessToken().then((value) => {
      if (!cancelled) setToken(value);
    });
    return () => {
      cancelled = true;
    };
  }, [canLoadPhoto, self, employeeId]);

  const showImage = canLoadPhoto && Boolean(token) && !imageFailed;

  if (showImage) {
    const path = self ? "/api/v1/employees/me/photo" : `/api/v1/employees/${employeeId}/photo`;
    return (
      <Image
        accessibilityLabel={name}
        source={{
          uri: `${apiClient.getBaseURL()}${path}`,
          headers: { Authorization: `Bearer ${token}` },
        }}
        style={[styles.image, { width: size, height: size, borderRadius: radius }]}
        onError={() => setImageFailed(true)}
      />
    );
  }

  const accent = accents[accentForName(name)];
  return (
    <View
      accessibilityLabel={name}
      style={[
        styles.initials,
        { width: size, height: size, borderRadius: radius, backgroundColor: accent.bg },
      ]}
    >
      <Text style={[styles.initialsText, { color: accent.fg }]}>{initialsOf(name)}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  image: {
    // Equivalent to the `glass` surface used everywhere else while the photo loads.
    backgroundColor: palette.glass,
  },
  initials: {
    alignItems: "center",
    justifyContent: "center",
  },
  initialsText: {
    fontSize: 18,
    ...fontFor(800),
  },
});
