<script setup lang="ts">
// Picks an EDMS procedure document and one of its in-force revisions (Released or As-built). The qualification of a special process names the document AND the revision; when the revision is replaced the
// qualification must be rewritten, so the choice is kept as a pair.
import { ref, watch } from "vue";
import { getDocHistory } from "@/api/EdmsAPIs";
import { getDeliverables } from "@/api/ProjectControlAPIs";
import { fail } from "@/composables/inventory";

const props = defineProps({
  deliverableId: { type: Number, default: null },
  revisionId: { type: Number, default: null },
  disabled: Boolean,
});
const emit = defineEmits<{ (e: "update:deliverableId", v: number | null): void; (e: "update:revisionId", v: number | null): void }>();
const text = ref("");
const found = ref<any[]>([]);
const revisions = ref<any[]>([]);
const label = ref("");

async function search() {
  if (text.value.trim().length < 2) return fail("دست‌کم دو نویسه از شماره یا عنوان روش اجرایی را بنویسید.");
  const [data, error] = await getDeliverables({ Contains: text.value.trim(), PageSize: 30 });
  if (error) return fail(error);
  found.value = Array.isArray(data) ? data : data?.data || [];
}
async function pick(d: any) {
  emit("update:deliverableId", d.id);
  emit("update:revisionId", null);
  label.value = d.documentNumber + " - " + d.title;
  found.value = [];
  await loadRevisions(d.id);
}
async function loadRevisions(id: number) {
  const [data, error] = await getDocHistory(id);
  if (error) return fail(error);
  revisions.value = (data?.revisions || []).filter((r: any) => r.status === 4 || r.status === 7);
  if (!label.value) label.value = (data?.documentNumber || "") + " - " + (data?.title || "");
}
watch(() => props.deliverableId, (id) => { if (id && !revisions.value.length) loadRevisions(id); }, { immediate: true });
</script>

<template>
  <div>
    <div v-if="deliverableId" class="small mb-1"><b class="inv-ltr">{{ label || "#" + deliverableId }}</b>
      <button v-if="!disabled" class="btn btn-sm btn-link p-0 ms-2" @click="emit('update:deliverableId', null), emit('update:revisionId', null), (revisions = []), (label = '')">تغییر</button></div>
    <div v-else-if="!disabled" class="d-flex gap-1">
      <input v-model="text" class="form-control form-control-sm" placeholder="شماره یا عنوان روش اجرایی" @keydown.enter="search" />
      <button class="btn btn-sm btn-outline-secondary" @click="search">جستجو</button>
    </div>
    <div v-if="found.length" class="border rounded mt-1" style="max-height: 160px; overflow: auto">
      <div v-for="d in found" :key="d.id" role="button" class="px-2 py-1 border-bottom small" @click="pick(d)"><span class="inv-ltr">{{ d.documentNumber }}</span> {{ d.title }}</div>
    </div>
    <select v-if="deliverableId" :disabled="disabled" :value="revisionId" class="form-select form-select-sm mt-1" @change="emit('update:revisionId', Number(($event.target as HTMLSelectElement).value) || null)">
      <option :value="null">ویرایش منتشرشده (معتبر)</option>
      <option v-for="r in revisions" :key="r.id" :value="r.id">ویرایش {{ r.code }}</option>
    </select>
    <div v-if="deliverableId && !revisions.length" class="small text-warning">ویرایش منتشرشده‌ای برای این سند نیست.</div>
  </div>
</template>
