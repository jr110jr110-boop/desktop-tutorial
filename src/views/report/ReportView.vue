<script setup lang="ts">
import { computed } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";

const route = useRoute();
const title = computed(() => String(route.meta.title || "گزارش"));

function printReport() {
  window.print();
}
</script>

<template>
  <div class="ks-report">
    <div class="d-flex justify-content-end mb-2 d-print-none">
      <button class="btn btn-sm btn-outline-secondary" type="button" @click="printReport">چاپ گزارش</button>
    </div>
    <KsReportHeader :title="title" />
    <router-view v-slot="{ Component, route: r }">
      <transition mode="out-in" name="fade">
        <component :is="Component" :key="r.path" />
      </transition>
    </router-view>
  </div>
</template>
