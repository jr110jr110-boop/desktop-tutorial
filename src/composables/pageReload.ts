import { ref } from "vue";
import type { Router } from "vue-router";

/** Bumped when a menu item of the page already open is clicked; App.vue adds it to the routed component key so the page remounts and reloads its data. */
export const pageReloadKey = ref(0);

/** Menu navigation: a click on the item of the current page remounts it (no browser reload); any other target is a normal push. */
export function goMenu(router: Router, name: string | undefined) {
  if (!name) return;
  const target = router.resolve({ name });
  if (target.path === router.currentRoute.value.path) pageReloadKey.value++;
  else router.push({ name });
}
