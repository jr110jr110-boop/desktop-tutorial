<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { useRoute } from "vue-router";
import ConnectionsTab from "@/components/production/dnc/ConnectionsTab.vue";
import ProgramsTab from "@/components/production/dnc/ProgramsTab.vue";
import TransfersTab from "@/components/production/dnc/TransfersTab.vue";

// DNC (MfDnc): the CNC program library, the transfers to and from the machines, the machine links. /production/dnc/programs/:id opens a program; /production/dnc/transfers the transfers.
const route = useRoute();
const openId = computed(() => (route.params.id ? Number(route.params.id) : null));
const initial = route.name === "MfDncTransfers" ? "transfers" : "programs";
const tab = ref<"programs" | "transfers" | "links">(initial);
watch(openId, (id) => id && (tab.value = "programs"));
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">برنامه‌های CNC و انتقال به ماشین (DNC)</h1></div>
      <ul class="nav nav-tabs mb-3">
        <li class="nav-item"><a :class="{ active: tab === 'programs' }" class="nav-link" href="#" @click.prevent="tab = 'programs'">کتابخانه برنامه‌ها</a></li>
        <li class="nav-item"><a :class="{ active: tab === 'transfers' }" class="nav-link" href="#" @click.prevent="tab = 'transfers'">انتقال‌ها</a></li>
        <li class="nav-item"><a :class="{ active: tab === 'links' }" class="nav-link" href="#" @click.prevent="tab = 'links'">اتصال ماشین‌ها</a></li>
      </ul>
      <ProgramsTab v-if="tab === 'programs'" :open-id="openId" />
      <TransfersTab v-else-if="tab === 'transfers'" />
      <ConnectionsTab v-else />
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
