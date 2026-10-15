<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { deleteLocation, getWarehouses, saveLocation, saveWarehouse } from "@/api/InventoryAPIs";
import { fail, labelCode, ok, openLabels, warehouseKinds } from "@/composables/inventory";

const router = useRouter();
const warehouses = ref<any[]>([]);
const loading = ref(false);
const current = ref<any>(null);
const form = ref<any>(null);
const locationForm = ref<any>(null);
const saving = ref(false);

async function load() {
  loading.value = true;
  const [data, error] = await getWarehouses();
  loading.value = false;
  if (error) return fail(error);
  warehouses.value = data || [];
  if (current.value) current.value = warehouses.value.find((w) => w.id === current.value.id) || null;
}

function add() {
  form.value = { id: null, code: "", title: "", kind: 0, description: "", isActive: true, sepidarCode: "" };
}

function edit(w: any) {
  form.value = { id: w.id, code: w.code, title: w.title, kind: w.kind, description: w.description, isActive: w.isActive, sepidarCode: w.sepidarCode };
}

async function save() {
  saving.value = true;
  const [, error] = await saveWarehouse(form.value);
  saving.value = false;
  if (error) return fail(error);
  ok("انبار ذخیره شد.");
  form.value = null;
  load();
}

function addLocation() {
  locationForm.value = { id: null, warehouseId: current.value.id, code: "", title: "", isActive: true };
}

function editLocation(l: any) {
  locationForm.value = { id: l.id, warehouseId: l.warehouseId, code: l.code, title: l.title, isActive: l.isActive };
}

async function saveLoc() {
  saving.value = true;
  const [, error] = await saveLocation(locationForm.value);
  saving.value = false;
  if (error) return fail(error);
  ok("محل ذخیره شد.");
  locationForm.value = null;
  load();
}

async function removeLocation(l: any) {
  if (!confirm(`محل ${l.code} حذف شود؟`)) return;
  const [, error] = await deleteLocation(l.id);
  if (error) return fail(error);
  ok("حذف شد.");
  load();
}

function printLocationLabels() {
  const w = current.value;
  if (!w || !w.locations.length) return fail("محلی برای چاپ نیست.");
  openLabels(
    w.locations.map((l: any) => ({ code: labelCode.location(w.code, l.code), title: `${w.title} — ${l.code}`, lines: [l.title || ""].filter(Boolean) })),
    router
  );
}

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="row g-3">
      <div class="col-lg-6">
        <div class="inv-card">
          <div class="inv-toolbar">
            <h1 class="inv-title">انبارها</h1>
            <button class="btn btn-sm btn-primary" @click="add">انبار جدید</button>
          </div>
          <table class="table table-sm table-hover">
            <thead><tr><th>کد</th><th>عنوان</th><th>نوع</th><th>کد سپیدار</th><th>محل‌ها</th><th></th></tr></thead>
            <tbody>
              <tr v-if="loading"><td class="text-center" colspan="6">در حال بارگذاری...</td></tr>
              <tr v-else-if="!warehouses.length"><td class="text-center inv-muted" colspan="6">انباری تعریف نشده است.</td></tr>
              <tr
                v-for="w in warehouses"
                :key="w.id"
                :class="{ 'table-primary': current && current.id === w.id, 'opacity-50': !w.isActive }"
                role="button"
                @click="current = w">
                <td class="inv-ltr">{{ w.code }}</td>
                <td>{{ w.title }}</td>
                <td><span class="inv-badge">{{ warehouseKinds[w.kind] }}</span></td>
                <td class="inv-ltr">{{ w.sepidarCode }}</td>
                <td>{{ w.locations.length }}</td>
                <td><button class="btn btn-sm btn-link p-0" @click.stop="edit(w)">ویرایش</button></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div class="col-lg-6">
        <div class="inv-card">
          <div v-if="!current" class="inv-muted text-center py-4">یک انبار را انتخاب کنید تا محل‌های آن (قفسه، ردیف، پالت) را ببینید.</div>
          <template v-else>
            <div class="inv-toolbar">
              <h2 class="inv-title">محل‌های {{ current.title }}</h2>
              <div class="d-flex gap-2">
                <button class="btn btn-sm btn-outline-secondary" @click="printLocationLabels">چاپ برچسب محل‌ها</button>
                <button class="btn btn-sm btn-primary" @click="addLocation">محل جدید</button>
              </div>
            </div>
            <table class="table table-sm table-hover">
              <thead><tr><th>کد محل</th><th>عنوان</th><th></th></tr></thead>
              <tbody>
                <tr v-if="!current.locations.length"><td class="text-center inv-muted" colspan="3">محلی تعریف نشده؛ کالا بدون محل نگهداری می‌شود.</td></tr>
                <tr v-for="l in current.locations" :key="l.id" :class="{ 'opacity-50': !l.isActive }">
                  <td class="inv-ltr">{{ l.code }}</td>
                  <td>{{ l.title }}</td>
                  <td class="text-nowrap">
                    <button class="btn btn-sm btn-link p-0 ms-2" @click="editLocation(l)">ویرایش</button>
                    <button class="btn btn-sm btn-link text-danger p-0" @click="removeLocation(l)">حذف</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </template>
        </div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">{{ form.id ? "ویرایش انبار" : "انبار جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small">کد *</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-8"><label class="form-label small">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-6">
            <label class="form-label small">نوع انبار</label>
            <select v-model="form.kind" class="form-select form-select-sm">
              <option v-for="(t, k) in warehouseKinds" :key="k" :value="Number(k)">{{ t }}</option>
            </select>
          </div>
          <div class="col-md-6"><label class="form-label small">کد انبار در سپیدار</label><input v-model="form.sepidarCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-12"><label class="form-label small">توضیح</label><textarea v-model="form.description" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-12 form-check ms-2"><input id="whActive" v-model="form.isActive" class="form-check-input" type="checkbox" /><label class="form-check-label small" for="whActive">فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="saving" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>
    </div>

    <div v-if="locationForm" class="inv-modal-backdrop" @click.self="locationForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">{{ locationForm.id ? "ویرایش محل" : "محل جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small">کد محل *</label><input v-model="locationForm.code" class="form-control form-control-sm inv-ltr" placeholder="A-01-03" /></div>
          <div class="col-md-8"><label class="form-label small">عنوان</label><input v-model="locationForm.title" class="form-control form-control-sm" /></div>
          <div class="col-12 form-check ms-2"><input id="locActive" v-model="locationForm.isActive" class="form-check-input" type="checkbox" /><label class="form-check-label small" for="locActive">فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="locationForm = null">انصراف</button>
          <button :disabled="saving" class="btn btn-sm btn-primary" @click="saveLoc">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
