<script setup lang="ts">
import { onMounted, ref } from "vue";
import ProcedurePicker from "@/components/quality/ProcedurePicker.vue";
import { getSpTypes, saveSpType } from "@/api/QualityAPIs";
import { fail, ok } from "@/composables/inventory";
import { instrumentClasses } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Package C: the list of special processes (heat treatment, nitriding, coating/painting, brazing, plating, NDT). The list is editable: a type is deactivated, never deleted. Engineering writes the parameter
// limits here (the system invents none); a qualification can only narrow them. NDT is controlled by the existing personnel qualifications, so it needs no process qualification.
const rows = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);
const canEdit = hasRole([...QualityRoles.engineering, ...QualityRoles.qaManager]);

async function load() {
  const [data, error] = await getSpTypes(true);
  if (error) return fail(error);
  rows.value = Array.isArray(data) ? data : [];
}
const blank = () => ({ id: null, code: "", title: "", description: "", requiresMachine: true, requiresSurvey: false, requiredInstrumentClass: null as number | null, procedureDeliverableId: null as number | null, existingControl: false, isActive: true, parameters: [] as any[], rowVersion: null });
function edit(t: any) {
  form.value = { ...t, parameters: (t.parameters || []).map((p: any) => ({ ...p })) };
}
const addParameter = () => form.value.parameters.push({ id: 0, sortOrder: form.value.parameters.length, name: "", unit: "", min: null, max: null, required: true });
async function save() {
  const f = form.value;
  const nn = (v: any) => (v === "" || v === undefined ? null : v);
  busy.value = true;
  const [, error] = await saveSpType({ ...f, requiredInstrumentClass: f.requiredInstrumentClass || null, parameters: f.parameters.map((p: any, i: number) => ({ ...p, sortOrder: i, min: nn(p.min), max: nn(p.max) })) });
  busy.value = false;
  if (error) return fail(error);
  ok("فرایند ذخیره شد.");
  form.value = null;
  load();
}
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">فهرست فرایندهای ویژه</h1>
        <button v-if="canEdit" class="btn btn-sm btn-primary" @click="form = blank()">فرایند جدید</button>
      </div>
      <p class="inv-muted small">حد پارامترها را مهندسی تعیین می‌کند؛ سامانه هیچ حدی از خود نمی‌گذارد و فرایند بدون حد، صلاحیت کامل نمی‌شود.</p>

      <div v-if="form" class="border rounded p-2 mb-3">
        <div class="row g-2">
          <div class="col-md-2"><label class="form-label small mb-0">کد *</label><input v-model="form.code" :disabled="!!form.id" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">توضیح</label><input v-model="form.description" class="form-control form-control-sm" /></div>
          <div class="col-md-3 d-flex align-items-end"><label class="form-check"><input v-model="form.requiresMachine" class="form-check-input" type="checkbox" /> <span class="form-check-label">دستگاه (کوره/خط) لازم است</span></label></div>
          <div class="col-md-3 d-flex align-items-end"><label class="form-check"><input v-model="form.requiresSurvey" class="form-check-input" type="checkbox" /> <span class="form-check-label">بررسی دما (TUS/SAT) لازم است</span></label></div>
          <div class="col-md-3 d-flex align-items-end"><label class="form-check"><input v-model="form.existingControl" class="form-check-input" type="checkbox" /> <span class="form-check-label">با صلاحیت پرسنلی موجود کنترل می‌شود (NDT)</span></label></div>
          <div class="col-md-3 d-flex align-items-end"><label class="form-check"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> <span class="form-check-label">فعال</span></label></div>
          <div class="col-md-4"><label class="form-label small mb-0">رده ابزار اندازه‌گیری لازم</label>
            <select v-model="form.requiredInstrumentClass" class="form-select form-select-sm"><option :value="null">ندارد</option><option v-for="(t, k) in instrumentClasses" :key="k" :value="Number(k)" :hidden="Number(k) === 99">{{ t }}</option></select></div>
          <div class="col-md-8"><label class="form-label small mb-0">روش اجرایی (سند سامانه مدارک)</label><ProcedurePicker v-model:deliverable-id="form.procedureDeliverableId" :revision-id="null" /></div>
          <div class="col-12">
            <table class="table table-sm mb-1">
              <thead><tr><th>پارامتر</th><th>واحد</th><th>حداقل</th><th>حداکثر</th><th>الزامی</th><th></th></tr></thead>
              <tbody>
                <tr v-if="!form.parameters.length"><td colspan="6" class="inv-muted text-center">پارامتری نیست.</td></tr>
                <tr v-for="(p, i) in form.parameters" :key="i">
                  <td><input v-model="p.name" class="form-control form-control-sm" /></td><td><input v-model="p.unit" class="form-control form-control-sm inv-ltr" style="max-width: 90px" /></td>
                  <td><input v-model="p.min" class="form-control form-control-sm inv-ltr" type="number" step="any" style="max-width: 120px" /></td><td><input v-model="p.max" class="form-control form-control-sm inv-ltr" type="number" step="any" style="max-width: 120px" /></td>
                  <td><input v-model="p.required" class="form-check-input" type="checkbox" /></td><td><button class="btn btn-sm btn-outline-danger" @click="form.parameters.splice(i, 1)">حذف</button></td>
                </tr>
              </tbody>
            </table>
            <button class="btn btn-sm btn-outline-secondary" @click="addParameter">پارامتر جدید</button>
          </div>
          <div class="col-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button></div>
        </div>
      </div>

      <table class="table table-sm">
        <thead><tr><th>کد</th><th>عنوان</th><th>دستگاه</th><th>TUS/SAT</th><th>ابزار</th><th>روش اجرایی</th><th>پارامترها</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-for="t in rows" :key="t.id">
            <td class="inv-ltr">{{ t.code }}</td><td>{{ t.title }}<span v-if="t.existingControl" class="inv-badge info ms-1">صلاحیت پرسنلی</span></td>
            <td>{{ t.requiresMachine ? "بله" : "خیر" }}</td><td>{{ t.requiresSurvey ? "بله" : "خیر" }}</td><td>{{ t.requiredInstrumentClass ? instrumentClasses[t.requiredInstrumentClass] : "-" }}</td>
            <td class="inv-ltr">{{ t.procedureNumber || "-" }}</td>
            <td class="small"><div v-for="p in t.parameters" :key="p.id" class="inv-ltr">{{ p.name }}: {{ p.min ?? "?" }} - {{ p.max ?? "?" }} {{ p.unit }}</div></td>
            <td><span :class="t.isActive ? 'ok' : 'bad'" class="inv-badge">{{ t.isActive ? "فعال" : "غیرفعال" }}</span></td>
            <td><button v-if="canEdit" class="btn btn-sm btn-outline-secondary" @click="edit(t)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
