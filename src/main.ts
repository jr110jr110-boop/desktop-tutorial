import { createApp } from "vue";
import App from "./App.vue";
import router from "./router";

import "./assets/css/style.scss";
import "bootstrap/scss/bootstrap.scss";
import "bootstrap/dist/js/bootstrap.min";
import "./assets/css/report.scss";

createApp(App).use(router).mount("#app");

// Load the report font now: a printout made before it loads would fall back to another font.
try {
  document.fonts.load('16px "B Zar"', "گزارش").catch(() => undefined);
  document.fonts.load('bold 16px "B Zar"', "گزارش").catch(() => undefined);
} catch {
  /* old browser without the font loading API */
}
