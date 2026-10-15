import { createApp } from "vue";
import App from "./App.vue";
import router from "./router";

import "./assets/css/style.scss";
import "bootstrap/scss/bootstrap.scss";
import "bootstrap/dist/js/bootstrap.min";

createApp(App).use(router).mount("#app");
