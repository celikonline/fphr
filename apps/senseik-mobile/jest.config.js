module.exports = {
  preset: "jest-expo",
  testEnvironment: "node",
  // pnpm nests real packages under node_modules/.pnpm/<name>@<version>/node_modules/<name>,
  // so the default RN/Expo transformIgnorePatterns (anchored right after "node_modules/")
  // never matches and native-module Flow/JSX sources are left untransformed. Allow an
  // optional ".pnpm/...*/node_modules/" segment before the usual package name matches.
  transformIgnorePatterns: [
    "node_modules/(?!(jest-)?react-native|@react-native(-community)?|expo(nent)?|@expo(nent)?/.*|@expo-google-fonts/.*|react-navigation|@react-navigation/.*|@unimodules/.*|unimodules|sentry-expo|native-base|react-native-svg|@react-native-async-storage/.*|\\.pnpm/[^/]+/node_modules/(jest-)?react-native|\\.pnpm/[^/]+/node_modules/@react-native(-community)?|\\.pnpm/[^/]+/node_modules/expo(nent)?|\\.pnpm/[^/]+/node_modules/@expo(nent)?/.*|\\.pnpm/[^/]+/node_modules/@expo-google-fonts/.*|\\.pnpm/[^/]+/node_modules/react-navigation|\\.pnpm/[^/]+/node_modules/@react-navigation/.*|\\.pnpm/[^/]+/node_modules/@unimodules/.*|\\.pnpm/[^/]+/node_modules/unimodules|\\.pnpm/[^/]+/node_modules/sentry-expo|\\.pnpm/[^/]+/node_modules/native-base|\\.pnpm/[^/]+/node_modules/react-native-svg|\\.pnpm/[^/]+/node_modules/@react-native-async-storage/.*)",
  ],
  setupFilesAfterEnv: ["<rootDir>/src/__tests__/setup.ts"],
  testMatch: ["**/__tests__/**/*.test.ts?(x)", "**/?(*.)+(spec|test).ts?(x)"],
  moduleNameMapper: {
    "^@/(.*)$": "<rootDir>/src/$1"
  },
  collectCoverageFrom: [
    "src/**/*.{ts,tsx}",
    "!src/**/*.d.ts",
    "!src/**/*.stories.tsx",
    "!src/index.ts"
  ],
  coveragePathIgnorePatterns: ["/node_modules/", "/build/"],
  testPathIgnorePatterns: ["/node_modules/", "/build/"]
};
