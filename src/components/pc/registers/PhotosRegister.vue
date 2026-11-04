<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { http, rowsOf } from "@/api/ApiClient";
import { uploadFile } from "@/api/InventoryAPIs";
import { getPhotos, savePhoto, voidPhoto } from "@/api/ProjectRegistersAPIs";
import JDate from "@/components/pc/JDate.vue";
import { fail, ok, today } from "@/composables/inventory";

// Project photo library: the image goes through the Documents upload; a wrong photo is voided with a reason, never deleted.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const urls = ref<Record<string, string>>({});
const file = ref<File | null>(null);
const caption = ref("");
const taken = ref<string | null>(today());
const voiding = ref<any>(null);
const reason = ref("");
const busy = ref(false);

async function thumb(p: any) {
  try {
    const r = await http.get("Documents", { params: { DocumentId: p.documentId }, responseType: "blob" });
    urls.value[p.documentId] = URL.createObjectURL(r.data);
  } catch { /* the picture stays empty */ }
}
async function load() {
  const [d, e] = await getPhotos({ ContractId: props.contractId, PageSize: 100 });
  if (e) return fail(e);
  rows.value = rowsOf(d);
  rows.value.filter((p) => !p.isVoid).forEach(thumb);
}
async function add() {
  if (!file.value) return fail("تصویر را انتخاب کنید.");
  busy.value = true;
  const [id, up] = await uploadFile(file.value, caption.value || "عکس پروژه");
  if (up || !id) { busy.value = false; return fail(up); }
  const [, e] = await savePhoto({ contractId: props.contractId, documentId: id, caption: caption.value, takenDate: taken.value });
  busy.value = false;
  if (e) return fail(e);
  ok("عکس ثبت شد.");
  file.value = null; caption.value = "";
  load();
}
async function doVoid() {
  if (!reason.value.trim()) return fail("دلیل ابطال را بنویسید.");
  const [, e] = await voidPhoto({ id: voiding.value.id, reason: reason.value });
  if (e) return fail(e);
  voiding.value = null; reason.value = "";
  load();
}
onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <div class="row g-2 align-items-end mb-3 d-print-none">
      <div class="col-md-4"><label class="form-label small mb-0">تصویر</label><input accept="image/*" class="form-control form-control-sm" type="file" @change="file = ($event.target as HTMLInputElement).files?.[0] || null" /></div>
      <div class="col-md-4"><label class="form-label small mb-0">شرح تصویر</label><input v-model="caption" class="form-control form-control-sm" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">تاریخ عکس</label><JDate v-model="taken" /></div>
      <div class="col-md-2"><button :disabled="busy" class="btn btn-sm btn-primary w-100" @click="add">افزودن</button></div>
    </div>
    <div class="row g-3">
      <div v-for="p in rows" :key="p.id" class="col-6 col-md-3">
        <div class="card h-100">
          <img v-if="urls[p.documentId] && !p.isVoid" :src="urls[p.documentId]" alt="" class="card-img-top" style="height: 160px; object-fit: cover" />
          <div v-else class="inv-muted text-center p-5">{{ p.isVoid ? "باطل" : "..." }}</div>
          <div class="card-body p-2 small">
            <div>{{ p.caption }}</div><div class="inv-muted">{{ p.takenDateText }} - {{ p.uploadedByName }}</div><div v-if="p.activityCode" class="inv-ltr">{{ p.activityCode }}</div>
            <div v-if="p.isVoid" class="text-danger">باطل: {{ p.voidReason }}</div>
            <button v-else class="btn btn-sm btn-link text-danger p-0 d-print-none" @click="voiding = p">ابطال</button>
          </div>
        </div>
      </div>
      <p v-if="!rows.length" class="inv-muted">عکسی ثبت نشده.</p>
    </div>
    <div v-if="voiding" class="inv-modal-backdrop" @click.self="voiding = null"><div class="inv-modal"><h5>ابطال عکس</h5><input v-model="reason" class="form-control form-control-sm" placeholder="دلیل" /><div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-danger" @click="doVoid">ابطال</button><button class="btn btn-sm btn-light" @click="voiding = null">انصراف</button></div></div></div>
  </div>
</template>
