<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { getMdrTemplates, getResponseCodes, saveMdrTemplate, saveResponseCode } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, ok } from "@/composables/inventory";
import { hasRole, ProjectControlRoles } from "@/composables/roles";
import { productTypes } from "@/composables/quality";

// MDR templates (document list per product type, copied to a work order) and the client response codes (A, B, C, D ...).
const tab = ref("templates");
const tabs = [{ key: "templates", title: "قالب‌های MDR" }, { key: "codes", title: "کدهای پاسخ کارفرما" }];
const templates = ref<any[]>([]);
const codes = ref<any[]>([]);
const tpl = ref<any>(null);
const code = ref<any>(null);
const busy = ref(false);
const canTpl = hasRole(ProjectControlRoles.mdrTemplates);
const canCode = hasRole(ProjectControlRoles.dccOnly);

async function load() {
  const [[t, e1], [c, e2]] = await Promise.all([getMdrTemplates(), getResponseCodes()]);
  if (e1) fail(e1);
  else templates.value = rowsOf(t);
  if (e2) fail(e2);
  else codes.value = Array.isArray(c) ? c : [];
}
const newTpl = () => ({ code: "", title: "", productType: 1, standardSource: "", note: "", isActive: true, lines: [] as any[] });
const addLine = () => tpl.value.lines.push({ seq: tpl.value.lines.length + 1, documentType: "", discipline: "", title: "", clientSubmittal: false, required: true, standardSource: "" });
async function saveTpl() {
  busy.value = true;
  tpl.value.lines.forEach((l: any, i: number) => (l.seq = i + 1));
  const [, e] = await saveMdrTemplate(tpl.value);
  busy.value = false;
  if (e) return fail(e);
  ok("ذخیره شد.");
  tpl.value = null;
  load();
}
async function saveCode() {
  busy.value = true;
  const [, e] = await saveResponseCode(code.value);
  busy.value = false;
  if (e) return fail(e);
  ok("ذخیره شد.");
  code.value = null;
  load();
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="تنظیمات مدارک مهندسی" />
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title screen-only">تنظیمات مدارک مهندسی</h1></div>
      <PcTabs v-model="tab" :tabs="tabs" />
      <div v-if="tab === 'templates'">
        <div class="mb-2 d-print-none"><button v-if="canTpl" class="btn btn-sm btn-primary" @click="tpl = newTpl()">قالب جدید</button></div>
        <table class="table table-sm">
          <thead><tr><th>کد</th><th>عنوان</th><th>نوع محصول</th><th>منبع استاندارد</th><th>ردیف</th><th>فعال</th><th class="d-print-none"></th></tr></thead>
          <tbody>
            <tr v-if="!templates.length"><td colspan="7" class="text-center inv-muted">قالبی تعریف نشده است.</td></tr>
            <tr v-for="t in templates" :key="t.id"><td class="inv-ltr">{{ t.code }}</td><td>{{ t.title }}</td><td>{{ productTypes[t.productType] }}</td><td>{{ t.standardSource }}</td><td>{{ t.lines?.length }}</td><td>{{ t.isActive ? "بله" : "خیر" }}</td><td class="d-print-none"><button v-if="canTpl" class="btn btn-sm btn-outline-secondary" @click="tpl = JSON.parse(JSON.stringify(t))">ویرایش</button></td></tr>
          </tbody>
        </table>
      </div>
      <div v-else>
        <div class="mb-2 d-print-none"><button v-if="canCode" class="btn btn-sm btn-primary" @click="code = { code: '', title: '', countsAsApproved: false, requiresResubmit: false, source: '' }">کد جدید</button></div>
        <table class="table table-sm">
          <thead><tr><th>کد</th><th>عنوان</th><th>تایید محسوب می‌شود</th><th>نیاز به ارسال مجدد</th><th>منبع</th><th class="d-print-none"></th></tr></thead>
          <tbody>
            <tr v-for="c in codes" :key="c.id"><td><b class="inv-ltr">{{ c.code }}</b></td><td>{{ c.title }}</td><td>{{ c.countsAsApproved ? "بله" : "خیر" }}</td><td>{{ c.requiresResubmit ? "بله" : "خیر" }}</td><td>{{ c.source }}</td><td class="d-print-none"><button v-if="canCode" class="btn btn-sm btn-outline-secondary" @click="code = { ...c }">ویرایش</button></td></tr>
          </tbody>
        </table>
        <p class="inv-muted">کدهای پاسخ را DCC تعریف می‌کند و باید با روش پاسخ‌دهی کارفرما در قرارداد یکی باشد؛ این صفحه کدی را از خود نمی‌سازد.</p>
      </div>
    </div>

    <div v-if="tpl" class="inv-modal-backdrop" @click.self="tpl = null">
      <div class="inv-modal" style="max-width: 1000px; width: 96%">
        <h3 class="h6">قالب MDR</h3>
        <div class="row g-2 mb-2">
          <div class="col-md-3"><label class="form-label small mb-0">کد</label><input v-model="tpl.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-5"><label class="form-label small mb-0">عنوان</label><input v-model="tpl.title" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع محصول</label><select v-model.number="tpl.productType" class="form-select form-select-sm"><option v-for="(n, k) in productTypes" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">منبع استاندارد (پیش‌فرض ردیف‌ها)</label><input v-model="tpl.standardSource" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">یادداشت</label><input v-model="tpl.note" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-check mt-4"><input v-model="tpl.isActive" class="form-check-input" type="checkbox" /> فعال</label></div>
        </div>
        <div style="max-height: 40vh; overflow: auto">
          <table class="table table-sm">
            <thead><tr><th>#</th><th>عنوان</th><th>نوع سند</th><th>رشته</th><th>منبع</th><th>کارفرما</th><th>الزامی</th><th></th></tr></thead>
            <tbody>
              <tr v-for="(l, i) in tpl.lines" :key="i">
                <td>{{ i + 1 }}</td><td><input v-model="l.title" class="form-control form-control-sm" /></td><td><input v-model="l.documentType" class="form-control form-control-sm" /></td><td><input v-model="l.discipline" class="form-control form-control-sm" /></td><td><input v-model="l.standardSource" class="form-control form-control-sm" /></td>
                <td><input v-model="l.clientSubmittal" type="checkbox" class="form-check-input" /></td><td><input v-model="l.required" type="checkbox" class="form-check-input" /></td><td><button class="btn btn-sm btn-outline-danger" @click="tpl.lines.splice(i, 1)">حذف</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <button class="btn btn-sm btn-outline-primary mb-2" @click="addLine">افزودن ردیف</button>
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="tpl = null">انصراف</button><button :disabled="busy || !tpl.code || !tpl.title" class="btn btn-sm btn-primary" @click="saveTpl">ذخیره</button></div>
      </div>
    </div>
    <div v-if="code" class="inv-modal-backdrop" @click.self="code = null">
      <div class="inv-modal">
        <h3 class="h6">کد پاسخ کارفرما</h3>
        <label class="form-label small mb-0">کد (حداکثر ۵ نویسه)</label><input v-model="code.code" class="form-control form-control-sm inv-ltr mb-2" maxlength="5" />
        <label class="form-label small mb-0">عنوان</label><input v-model="code.title" class="form-control form-control-sm mb-2" />
        <label class="form-check"><input v-model="code.countsAsApproved" class="form-check-input" type="checkbox" /> تایید محسوب می‌شود</label>
        <label class="form-check mb-2"><input v-model="code.requiresResubmit" class="form-check-input" type="checkbox" /> نیاز به ارسال مجدد</label>
        <label class="form-label small mb-0">منبع</label><input v-model="code.source" class="form-control form-control-sm mb-3" />
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="code = null">انصراف</button><button :disabled="busy || !code.code || !code.title" class="btn btn-sm btn-primary" @click="saveCode">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
