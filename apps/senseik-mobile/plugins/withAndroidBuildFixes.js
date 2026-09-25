// Expo loads config plugins with Node's require(), and this package has no "type": "module".
// eslint-disable-next-line @typescript-eslint/no-require-imports
const configPlugins = require("expo/config-plugins");

const { withMainActivity, withMainApplication, withProjectBuildGradle } = configPlugins;

// Windows: the Android SDK's default CMake 3.22.1 ships ninja 1.10, which can't handle pnpm's
// long node_modules paths ("build.ninja still dirty after 100 tries"). CMake 3.31 ships ninja
// 1.12 with long-path support, so every native module is pinned to it.
const CMAKE_VERSION = "3.31.6";
const CMAKE_MARKER = "// @senseik/cmake-version";
const CMAKE_BLOCK = `${CMAKE_MARKER}
subprojects {
  plugins.withId("com.android.library") {
    android.externalNativeBuild.cmake.version = "${CMAKE_VERSION}"
  }
  plugins.withId("com.android.application") {
    android.externalNativeBuild.cmake.version = "${CMAKE_VERSION}"
  }
}

`;
// "expo-root-project" evaluates the subprojects, after which AGP rejects the CMake version
// ("It is too late to set version"), so the block has to be registered before it is applied.
const ROOT_PROJECT_PLUGIN = 'apply plugin: "expo-root-project"';

function withCmakeVersion(config) {
  return withProjectBuildGradle(config, (config) => {
    const { contents } = config.modResults;
    if (!contents.includes(CMAKE_MARKER)) {
      if (!contents.includes(ROOT_PROJECT_PLUGIN)) {
        throw new Error(
          `withAndroidBuildFixes: '${ROOT_PROJECT_PLUGIN}' not found in build.gradle`
        );
      }
      config.modResults.contents = contents.replace(
        ROOT_PROJECT_PLUGIN,
        CMAKE_BLOCK + ROOT_PROJECT_PLUGIN
      );
    }
    return config;
  });
}

// Prebuild moves MainActivity/MainApplication into the `android.package` folder but leaves the
// slug-derived `package com.senseikmobile` line, so BuildConfig can't be resolved.
function withPackageDeclaration(withMod) {
  return (config) =>
    withMod(config, (config) => {
      config.modResults.contents = config.modResults.contents.replace(
        /^package [\w.]+$/m,
        `package ${config.android.package}`
      );
      return config;
    });
}

module.exports = function withAndroidBuildFixes(config) {
  config = withCmakeVersion(config);
  config = withPackageDeclaration(withMainActivity)(config);
  return withPackageDeclaration(withMainApplication)(config);
};
