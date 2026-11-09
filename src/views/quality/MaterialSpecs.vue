<script setup lang="ts">
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import { getMaterialSpec, getMaterialSpecs, materialSpecAction, saveMaterialSpec } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { certificateTypes, certValueKinds, matSpecStatuses, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Package C: the material specification (grade + standard) against which a 3.1/3.2 certificate is judged. Engineering writes the chemistry and mechanical limits and the linked coded items; QA/QC approves, never the
// author. A change is a new version (the approved one is replaced only when the new one is approved). The certificate type a coded item needs is derived from here and from its BOM line.
const filters = ref({ Status: null as number | null, Contains: "" });
const list = usePaged(getMaterialSpecs, filters);
const form = ref<any>(null);
const items = ref<any[]>([]);
const addItem = ref<any>(null);
const busy = ref(false);
const dialog = ref<null | { id: number; action: string; title: string }>(null);
const canWrite = hasRole(QualityRoles.engineering);

const blank = () => ({ id: null, newVersionOfId: null as number | null, grade: "", standard: "", title: "", requiredCertificateType: 2, reason: "", limits: [] as any[], itemIds: [] as number[], rowVersion: null as string | null });
async function edit(id: number, newVersion = false) {
  const [s, error] = await getMaterialSpec(id);
  if (error) return fail(error);
  form.value = {
    id: newVersion ? null : s.id, newVersionOfId: newVersion ? s.id : null, grade: s.grade, standard: s.standard, title: s.title || "", requiredCertificateType: s.requiredCertificateType, reason: newVersion ? "" : s.reason || "",
    limits: s.limits.map((l: any) => ({ kind: l.kind, name: l.name, unit: l.unit, min: l.min, max: l.max, required: l.required })), itemIds: s.items.map((i: any) => i.itemId), rowVersion: newVersion ? null : s.rowVersion,
  };
  items.value = s.items.map((i: any) => ({ id: i.itemId, code: i.itemCode, title: i.itemTitle }));
}
const addLimit = (kind: number) => form.value.limits.push({ kind, name: "", unit: kind === 1 ? "%" : "MPa", min: null, max: null, required: true });
function attachItem() {
  if (!addItem.value) return;
  if (!form.value.itemIds.includes(addItem.value.id)) {
    form.value.itemIds.push(addItem.value.id);
    items.value.push({ id: addItem.value.id, code: addItem.value.code, title: addItem.value.title });
  }
  addItem.value = null;
}
function detachItem(id: number) {
  form.value.itemIds = form.value.itemIds.filter((x: number) => x !== id);
  items.value = items.value.filter((x) => x.id !== id);
}
async function save() {
  const f = form.value;
  const nn = (v: any) => (v === "" || v === undefined ? null : v);
  busy.value = true;
  const [, error] = await saveMaterialSpec({ ...f, limits: f.limits.map((l: any) => ({ ...l, min: nn(l.min), max: nn(l.max) })) });
  busy.value = false;
  if (error) return fail(error);
  ok("مشخصات ماده ذخیره شد.");
  form.value = null;
  list.load();
}
async function act(id: number, action: string, note?: string) {
  busy.value = true;
  const [, error] = await materialSpecAction(id, action, note);
  busy.value = false;
  dialog.value = null;
  if (error) return fail(error);
  ok("انجام شد.");
  list.load();
}
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">مشخصات مواد (گرید و استاندارد)</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="(form = blank()), (items = [])">مشخصات جدید</button>
      </div>
      <div v-if="form" class="border rounded p-2 mb-3">
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small mb-0">گرید *</label><input v-model="form.grade" :disabled="!!form.newVersionOfId" class="form-control form-control-sm inv-ltr" placeholder="S355J2" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">استاندارد *</label><input v-model="form.standard" :disabled="!!form.newVersionOfId" class="form-control form-control-sm inv-ltr" placeholder="EN 10025-2" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">گواهی لازم</label><select v-model="form.requiredCertificateType" class="form-select form-select-sm"><option v-for="(t, k) in certificateTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">دلیل ویرایش / نسخه جدید</label><input v-model="form.reason" class="form-control form-control-sm" /></div>
          <div class="col-12">
            <table class="table table-sm mb-1">
              <thead><tr><th>نوع</th><th>پارامتر</th><th>واحد</th><th>حداقل</th><th>حداکثر</th><th>الزامی</th><th></th></tr></thead>
              <tbody>
                <tr v-if="!form.limits.length"><td colspan="7" class="inv-muted text-center">حدی ثبت نشده است.</td></tr>
                <tr v-for="(l, i) in form.limits" :key="i">
                  <td>{{ certValueKinds[l.kind] }}</td><td><input v-model="l.name" class="form-control form-control-sm inv-ltr" /></td><td><input v-model="l.unit" class="form-control form-control-sm inv-ltr" style="max-width: 90px" /></td>
                  <td><input v-model="l.min" class="form-control form-control-sm inv-ltr" type="number" step="any" style="max-width: 120px" /></td><td><input v-model="l.max" class="form-control form-control-sm inv-ltr" type="number" step="any" style="max-width: 120px" /></td>
                  <td><input v-model="l.required" class="form-check-input" type="checkbox" /></td><td><button class="btn btn-sm btn-outline-danger" @click="form.limits.splice(i, 1)">حذف</button></td>
                </tr>
              </tbody>
            </table>
            <div class="d-flex gap-2"><button class="btn btn-sm btn-outline-secondary" @click="addLimit(1)">حد شیمیایی</button><button class="btn btn-sm btn-outline-secondary" @click="addLimit(2)">حد مکانیکی</button></div>
          </div>
          <div class="col-md-5"><label class="form-label small mb-0">کالاهای کددار این مشخصات</label>
            <div class="d-flex gap-1"><div class="flex-grow-1"><ItemPicker v-model="addItem" :active-only="false" /></div><button class="btn btn-sm btn-outline-secondary" @click="attachItem">افزودن</button></div></div>
          <div class="col-md-7 d-flex flex-wrap gap-2 align-items-end"><span v-for="i in items" :key="i.id" class="inv-badge info"><span class="inv-ltr">{{ i.code }}</span> {{ i.title }} <a href="#" @click.prevent="detachItem(i.id)">×</a></span></div>
          <div class="col-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره پیش‌نویس</button><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button></div>
        </div>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="گرید یا استاندارد" @keydown.enter="list.search()" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in matSpecStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <table class="table table-sm">
        <thead><tr><th>گرید / استاندارد</th><th>نسخه</th><th>گواهی لازم</th><th>حدود</th><th>وضعیت</th><th>تهیه / تایید</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">مشخصاتی نیست.</td></tr>
          <tr v-for="s in list.rows.value" :key="s.id">
            <td class="inv-ltr">{{ s.grade }} / {{ s.standard }}<div class="small inv-muted">{{ s.title }}</div></td><td>{{ s.version }}</td><td>{{ s.requiredCertificateTypeText }}</td>
            <td class="small"><div v-for="(l, i) in s.limits" :key="i" class="inv-ltr">{{ l.name }}: {{ l.min ?? "-" }} .. {{ l.max ?? "-" }} {{ l.unit }}</div></td>
            <td><span :class="{ ok: s.status === 2, warn: s.status <= 1, info: s.status === 3, bad: s.status === 4 }" class="inv-badge">{{ matSpecStatuses[s.status] }}</span><div v-if="s.voidReason" class="small text-danger">{{ s.voidReason }}</div></td>
            <td class="small">{{ s.creatorName }}<br />{{ s.approvedByName }} {{ s.approvedAt ? jalali(s.approvedAt) : "" }}</td>
            <td class="text-nowrap">
              <button v-if="(s.actions || []).includes('edit')" class="btn btn-sm btn-outline-secondary me-1" @click="edit(s.id)">ویرایش</button>
              <button v-if="(s.actions || []).includes('submit')" :disabled="busy" class="btn btn-sm btn-outline-primary me-1" @click="act(s.id, 'submit')">ارسال</button>
              <button v-if="(s.actions || []).includes('approve')" :disabled="busy" class="btn btn-sm btn-success me-1" @click="act(s.id, 'approve')">تایید</button>
              <button v-if="(s.actions || []).includes('return')" :disabled="busy" class="btn btn-sm btn-outline-warning me-1" @click="dialog = { id: s.id, action: 'return', title: 'بازگشت مشخصات برای اصلاح' }">بازگشت</button>
              <button v-if="(s.actions || []).includes('new-version')" class="btn btn-sm btn-outline-secondary me-1" @click="edit(s.id, true)">نسخه جدید</button>
              <button v-if="(s.actions || []).includes('void')" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dialog = { id: s.id, action: 'void', title: 'ابطال مشخصات' }">ابطال</button>
            </td>
          </tr>
        </tbody>
      </table>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
    <ReasonDialog v-if="dialog" :title="dialog.title" :busy="busy" @confirm="(t: string) => act(dialog!.id, dialog!.action, t)" @close="dialog = null" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
