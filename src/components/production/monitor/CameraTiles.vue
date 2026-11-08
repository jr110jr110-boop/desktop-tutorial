<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from "vue";
import { cameraSnapshotUrl, getCameras, saveCamera } from "@/api/ShopAPIs";
import { getProductionMachines } from "@/api/ProductionAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { ProductionRoles, hasRole } from "@/composables/roles";

// Live view of the shop cameras: the server fetches every picture from the camera on the LAN; the tiles refresh it every second while the page is open (the browser never reaches the camera).
const cameras = ref<any[]>([]);
const pictures = ref<Record<number, { url: string | null; error: string | null }>>({});
const machines = ref<any[]>([]);
const canEdit = hasRole(ProductionRoles.masterEditors);
const f = ref<any>(null);
const busy = ref(false);
let timer: any = null;
let running = false;

async function load() {
  const [d, error] = await getCameras();
  if (error) return fail(error);
  cameras.value = d;
}
async function refresh() {
  if (running) return;
  running = true;
  for (const c of cameras.value.filter((x) => x.isActive)) {
    const [url, error] = await cameraSnapshotUrl(c.id);
    const old = pictures.value[c.id]?.url;
    pictures.value = { ...pictures.value, [c.id]: { url: url ?? old ?? null, error } };
    if (url && old) URL.revokeObjectURL(old);
  }
  running = false;
}
async function openEditor(c?: any) {
  if (!machines.value.length) {
    const [m] = await getProductionMachines({ PageSize: 500 });
    machines.value = rowsOf(camelize(m));
  }
  f.value = c ? { ...c } : { title: "", machineId: null, snapshotUrl: "", streamUrl: "", credentialKey: "", isActive: true };
}
async function save() {
  busy.value = true;
  const x = f.value;
  const [, error] = await saveCamera({ ...x, streamUrl: x.streamUrl?.trim() || null, credentialKey: x.credentialKey?.trim() || null });
  busy.value = false;
  if (error) return fail(error);
  ok("دوربین ذخیره شد.");
  f.value = null;
  await load();
}
onMounted(async () => {
  await load();
  refresh();
  timer = setInterval(refresh, 1000);
});
onBeforeUnmount(() => {
  clearInterval(timer);
  Object.values(pictures.value).forEach((p) => p.url && URL.revokeObjectURL(p.url));
});
</script>

<template>
  <div>
    <div class="inv-toolbar"><span class="inv-muted small">تصویر هر دوربین هر ثانیه از راه سرور به‌روز می‌شود.</span><button v-if="canEdit" class="btn btn-sm btn-primary" @click="openEditor()">دوربین تازه</button></div>
    <p v-if="!cameras.length" class="inv-muted">دوربینی تعریف نشده است.</p>
    <div class="cam-grid">
      <div v-for="c in cameras" :key="c.id" class="cam-tile">
        <div class="d-flex justify-content-between small mb-1"><b>{{ c.title }}</b><span>{{ c.machineTitle || c.workCenterCode || "" }}<button v-if="canEdit" class="btn btn-link btn-sm p-0 ms-2" @click="openEditor(c)">ویرایش</button></span></div>
        <img v-if="pictures[c.id]?.url" :alt="c.title" :src="pictures[c.id].url" class="cam-img" />
        <div v-else class="cam-empty">{{ c.isActive ? pictures[c.id]?.error || "در حال دریافت تصویر..." : "غیرفعال" }}</div>
        <div v-if="pictures[c.id]?.error && pictures[c.id]?.url" class="small text-danger">{{ pictures[c.id].error }}</div>
      </div>
    </div>

    <div v-if="f" class="inv-modal-backdrop" @click.self="f = null">
      <div class="inv-modal" style="max-width: 600px">
        <h3 class="inv-title mb-3">دوربین</h3>
        <div class="row g-2 small">
          <div class="col-md-6"><label class="form-label mb-0">عنوان</label><input v-model="f.title" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label mb-0">ماشین</label><select v-model="f.machineId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="m in machines" :key="m.machineId" :value="m.machineId">{{ m.title }}</option></select></div>
          <div class="col-md-12"><label class="form-label mb-0">نشانی تصویر (JPEG) در شبکه داخلی</label><input v-model="f.snapshotUrl" class="form-control form-control-sm inv-ltr" placeholder="http://192.168.10.64/ISAPI/Streaming/channels/101/picture" /></div>
          <div class="col-md-12"><label class="form-label mb-0">نشانی پخش MJPEG (اختیاری)</label><input v-model="f.streamUrl" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label mb-0">نام کلید اعتبارنامه</label><input v-model="f.credentialKey" class="form-control form-control-sm inv-ltr" placeholder="cam-hall" /><div class="inv-muted">رمز دوربین در تنظیمات سرور ثبت می‌شود.</div></div>
          <div class="col-md-4 form-check ms-2 mt-4"><input id="cam-act" v-model="f.isActive" class="form-check-input" type="checkbox" /><label class="form-check-label" for="cam-act">فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="f = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.cam-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 12px; }
.cam-tile { border: 1px solid #ddd; border-radius: 8px; padding: 8px; }
.cam-img { width: 100%; border-radius: 6px; background: #111; aspect-ratio: 16 / 9; object-fit: contain; }
.cam-empty { aspect-ratio: 16 / 9; display: flex; align-items: center; justify-content: center; background: #222; color: #bbb; border-radius: 6px; font-size: 13px; }
</style>
