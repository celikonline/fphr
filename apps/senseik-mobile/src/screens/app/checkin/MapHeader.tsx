import React from "react";
import { StyleSheet, Text, View, useWindowDimensions } from "react-native";
import MapView, { Circle, Marker } from "react-native-maps";
import { Ionicons } from "@expo/vector-icons";
import { GlassCard } from "@/components/ui";
import { palette, radius, text, tones } from "@/theme/theme";

export type Coordinates = { latitude: number; longitude: number };

/** Spec §6: the map takes about a third of the screen height. */
const MAP_HEIGHT_RATIO = 0.35;
/** Radius of the location circle drawn around the user's position. */
const CIRCLE_METERS = 60;
/** ~400 m visible span - enough to read the street around the circle. */
const MAP_DELTA = 0.004;

type Props = {
  /** `shouldRenderMap(...)`: false on Android without a Google Maps key (the SDK would crash). */
  showMap: boolean;
  position: Coordinates | null;
  /** Location name from the last punch, the coordinates, or a status such as "Konum alınıyor…". */
  placeLabel: string;
};

/** Map with the user's position and a 60 m circle, or a glass "Konumun" card when no map can render. */
export function MapHeader({ showMap, position, placeLabel }: Props) {
  const { height } = useWindowDimensions();

  if (showMap && position) {
    const region = { ...position, latitudeDelta: MAP_DELTA, longitudeDelta: MAP_DELTA };
    return (
      <View
        style={[styles.mapFrame, { height: Math.round(height * MAP_HEIGHT_RATIO) }]}
        accessible
        accessibilityRole="image"
        accessibilityLabel={`Harita, konumun: ${placeLabel}`}
      >
        <MapView
          style={StyleSheet.absoluteFill}
          region={region}
          userInterfaceStyle="dark"
          scrollEnabled={false}
          zoomEnabled={false}
          pitchEnabled={false}
          rotateEnabled={false}
          toolbarEnabled={false}
        >
          <Circle
            center={position}
            radius={CIRCLE_METERS}
            strokeColor={palette.brand}
            fillColor={tones.info.bg}
          />
          <Marker coordinate={position} pinColor={palette.brand} />
        </MapView>
      </View>
    );
  }

  return (
    <GlassCard style={styles.card}>
      <View style={styles.row} accessible accessibilityLabel={`Konumun: ${placeLabel}`}>
        <View style={styles.iconWrap}>
          <Ionicons name="location-outline" size={22} color={palette.brandText} />
        </View>
        <View style={styles.textWrap}>
          <Text style={styles.caption}>Konumun</Text>
          <Text style={styles.place} numberOfLines={2}>
            {placeLabel}
          </Text>
        </View>
      </View>
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  mapFrame: {
    marginHorizontal: 20,
    marginTop: 8,
    borderRadius: radius.card,
    overflow: "hidden",
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  card: {
    marginHorizontal: 20,
    marginTop: 8,
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
  },
  iconWrap: {
    width: 44,
    height: 44,
    borderRadius: radius.button,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: tones.info.bg,
  },
  textWrap: {
    flex: 1,
    minWidth: 0,
  },
  caption: {
    ...text.caption,
  },
  place: {
    ...text.heading,
    marginTop: 2,
  },
});
