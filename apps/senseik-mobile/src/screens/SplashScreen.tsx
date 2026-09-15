import React from "react";
import { View, Text, ActivityIndicator } from "react-native";

export default function SplashScreen() {
  return (
    <View style={{ flex: 1, justifyContent: "center", alignItems: "center" }}>
      <Text style={{ fontSize: 24, marginBottom: 20 }}>SenseİK Mobile</Text>
      <ActivityIndicator size="large" color="#0066cc" />
    </View>
  );
}
