<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getQualitySettings, saveQualitySetting } from "@/api/QualityAPIs";
import { fail, ok } from "@/composables/inventory";
import { hasRole, QualityRoles } from "@/composables/roles";

const items = ref<any[]>([]);
const busy = ref(false);
const canWrite = hasRole(QualityRoles.settings);

async function load() {
  const [data, error] = await getQualitySettings();
  if (error) return fail(error);
  items.value = (data || []).map((s: any) => ({ ...s, edit: s.value }));
}

async function save(s: any) {
  busy.value = true;
  const [, error] = await saveQualitySetting(s.key, Number(s.edit));
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  load();
}

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">تنظیمات کیفیت</h1></div>
      <table class="table table-sm align-middle">
        <thead><tr><th>تنظیم</th><th>مقدار</th><th>مبنا</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!items.length"><td colspan="4" class="text-center inv-muted">تنظیمی نیست.</td></tr>
          <tr v-for="s in items" :key="s.key">
            <td>{{ s.title }}<div class="inv-muted small inv-ltr">{{ s.key }}</div></td>
            <td style="max-width: 140px"><input v-model.number="s.edit" :disabled="!canWrite" class="form-control form-control-sm" type="number" step="any" /></td>
            <td class="small">{{ s.isDefault ? "پیش‌فرض سیستم" : s.source }}</td>
            <td><button v-if="canWrite" :disabled="busy || s.edit === s.value" class="btn btn-sm btn-primary" @click="save(s)">ذخیره</button></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
