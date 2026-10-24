<script setup lang="ts">
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import { getPositions, savePosition } from "@/api/OfficeAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import EmployeePick from "./EmployeePick.vue";
import UserPick from "./UserPick.vue";
import { loadUnits, resetPositions } from "./lookups";

// Positions: letters and approvals go to positions, not people. Holder rules (HR only; the CEO only for his own or a vacant position; never yourself) are enforced by the API and shown as its message.
const props = defineProps({ canWrite: Boolean });
const emit = defineEmits(["changed"]);
const filters = ref({ UnitId: null as number | null, Contains: "" });
const list = usePagedList(getPositions, filters);
const units = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);

const edit = (p?: any) =>
  (form.value = p
    ? { id: p.id, title: p.title, unitId: p.unitId, userId: p.userId, userName: p.userName, employeeId: p.employeeId, isManager: p.isManager, canSign: p.canSign, isActive: p.isActive }
    : { id: null, title: "", unitId: null, userId: null, userName: "", employeeId: null, isManager: false, canSign: false, isActive: true });
async function save() {
  const f = form.value;
  busy.value = true;
  const [, error] = await savePosition({ id: f.id || null, title: f.title, unitId: f.unitId, userId: f.userId || null, employeeId: f.employeeId || null, isManager: f.isManager, canSign: f.canSign, isActive: f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("سمت ذخیره شد.");
  form.value = null;
  resetPositions();
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
      <select v-model="filters.UnitId" class="form-select form-select-sm"><option :value="null">همه واحدها</option><option v-for="u in units" :key="u.id" :value="u.id">{{ u.title }}</option></select>
      <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="جستجو در عنوان سمت" @keyup.enter="list.search" />
      <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      <button v-if="props.canWrite" class="btn btn-sm btn-primary" @click="edit()">سمت جدید</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>سمت</th><th>واحد</th><th>دارنده</th><th>امضا</th><th>ویژگی</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">سمتی نیست.</td></tr>
          <tr v-for="p in list.rows.value" :key="p.id">
            <td>{{ p.title }}</td><td>{{ p.unitTitle }}</td><td>{{ p.userName || "خالی" }}</td>
            <td><span v-if="p.canSign" :class="p.holderHasSignature ? 'ok' : 'warn'" class="inv-badge">{{ p.holderHasSignature ? "تصویر امضا دارد" : "بدون تصویر امضا" }}</span></td>
            <td><span v-if="p.isManager" class="inv-badge info">مدیر واحد</span> <span v-if="p.canSign" class="inv-badge ok">حق امضا</span></td>
            <td><span :class="p.isActive ? 'ok' : 'bad'" class="inv-badge">{{ p.isActive ? "فعال" : "غیرفعال" }}</span></td>
            <td><button v-if="props.canWrite" class="btn btn-sm btn-outline-primary" @click="edit(p)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش سمت" : "سمت جدید" }}</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">عنوان سمت</label><input v-model="form.title" class="form-control form-control-sm" placeholder="مثلا مدیر برنامه‌ریزی" /></div>
          <div class="col-12">
            <label class="form-label small mb-0">واحد</label>
            <select v-model="form.unitId" class="form-select form-select-sm"><option :value="null">انتخاب واحد</option><option v-for="u in units.filter((x) => x.isActive || x.id === form.unitId)" :key="u.id" :value="u.id">{{ u.title }}</option></select>
          </div>
          <div class="col-12">
            <label class="form-label small mb-0">دارنده سمت (کاربر)</label>
            <UserPick v-model="form.userId" :fallback-text="form.userName" />
            <div class="inv-muted small">تعیین یا تغییر دارنده فقط توسط مسئول منابع انسانی (مدیرعامل فقط برای سمت خودش یا سمت خالی) و هرگز برای خود شخص ممکن است. برای خالی کردن، متن کادر را پاک کنید.</div>
          </div>
          <div class="col-12"><label class="form-label small mb-0">کارمند مرتبط</label><EmployeePick v-model="form.employeeId" /></div>
          <div class="col-4"><label class="small d-flex align-items-center gap-1"><input v-model="form.isManager" type="checkbox" class="form-check-input mt-0" />مدیر واحد</label></div>
          <div class="col-4"><label class="small d-flex align-items-center gap-1"><input v-model="form.canSign" type="checkbox" class="form-check-input mt-0" />حق امضا</label></div>
          <div class="col-4"><label class="small d-flex align-items-center gap-1"><input v-model="form.isActive" type="checkbox" class="form-check-input mt-0" />فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>
