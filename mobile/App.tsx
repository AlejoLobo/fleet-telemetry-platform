import { StatusBar } from "expo-status-bar";
import { SafeAreaView, StyleSheet } from "react-native";
import { AuthSessionProvider } from "@/context/auth-session-context";
import { DriverDashboard } from "@/components/DriverDashboard";

export default function App() {
  return (
    <AuthSessionProvider>
      <SafeAreaView style={styles.root}>
        <StatusBar style="dark" />
        <DriverDashboard />
      </SafeAreaView>
    </AuthSessionProvider>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: "#f8fafc",
  },
});
