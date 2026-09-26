import React, { useEffect, useState } from "react";
import { NavigationContainer } from "@react-navigation/native";
import { createNativeStackNavigator } from "@react-navigation/native-stack";
import { createBottomTabNavigator } from "@react-navigation/bottom-tabs";
import { ActivityIndicator, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useAuthStore } from "@/store/authStore";
import {
  RootStackParamList,
  AuthStackParamList,
  AppStackParamList,
  MainTabsParamList,
} from "@/types/navigation";

// Screens (will be created)
import LoginScreen from "@/screens/auth/LoginScreen";
import BiometricPromptScreen from "@/screens/auth/BiometricPromptScreen";
import ForgotPasswordScreen from "@/screens/auth/ForgotPasswordScreen";
import DashboardScreen from "@/screens/app/DashboardScreen";
import AttendanceScreen from "@/screens/app/AttendanceScreen";
import LeaveScreen from "@/screens/app/LeaveScreen";
import PayrollScreen from "@/screens/app/PayrollScreen";
import ApprovalsScreen from "@/screens/app/ApprovalsScreen";
import ProfileScreen from "@/screens/app/ProfileScreen";
import SupportScreen from "@/screens/app/SupportScreen";
import CheckInScreen from "@/screens/app/CheckInScreen";
import PeopleScreen from "@/screens/app/PeopleScreen";
import ShiftScreen from "@/screens/app/ShiftScreen";
import GoalsScreen from "@/screens/app/GoalsScreen";

const RootStack = createNativeStackNavigator<RootStackParamList>();
const AuthStack = createNativeStackNavigator<AuthStackParamList>();
const AppStack = createNativeStackNavigator<AppStackParamList>();
const Tab = createBottomTabNavigator<MainTabsParamList>();

function AuthNavigator() {
  return (
    <AuthStack.Navigator
      screenOptions={{
        headerShown: false,
      }}
    >
      <AuthStack.Screen name="Login" component={LoginScreen} />
      <AuthStack.Screen name="BiometricPrompt" component={BiometricPromptScreen} />
      <AuthStack.Screen name="ForgotPassword" component={ForgotPasswordScreen} />
    </AuthStack.Navigator>
  );
}

function MainTabsNavigator() {
  return (
    <Tab.Navigator
      screenOptions={{
        tabBarActiveTintColor: "#0066cc",
        tabBarInactiveTintColor: "#999999",
        // B3: each tab screen already renders its own in-page title (e.g. "Ana Sayfa") - a
        // navigator header on top of that showed the title twice ("Anasayfa" + "Ana Sayfa").
        headerShown: false,
      }}
    >
      <Tab.Screen
        name="Dashboard"
        component={DashboardScreen}
        options={{
          title: "Anasayfa",
          tabBarLabel: "Anasayfa",
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="home-outline" size={size} color={color} />
          ),
        }}
      />
      <Tab.Screen
        name="Attendance"
        component={AttendanceScreen}
        options={{
          title: "Puantaj",
          tabBarLabel: "Puantaj",
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="time-outline" size={size} color={color} />
          ),
        }}
      />
      <Tab.Screen
        name="Leave"
        component={LeaveScreen}
        options={{
          title: "İzin",
          tabBarLabel: "İzin",
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="calendar-outline" size={size} color={color} />
          ),
        }}
      />
      <Tab.Screen
        name="Payroll"
        component={PayrollScreen}
        options={{
          title: "Bordro",
          tabBarLabel: "Bordro",
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="cash-outline" size={size} color={color} />
          ),
        }}
      />
      <Tab.Screen
        name="Approvals"
        component={ApprovalsScreen}
        options={{
          title: "Onaylarım",
          tabBarLabel: "Onaylarım",
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="checkmark-done-outline" size={size} color={color} />
          ),
        }}
      />
      <Tab.Screen
        name="Profile"
        component={ProfileScreen}
        options={{
          title: "Profil",
          tabBarLabel: "Profil",
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="person-outline" size={size} color={color} />
          ),
        }}
      />
    </Tab.Navigator>
  );
}

function AppNavigator() {
  return (
    <AppStack.Navigator
      screenOptions={{
        headerShown: true,
      }}
    >
      <AppStack.Screen
        name="MainTabs"
        component={MainTabsNavigator}
        options={{
          headerShown: false,
        }}
      />
      <AppStack.Screen name="Support" component={SupportScreen} options={{ title: "Destek" }} />
      <AppStack.Screen
        name="CheckIn"
        component={CheckInScreen}
        options={{ title: "Giriş / Çıkış" }}
      />
      <AppStack.Screen name="People" component={PeopleScreen} options={{ title: "Kişiler" }} />
      <AppStack.Screen name="Shift" component={ShiftScreen} options={{ title: "Vardiyalarım" }} />
      <AppStack.Screen name="Goals" component={GoalsScreen} options={{ title: "Hedeflerim" }} />
    </AppStack.Navigator>
  );
}

export function RootNavigator() {
  const { isAuthenticated, isLoading, checkAuth } = useAuthStore();
  const [appReady, setAppReady] = useState(false);

  useEffect(() => {
    const initializeApp = async () => {
      try {
        await checkAuth();
      } catch (error) {
        if (__DEV__) {
          console.error("Failed to check authentication:", error);
        }
      } finally {
        setAppReady(true);
      }
    };

    initializeApp();
  }, [checkAuth]);

  if (!appReady || isLoading) {
    return (
      <View style={{ flex: 1, justifyContent: "center", alignItems: "center" }}>
        <ActivityIndicator size="large" color="#0066cc" />
      </View>
    );
  }

  return (
    <NavigationContainer>
      <RootStack.Navigator
        screenOptions={{
          headerShown: false,
        }}
      >
        {!isAuthenticated ? (
          <RootStack.Screen
            name="Auth"
            component={AuthNavigator}
            options={{
              animation: "none",
            }}
          />
        ) : (
          <RootStack.Screen
            name="App"
            component={AppNavigator}
            options={{
              animation: "none",
            }}
          />
        )}
      </RootStack.Navigator>
    </NavigationContainer>
  );
}
