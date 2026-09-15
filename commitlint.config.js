module.exports = {
  extends: ["@commitlint/config-conventional"],
  // Matches the scopes already used in this repo's history (feat(item-21): ..., fix(item-16): ...).
  rules: {
    "scope-case": [2, "always", "kebab-case"],
  },
};
