<script setup lang="ts">
// The structured list of the resources pre-assigned to a project (priority 4d-4): resource, quantity and a note, beside the free text of the charter.
// Once a list exists the free text is read only (the list is the reference); an empty list gives the text back. The server checks every rule again: no duplicate, an active resource, a quantity above zero, a note of at most 500 characters.
import { computed, onMounted, ref } from "vue";
import { getResources } from "@/api/ResourceAPIs";
import type { Resource } from "@/api/ResourceAPIs";
import { fail } from "@/composables/inventory";
import { resourceLabel, resourceUnits } from "@/composables/resources";

interface Row { resourceId: number | null; resourceCode?: string; resourceTitle?: string; quantity: number; note: string | null }
const props = defineProps({ modelValue: { type: Array as () => Row[], default: () => [] }, editable: Boolean });
const emit = defineEmits<{ (e: "update:modelValue", v: Row[]): void }>();

const pool = ref<Resource[]>([]);
onMounted(async () => {
  if (!props.editable) return;
  // the whole pool (never paged): the active ones can be chosen, and an inactive one that is already on the list stays visible and marked
  const [list, error] = await getResources({});
  if (error) return fail(error);
  pool.value = list;
});

const rows = computed(() => props.modelValue);
const unitOf = (id: number | null) => resourceUnits[pool.value.find((r) => r.id === id)?.unit ?? 0] || "";
const onList = (id: number) => rows.value.some((r) => r.resourceId === id);
const choices = computed(() => pool.value.filter((p) => p.isActive || onList(p.id)));
const optionLabel = (p: Resource) => resourceLabel(p) + (p.isActive ? "" : " (غیرفعال)");
const inactiveOf = (id: number | null) => pool.value.some((p) => p.id === id && !p.isActive);
const taken = (id: number | null, index: number) => rows.value.some((r, i) => i !== index && r.resourceId === id && id !== null);
function set(index: number, patch: Partial<Row>) {
  emit("update:modelValue", rows.value.map((r, i) => (i === index ? { ...r, ...patch } : r)));
}
const add = () => emit("update:modelValue", [...rows.value, { resourceId: null, quantity: 1, note: null }]);
const remove = (index: number) => emit("update:modelValue", rows.value.filter((_, i) => i !== index));
const problem = computed(() => {
  for (const r of rows.value) {
    if (!r.resourceId) return "برای هر ردیف منبع را انتخاب کنید.";
    if (!(Number(r.quantity) > 0)) return "مقدار هر ردیف باید بیشتر از صفر باشد.";
    if ((r.note || "").length > 500) return "توضیح هر ردیف حداکثر ۵۰۰ نویسه است.";
  }
  if (new Set(rows.value.map((r) => r.resourceId)).size !== rows.value.length) return "یک منبع بیش از یک بار در فهرست آمده است.";
  return null;
});
defineExpose({ problem });
</script>

<template>
  <div class="mt-2">
    <div class="fw-bold small mb-1">فهرست منابع پیش‌تعیین‌شده (مرجع)</div>
    <div v-if="!rows.length && !editable" class="inv-muted small">فهرستی ثبت نشده است؛ متن آزاد بالا معتبر است.</div>
    <div class="table-responsive" v-if="rows.length">
      <table class="table table-sm align-middle mb-1">
        <thead><tr><th>منبع</th><th style="width: 140px">مقدار</th><th>توضیح</th><th v-if="editable" class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-for="(r, i) in rows" :key="i">
            <template v-if="editable">
              <td>
                <select :value="r.resourceId" class="form-select form-select-sm" aria-label="منبع" @change="set(i, { resourceId: Number(($event.target as HTMLSelectElement).value) || null })">
                  <option :value="null">انتخاب منبع</option>
                  <option v-for="p in choices" :key="p.id" :value="p.id" :disabled="taken(p.id, i) || (!p.isActive && r.resourceId !== p.id)">{{ optionLabel(p) }}</option>
                </select>
              </td>
              <td>
                <div class="input-group input-group-sm">
                  <input :value="r.quantity" type="number" min="0" step="any" class="form-control" aria-label="مقدار" @input="set(i, { quantity: Number(($event.target as HTMLInputElement).value) })" />
                  <span class="input-group-text">{{ unitOf(r.resourceId) }}</span>
                </div>
              </td>
              <td><input :value="r.note" maxlength="500" class="form-control form-control-sm" aria-label="توضیح" @input="set(i, { note: ($event.target as HTMLInputElement).value })" /></td>
              <td class="d-print-none"><button type="button" class="btn btn-sm btn-outline-danger" @click="remove(i)">حذف</button></td>
            </template>
            <template v-else>
              <td><span class="inv-ltr">{{ r.resourceCode }}</span> {{ r.resourceTitle }}<span v-if="inactiveOf(r.resourceId)" class="inv-badge ms-1">غیرفعال</span></td>
              <td>{{ r.quantity }}</td>
              <td>{{ r.note || "—" }}</td>
            </template>
          </tr>
        </tbody>
      </table>
    </div>
    <button v-if="editable" type="button" class="btn btn-sm btn-outline-primary d-print-none" @click="add">افزودن منبع</button>
    <p v-if="editable && problem" class="small text-danger mb-0 mt-1">{{ problem }}</p>
    <p v-if="editable && rows.length" class="small inv-muted mb-0 mt-1">تا وقتی فهرست دارد، متن آزاد بالا فقط‌خواندنی است. برای ویرایش دوباره‌ی متن، فهرست را خالی کنید و ذخیره کنید.</p>
  </div>
</template>
