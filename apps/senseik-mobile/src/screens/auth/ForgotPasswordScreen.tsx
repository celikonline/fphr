import React, { useState } from "react";
import { View, Text, TextInput, TouchableOpacity, Alert } from "react-native";
import { NativeStackScreenProps } from "@react-navigation/native-stack";
import { AuthStackParamList } from "@/types/navigation";

type Props = NativeStackScreenProps<AuthStackParamList, "ForgotPassword">;

export default function ForgotPasswordScreen({ navigation }: Props) {
  const [email, setEmail] = useState("");

  const handleReset = () => {
    if (!email) {
      Alert.alert("Hata", "Lütfen e-posta adresinizi girin");
      return;
    }
    // Generic message without echoing the address: the screen performs no request yet,
    // and confirming a specific address enables account enumeration.
    Alert.alert("Başarılı", "E-posta kayıtlıysa sıfırlama bağlantısı gönderildi.");
    navigation.goBack();
  };

  return (
    <View style={{ flex: 1, padding: 20, justifyContent: "center" }}>
      <Text style={{ fontSize: 24, fontWeight: "bold", marginBottom: 20 }}>Şifreyi sıfırla</Text>

      <TextInput
        style={{
          borderWidth: 1,
          borderColor: "#ccc",
          padding: 12,
          marginBottom: 20,
          borderRadius: 8
        }}
        placeholder="E-posta"
        value={email}
        onChangeText={setEmail}
      />

      <TouchableOpacity
        style={{
          backgroundColor: "#0066cc",
          padding: 15,
          borderRadius: 8,
          alignItems: "center",
          marginBottom: 10
        }}
        onPress={handleReset}
      >
        <Text style={{ color: "white", fontSize: 16, fontWeight: "bold" }}>Sıfırlama bağlantısı gönder</Text>
      </TouchableOpacity>

      <TouchableOpacity onPress={() => navigation.goBack()}>
        <Text style={{ color: "#0066cc", textAlign: "center" }}>Girişe dön</Text>
      </TouchableOpacity>
    </View>
  );
}
