<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getProcurementSettings, saveProcurementSetting } from "@/api/ProcurementAPIs";
import { fail, ok, rial } from "@/composables/inventory";
import { hasRole, ProcurementRoles } from "@/composables/roles";

// Thresholds of the purchase rules (rial) with the procedure each one comes from. Only the CEO changes them.
const rows = ref<any[]>([]);
const busy = ref(false);
const canEdit = hasRole(ProcurementRoles.management);

async function load() {
  const [data, error] = await getProcurementSettings();
  if (error) return fail(error);
  rows.value = (data || []).map((s: any) => ({ ...s, edit: s.value }));
}

// percent and switch settings are not amounts in rial
const isPlain = (s: any) => /Percent|Enforce/i.test(s.key);

async function save(s: any) {
  busy.value = true;
  const [, error] = await saveProcurementSetting(s.key, Number(s.edit));
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
      <div class="inv-toolbar">
        <h1 class="inv-title">تنظیمات خرید (سقف‌ها و آستانه‌ها)</h1>
      </div>
      <p class="inv-muted">
        مبلغ‌ها به ریال است. هر ردیف مرجع آیین‌نامه یا دستورالعمل خود را دارد. فقط مدیرعامل می‌تواند تغییر دهد و هر تغییر روی درخواست‌های بعدی اثر می‌گذارد.
      </p>
      <div class="table-responsive">
        <table class="table table-sm table-bordered">
          <thead>
            <tr><th>تنظیم</th><th style="width: 230px">مقدار</th><th>معادل</th><th>مرجع</th><th v-if="canEdit"></th></tr>
          </thead>
          <tbody>
            <tr v-for="s in rows" :key="s.key">
              <td>{{ s.title }} <span class="inv-ltr inv-muted">{{ s.key }}</span></td>
              <td><input v-model.number="s.edit" :disabled="!canEdit" class="form-control form-control-sm inv-ltr" min="0" type="number" /></td>
              <td class="num">{{ isPlain(s) ? "" : rial(s.edit) + " ریال" }}<span v-if="!isPlain(s)" class="inv-muted"> ({{ rial(s.edit / 10) }} تومان)</span></td>
              <td>{{ s.source }}</td>
              <td v-if="canEdit"><button :disabled="busy || s.edit === s.value" class="btn btn-sm btn-outline-primary" @click="save(s)">ذخیره</button></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
