<script setup lang="ts">
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import { getUnits, saveUnit } from "@/api/OfficeAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { loadUnits } from "./lookups";

// Units of the org chart: code, title, parent, active (no delete in the API; deactivate instead).
const props = defineProps({ canWrite: Boolean });
const emit = defineEmits(["changed"]);
const filters = ref({ Contains: "" });
const list = usePagedList(getUnits, filters);
const units = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);

const edit = (u?: any) => (form.value = u ? { id: u.id, code: u.code, title: u.title, parentId: u.parentId, isActive: u.isActive } : { id: null, code: "", title: "", parentId: null, isActive: true });
async function save() {
  busy.value = true;
  const [, error] = await saveUnit({ ...form.value, id: form.value.id || null });
  busy.value = false;
  if (error) return fail(error);
  ok("واحد ذخیره شد.");
  form.value = null;
  units.value = await loadUnits();
  list.load();
  emit("changed");
}
onMounted(async () => {
  units.value = await loadUnits();
  list.load();
});
</script>

<template>
  <div>
    <div class="inv-filters mb-2">
      <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="جستجو در عنوان یا کد" @keyup.enter="list.search" />
      <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      <button v-if="props.canWrite" class="btn btn-sm btn-primary" @click="edit()">واحد جدید</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>کد</th><th>عنوان</th><th>واحد بالادست</th><th>مدیر واحد</th><th>تعداد سمت</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">واحدی نیست.</td></tr>
          <tr v-for="u in list.rows.value" :key="u.id">
            <td class="inv-ltr">{{ u.code }}</td><td>{{ u.title }}</td><td>{{ u.parentTitle }}</td><td>{{ u.managerPositionTitle }}</td><td>{{ u.positionCount }}</td>
            <td><span :class="u.isActive ? 'ok' : 'bad'" class="inv-badge">{{ u.isActive ? "فعال" : "غیرفعال" }}</span></td>
            <td><button v-if="props.canWrite" class="btn btn-sm btn-outline-primary" @click="edit(u)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش واحد" : "واحد جدید" }}</h2>
        <div class="row g-2">
          <div class="col-4"><label class="form-label small mb-0">کد</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-8"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-12">
            <label class="form-label small mb-0">واحد بالادست</label>
            <select v-model="form.parentId" class="form-select form-select-sm"><option :value="null">بدون بالادست</option><option v-for="u in units.filter((x) => x.id !== form.id)" :key="u.id" :value="u.id">{{ u.title }}</option></select>
          </div>
          <div class="col-12"><label class="small d-flex align-items-center gap-1"><input v-model="form.isActive" type="checkbox" class="form-check-input mt-0" />فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>
