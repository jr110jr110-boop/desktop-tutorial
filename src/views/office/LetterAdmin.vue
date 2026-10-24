<script setup lang="ts">
import { computed, ref } from "vue";
import ImportTab from "@/components/office/letters/ImportTab.vue";
import NumberingRulesTab from "@/components/office/letters/NumberingRulesTab.vue";
import PartiesTab from "@/components/office/letters/PartiesTab.vue";
import TemplatesTab from "@/components/office/letters/TemplatesTab.vue";
import { hasRole, OfficeRoles } from "@/composables/roles";

// Letter administration: numbering rules, templates, external parties, legacy archive import (secretariat only).
const canImport = hasRole(OfficeRoles.secretariat);
const tabs = computed(() => [
  { key: "rules", title: "قواعد شماره‌گذاری" },
  { key: "templates", title: "قالب‌های نامه" },
  { key: "parties", title: "طرف‌های مکاتبه" },
  ...(canImport ? [{ key: "import", title: "ورود آرشیو قدیم" }] : []),
]);
const tab = ref("rules");
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">مدیریت نامه‌ها</h1></div>
      <ul class="nav nav-tabs mb-3">
        <li v-for="t in tabs" :key="t.key" class="nav-item"><a :class="{ active: tab === t.key }" class="nav-link" href="#" @click.prevent="tab = t.key">{{ t.title }}</a></li>
      </ul>
      <NumberingRulesTab v-if="tab === 'rules'" />
      <TemplatesTab v-else-if="tab === 'templates'" />
      <PartiesTab v-else-if="tab === 'parties'" />
      <ImportTab v-else-if="tab === 'import' && canImport" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
