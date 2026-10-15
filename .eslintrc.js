module.exports = {
  env: {
    "vue/setup-compiler-macros": true,
    browser: true,
    es2021: true,
    node: true,
  },
  extends: [
    "eslint:recommended",
    "plugin:vue/essential",
    "plugin:@typescript-eslint/recommended",
    "plugin:vue/vue3-recommended",
    "prettier",
  ],
  parser: "vue-eslint-parser",
  parserOptions: {
    ecmaVersion: "latest",
    parser: "@typescript-eslint/parser",
    sourceType: "module",
  },
  plugins: ["vue", "@typescript-eslint"],
  rules: {
    "@typescript-eslint/no-explicit-any": "off",
    "vue/v-on-event-hyphenation": ["error", "never"],
    "vue/multi-word-component-names": "off",
  },
};
