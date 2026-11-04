<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getProducts, saveProduct, seedFy } from "@/api/ProductBankAPIs";
import { fail, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { PbRoles, revisionText } from "@/composables/productBank";

// Product bank: each product has a released BOM, OPC routings and a WBS template.
const rows = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(PbRoles.writers);
const canSeed = hasRole(PbRoles.admin);

async function load() {
  const [data, error] = await getProducts();
  if (error) return fail(error);
  rows.value = data || [];
}
async function save() {
  busy.value = true;
  const [, error] = await saveProduct(form.value);
  busy.value = false;
  if (error) return fail(error);
  form.value = null;
  ok("محصول ذخیره شد.");
  load();
}
async function seed() {
  if (!window.confirm("اطلاعات محصول FY شرکت (BOM، چارت‌های OPC، قالب WBS و چند کالا) نصب شود؟ این کار فقط یک بار انجام می‌شود.")) return;
  busy.value = true;
  const [data, error] = await seedFy();
  busy.value = false;
  if (error) return fail(error);
  ok(data.alreadyInstalled ? "اطلاعات FY از قبل نصب شده بود." : `نصب شد: ${data.bomLines} ردیف BOM، ${data.routings} چارت OPC، ${data.templateNodes} فعالیت WBS، ${data.mappings} پیشنهاد نگاشت.`);
  load();
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="بانک محصول" subtitle="BOM + مسیر ساخت OPC + قالب WBS" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">بانک محصول</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button v-if="canSeed && !rows.length" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="seed">نصب اطلاعات محصول FY شرکت</button>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = { id: null, code: '', title: '', platform: '', description: '', isActive: true }">+ محصول</button>
        </div>
      </div>
      <p class="inv-muted screen-only">پروژه هنگام گرفتن محصول از بانک، نسخهٔ منتشرشدهٔ BOM، چارت‌های تاییدشده و قالب WBS تاییدشده را به‌عنوان مبنای خود کپی می‌کند؛ بعدا تغییر بانک روی پروژه اثر نمی‌گذارد.</p>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>عنوان</th><th>پلتفرم</th><th>BOM منتشرشده</th><th>نسخه‌ها</th><th>چارت OPC تاییدشده</th><th>قالب WBS</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="8">محصولی در بانک نیست.</td></tr>
          <tr v-for="p in rows" :key="p.id">
            <td class="inv-ltr">{{ p.code }}</td>
            <td>{{ p.title }}</td>
            <td class="inv-ltr">{{ p.platform }}</td>
            <td>{{ p.releasedRevision === null || p.releasedRevision === undefined ? "—" : "نسخه " + revisionText(p.releasedRevision) }}</td>
            <td>{{ p.bomRevisions }}</td>
            <td>{{ p.routingCount }}</td>
            <td>{{ p.wbsTemplateVersion ? "نسخه " + p.wbsTemplateVersion : "—" }}</td>
            <td class="d-print-none"><router-link :to="{ name: 'ProductBankProduct', params: { id: p.id } }" class="btn btn-sm btn-outline-primary py-0">باز کردن</router-link></td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 520px">
        <h2 class="inv-title mb-2">محصول جدید</h2>
        <label class="form-label small mb-0">شماره مجموعه *</label>
        <input v-model="form.code" class="form-control form-control-sm inv-ltr" placeholder="16.11.00.000.2" />
        <label class="form-label small mb-0 mt-2">عنوان *</label>
        <input v-model="form.title" class="form-control form-control-sm" />
        <label class="form-label small mb-0 mt-2">پلتفرم</label>
        <input v-model="form.platform" class="form-control form-control-sm inv-ltr" />
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
