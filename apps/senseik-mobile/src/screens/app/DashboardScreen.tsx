import React from "react";
import { View, Text, TouchableOpacity } from "react-native";
import { useAuthStore } from "@/store/authStore";

export default function DashboardScreen() {
  const { user, logout } = useAuthStore();

  return (
    <View style={{ flex: 1, padding: 20, justifyContent: "center" }}>
      <Text style={{ fontSize: 24, fontWeight: "bold", marginBottom: 20 }}>Dashboard</Text>
      {user && (
        <>
          <Text style={{ fontSize: 16, marginBottom: 10 }}>Welcome, {user.firstName} {user.lastName}</Text>
          <Text style={{ fontSize: 14, color: "#666", marginBottom: 20 }}>Email: {user.email}</Text>
        </>
      )}

      <TouchableOpacity
        style={{
          backgroundColor: "#ff3b30",
          padding: 15,
          borderRadius: 8,
          alignItems: "center"
        }}
        onPress={() => logout()}
      >
        <Text style={{ color: "white", fontSize: 16, fontWeight: "bold" }}>Logout</Text>
      </TouchableOpacity>
    </View>
  );
}
