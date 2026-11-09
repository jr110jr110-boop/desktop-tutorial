<script setup lang="ts">
import { ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import { getOperationStandards } from "@/api/ProductionAPIs";
import { getChecklistTemplate, saveChecklistTemplate } from "@/api/ShopAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { itemLabel, searchItems } from "./masterKit";

// The in-process checklist of each routing step: yes-no items, measurements with limits and text notes. A measurement with a characteristic code can be "device only" (P2 part E): its value comes
// from the machine probe or a digital gauge, never typed. With "required to finish", the terminal does not finish the step before the checklist is submitted and passing.
const item = ref<any>(null);
const steps = ref<any[]>([]);
const step = ref<any>(null);
const t = ref<any>(null);
const busy = ref(false);
const canEdit = hasRole(["PRODUCTIONMANAGER", "ENGINEERING", "ENGINEER", "PMO", "QC", "QA"]);
const kinds: Record<number, string> = { 1: "بله / خیر", 2: "اندازه‌گیری", 3: "متن" };

async function pickItem(i: any) {
  item.value = i;
  step.value = null;
  t.value = null;
  if (!i) return (steps.value = []);
  const [d, error] = await getOperationStandards({ ItemId: i.id, PageSize: 200 });
  if (error) return fail(error);
  steps.value = rowsOf(camelize(d));
}
async function pickStep(s: any) {
  step.value = s;
  const [d, error] = await getChecklistTemplate(s.operationTemplateId);
  if (error) return fail(error);
  t.value = { ...d, title: d.title || "کنترل حین " + s.templateTitle, items: d.items.length ? d.items : [{ text: "", kind: 1, mandatory: true }] };
}
const add = () => t.value.items.push({ text: "", kind: 1, mandatory: true });
const n = (v: any) => (v === "" || v === null || v === undefined ? null : Number(v));
async function save() {
  busy.value = true;
  const [d, error] = await saveChecklistTemplate({ ...t.value, items: t.value.items.map((i: any) => ({ ...i, nominal: n(i.nominal), min: n(i.min), max: n(i.max), unit: i.unit || null, characteristic: i.characteristic || null, deviceOnly: i.kind === 2 && !!i.characteristic && !!i.deviceOnly })) });
  busy.value = false;
  if (error) return fail(error);
  t.value = { ...d };
  ok("چک‌لیست ذخیره شد.");
}
</script>

<template>
  <div>
    <div class="inv-filters mb-3">
      <div style="min-width: 260px"><label class="form-label small mb-0">قطعه</label><LookupBox :model-value="item" :fetch="searchItems" :label="itemLabel" placeholder="کد یا نام قطعه" @pick="pickItem" /></div>
      <div v-if="steps.length"><label class="form-label small mb-0">مرحله</label><select :value="step?.operationTemplateId ?? null" class="form-select form-select-sm" @change="pickStep(steps.find((s) => s.operationTemplateId === Number(($event.target as HTMLSelectElement).value)))"><option :value="null">انتخاب کنید</option><option v-for="s in steps" :key="s.operationTemplateId" :value="s.operationTemplateId">{{ s.sequence }} - {{ s.templateTitle }}</option></select></div>
    </div>
    <template v-if="t">
      <div class="row g-2 small mb-2">
        <div class="col-md-6"><label class="form-label mb-0">عنوان چک‌لیست</label><input v-model="t.title" :disabled="!canEdit" class="form-control form-control-sm" /></div>
        <div class="col-md-3 form-check ms-2 mt-4"><input id="cl-req" v-model="t.requiredToFinish" :disabled="!canEdit" class="form-check-input" type="checkbox" /><label class="form-check-label" for="cl-req">الزامی برای پایان مرحله</label></div>
        <div class="col-md-2 form-check ms-2 mt-4"><input id="cl-act" v-model="t.isActive" :disabled="!canEdit" class="form-check-input" type="checkbox" /><label class="form-check-label" for="cl-act">فعال</label></div>
      </div>
      <table class="table table-sm">
        <thead><tr><th>بند</th><th>نوع</th><th>اسمی</th><th>حد پایین</th><th>حد بالا</th><th>واحد</th><th title="کدی که سیکل پراب یا گیج گزارش می‌کند، مثل D1">کد مشخصه</th><th title="مقدار فقط از پراب ماشین یا گیج دیجیتال پذیرفته می‌شود">فقط از دستگاه</th><th>الزامی</th><th></th></tr></thead>
        <tbody>
          <tr v-for="(i, k) in t.items" :key="k">
            <td><input v-model="i.text" :disabled="!canEdit" class="form-control form-control-sm" /></td>
            <td><select v-model.number="i.kind" :disabled="!canEdit" class="form-select form-select-sm"><option v-for="(x, v) in kinds" :key="v" :value="Number(v)">{{ x }}</option></select></td>
            <td><input v-if="i.kind === 2" v-model="i.nominal" :disabled="!canEdit" class="form-control form-control-sm inv-ltr" /></td>
            <td><input v-if="i.kind === 2" v-model="i.min" :disabled="!canEdit" class="form-control form-control-sm inv-ltr" /></td>
            <td><input v-if="i.kind === 2" v-model="i.max" :disabled="!canEdit" class="form-control form-control-sm inv-ltr" /></td>
            <td><input v-if="i.kind === 2" v-model="i.unit" :disabled="!canEdit" class="form-control form-control-sm inv-ltr" style="width: 70px" /></td>
            <td><input v-if="i.kind === 2" v-model="i.characteristic" :disabled="!canEdit" class="form-control form-control-sm inv-ltr" placeholder="D1" style="width: 80px" /></td>
            <td><input v-if="i.kind === 2" v-model="i.deviceOnly" :disabled="!canEdit || !i.characteristic" type="checkbox" /></td>
            <td><input v-model="i.mandatory" :disabled="!canEdit" type="checkbox" /></td>
            <td><button v-if="canEdit" class="btn btn-sm btn-outline-danger" @click="t.items.splice(k, 1)">حذف</button></td>
          </tr>
        </tbody>
      </table>
      <div v-if="canEdit" class="d-flex gap-2"><button class="btn btn-sm btn-outline-primary" @click="add">افزودن بند</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره چک‌لیست</button></div>
    </template>
  </div>
</template>
