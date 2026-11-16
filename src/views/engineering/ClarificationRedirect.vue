<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { getEnClarification } from "@/api/EngineeringAPIs";
import { enPaths, errText } from "@/composables/engineering";

// The link of a clarification (written by the API into agent steps) opens the case that holds it.
const route = useRoute();
const router = useRouter();
const message = ref("در حال باز کردن پرونده…");
onMounted(async () => {
  const [d, e] = await getEnClarification(Number(route.params.id));
  if (e || !d) {
    message.value = errText(e, "شفاف‌سازی پیدا نشد.");
    return;
  }
  router.replace(enPaths.case(d.caseId) + "#clarifications");
});
</script>

<template>
  <div class="inv-page"><div class="inv-card"><p class="inv-muted">{{ message }}</p></div></div>
</template>
