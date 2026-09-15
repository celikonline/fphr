const { getDefaultConfig } = require("expo/metro-config");

const config = getDefaultConfig(__dirname);

config.resolver.extraNodeModules = new Proxy(
  {},
  {
    get: (target, prop) => {
      if (typeof prop !== "string" || prop.includes("/")) {
        return undefined;
      }
      return require.resolve(prop);
    }
  }
);

module.exports = config;
