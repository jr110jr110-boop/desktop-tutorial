import { createApp } from "vue";
import App from "./App.vue";
import router from "./router";
import { setSessionExpiredHandler } from "@/api/ApiClient";
import useNotification from "@/components/common/KsNotification/useNotification";

import "./assets/css/style.scss";
import "bootstrap/scss/bootstrap.scss";
import "bootstrap/dist/js/bootstrap.min";
import "./assets/css/report.scss";

// The API refused the refresh token (session over, signed in elsewhere, account stopped): the login page with the usual notice.
setSessionExpiredHandler(() => {
  router.push({ name: "Login" });
  useNotification.addNotification({
    title: "انقضای مهلت دسترسی",
    text: "لطفا دوباره به سیستم وارد شوید",
    color: "primary",
    duration: 3000,
  });
});

createApp(App).use(router).mount("#app");

// Load the report font now: a printout made before it loads would fall back to another font.
try {
  document.fonts.load('16px "B Zar"', "گزارش").catch(() => undefined);
  document.fonts.load('bold 16px "B Zar"', "گزارش").catch(() => undefined);
} catch {
  /* old browser without the font loading API */
}
