<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getPcSettings, savePcSettings } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, ok } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";

// Numbers the company can change in project control (thresholds, tolerances). A source that starts with the star is a provisional default nobody confirmed yet.
// Reads: project-control readers; saving: PMO and CEO only (the service answers in Persian otherwise).
const rows = ref<any[]>([]);
const edit = ref<Record<string, string>>({});
const busy = ref(false);
const canWrite = hasRole(["PMO", "CEO"]);

async function load() {
  const [d, e] = await getPcSettings();
  if (e) return fail(e);
  rows.value = rowsOf(d);
  edit.value = Object.fromEntries(rows.value.map((r) => [r.key, String(r.value)]));
}
const provisional = (r: any) => String(r.source || "").startsWith("★");
async function save() {
  const items: { key: string; value: number }[] = [];
  for (const r of rows.value) {
    const v = Number(edit.value[r.key]);
    if (!Number.isFinite(v) || v < r.min || v > r.max) return fail(`«${r.title}»: مقدار باید بین ${r.min} و ${r.max} باشد.`);
    if (r.wholeNumber && !Number.isInteger(v)) return fail(`«${r.title}»: مقدار باید عدد صحیح باشد.`);
    if (v !== Number(r.value)) items.push({ key: r.key, value: v });
  }
  if (!items.length) return fail("تغییری وارد نشده است.");
  busy.value = true;
  const [, error] = await savePcSettings(items);
  busy.value = false;
  if (error) return fail(error);
  ok("تنظیمات ذخیره شد.");
  load();
}
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">تنظیمات کنترل پروژه</h1><button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      <p class="inv-muted small">مقدارهای دارای نشان ★ پیش‌فرض موقت هستند و هنوز تأیید نشده‌اند؛ با ذخیره‌ی مقدار، تأیید ثبت می‌شود. فقط معاون PMO و مدیرعامل می‌توانند تغییر دهند.</p>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>تنظیم</th><th>منبع</th><th style="width: 10rem">مقدار</th><th>پیش‌فرض</th><th>حدود</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="6" class="text-center inv-muted">تنظیمی نیست.</td></tr>
            <tr v-for="r in rows" :key="r.key">
              <td>{{ r.title }}<div class="inv-muted small inv-ltr">{{ r.key }}</div></td>
              <td class="small">{{ r.source }}</td>
              <td><input v-model="edit[r.key]" :disabled="!canWrite" class="form-control form-control-sm inv-ltr" inputmode="decimal" /></td>
              <td class="inv-ltr">{{ r.defaultValue }}</td>
              <td class="inv-ltr small">{{ r.min }} - {{ r.max }}</td>
              <td><span v-if="provisional(r)" class="inv-badge warn">پیش‌فرض موقت</span><span v-else-if="r.confirmed" class="inv-badge ok">تأییدشده</span><span v-else-if="r.isDefault" class="inv-badge">پیش‌فرض</span></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
