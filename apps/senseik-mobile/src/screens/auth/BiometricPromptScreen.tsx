import React, { useEffect } from "react";
import { View, Text, TouchableOpacity, Alert, ActivityIndicator } from "react-native";
import { NativeStackScreenProps } from "@react-navigation/native-stack";
import { AuthStackParamList } from "@/types/navigation";
import { useAuthStore } from "@/store/authStore";

type Props = NativeStackScreenProps<AuthStackParamList, "BiometricPrompt">;

export default function BiometricPromptScreen({ navigation, route }: Props) {
  const { loginWithBiometric } = useAuthStore();
  const { email } = route.params;

  useEffect(() => {
    const attemptBiometric = async () => {
      try {
        await loginWithBiometric(email);
      } catch (error) {
        const message = error instanceof Error ? error.message : "Lütfen tekrar deneyin";
        Alert.alert("Biyometrik giriş başarısız", message);
        navigation.goBack();
      }
    };

    attemptBiometric();
  }, [email, loginWithBiometric, navigation]);

  return (
    <View style={{ flex: 1, padding: 20, justifyContent: "center", alignItems: "center" }}>
      <ActivityIndicator size="large" color="#0066cc" />
      <Text style={{ marginTop: 20, fontSize: 16 }}>Kimlik doğrulanıyor...</Text>

      <TouchableOpacity
        style={{
          marginTop: 30,
          backgroundColor: "#999",
          padding: 15,
          borderRadius: 8,
          minWidth: 150,
          alignItems: "center"
        }}
        onPress={() => navigation.goBack()}
      >
        <Text style={{ color: "white", fontWeight: "bold" }}>Vazgeç</Text>
      </TouchableOpacity>
    </View>
  );
}
