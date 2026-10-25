import { createRequire } from "node:module";
const require = createRequire("/home/claude/front/package.json");
const vue = require("@vitejs/plugin-vue");
export default {
  root: "/home/claude/front",
  plugins: [vue()],
  resolve: { alias: { "@": "/home/claude/front/src", "vue-router": "/home/claude/front/node_modules/vue-router/dist/vue-router.esm-bundler.js", vue: "/home/claude/front/node_modules/vue/dist/vue.esm-bundler.js" } },
  define: { "process.env.NODE_ENV": '"development"' },
  build: { outDir: "/home/claude/mc3p/bundle", emptyOutDir: true, minify: false, lib: { entry: "/home/claude/mc3p/entry.ts", name: "chk", formats: ["iife"], fileName: () => "chk.js" } },
};
