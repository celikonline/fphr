const base = require("./app.json");

module.exports = () => {
  const mapsKey = process.env.GOOGLE_MAPS_ANDROID_API_KEY;
  const expo = { ...base.expo };
  expo.android = { ...expo.android, ...(mapsKey ? { config: { googleMaps: { apiKey: mapsKey } } } : {}) };
  expo.extra = { ...(expo.extra ?? {}), hasMapsKey: Boolean(mapsKey) };
  return { expo };
};
