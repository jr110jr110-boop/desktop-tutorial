<script setup lang="ts">
// Clock devices. The API key (48 hex characters) is shown once in a modal after creating a device or rotating its key; it is never stored by the page.
import { onBeforeUnmount, onMounted, ref } from "vue";
import { getDevices, saveDevice } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";
import { rowsOf } from "./attCommon";

const devices = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);
const secret = ref<{ code: string; key: string } | null>(null);
const copied = ref(false);
const rotating = ref<any>(null);

async function load() {
  const [d, error] = await getDevices();
  if (error) return fail(error);
  devices.value = rowsOf(d);
}
const edit = (d: any | null) => (form.value = d ? { id: d.id, code: d.code, title: d.title, location: d.location || "", isActive: !!d.isActive } : { id: null, code: "", title: "", location: "", isActive: true });
async function send(dto: any) {
  busy.value = true;
  const [d, error] = await saveDevice(dto);
  busy.value = false;
  if (error) return fail(error);
  if (d?.apiKey) {
    secret.value = { code: d.device?.code || dto.code, key: d.apiKey };
    copied.value = false;
  } else ok("دستگاه ذخیره شد.");
  form.value = null;
  rotating.value = null;
  load();
}
function save() {
  const f = form.value;
  if (!f.code.trim()) return fail("کد دستگاه را وارد کنید.");
  if (!f.title.trim()) return fail("عنوان دستگاه را وارد کنید.");
  return send({ id: f.id, code: f.code.trim(), title: f.title.trim(), location: f.location.trim(), isActive: f.isActive, rotateKey: false });
}
const rotate = () => send({ id: rotating.value.id, code: rotating.value.code, title: rotating.value.title, location: rotating.value.location || "", isActive: rotating.value.isActive, rotateKey: true });

async function copy() {
  if (!secret.value) return;
  try {
    await navigator.clipboard.writeText(secret.value.key);
    copied.value = true;
  } catch {
    const box = document.getElementById("att-device-key") as HTMLInputElement | null;
    box?.select();
    copied.value = document.execCommand("copy");
    if (!copied.value) fail("کپی انجام نشد؛ کلید را با دست انتخاب و کپی کنید.");
  }
}
const closeSecret = () => { secret.value = null; copied.value = false; };
onMounted(load);
onBeforeUnmount(closeSecret);
</script>

<template>
  <div>
    <div class="inv-toolbar"><span class="inv-muted small">دستگاه‌های تردد در شبکه داخلی با کد و کلید خود ترددها را ارسال می‌کنند. از کلید فقط رمز یک‌طرفه نگهداری می‌شود.</span><button class="btn btn-sm btn-primary" @click="edit(null)">دستگاه جدید</button></div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>کد</th><th>عنوان</th><th>محل</th><th>آخرین ارسال</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!devices.length"><td colspan="6" class="text-center inv-muted">دستگاهی تعریف نشده است.</td></tr>
          <tr v-for="d in devices" :key="d.id">
            <td class="inv-ltr">{{ d.code }}</td><td>{{ d.title }}</td><td>{{ d.location }}</td><td class="inv-ltr">{{ d.lastPushAtText || "-" }}</td>
            <td><span :class="d.isActive ? 'ok' : 'warn'" class="inv-badge">{{ d.isActive ? "فعال" : "غیرفعال" }}</span></td>
            <td class="text-nowrap"><button class="btn btn-sm btn-outline-secondary me-1" @click="edit(d)">ویرایش</button><button class="btn btn-sm btn-outline-danger" @click="rotating = d">کلید جدید</button></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش دستگاه" : "دستگاه جدید" }}</h2>
        <p v-if="!form.id" class="inv-muted small">پس از ذخیره، کلید دستگاه یک بار نمایش داده می‌شود.</p>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">کد دستگاه *</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" placeholder="مثلا دستگاه ورودی اصلی" /></div>
          <div class="col-12"><label class="form-label small mb-0">محل نصب</label><input v-model="form.location" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="small"><input v-model="form.isActive" type="checkbox" class="form-check-input me-1" />فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>

    <div v-if="rotating" class="inv-modal-backdrop" @click.self="rotating = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">صدور کلید جدید</h2>
        <p class="small">کلید قبلی دستگاه «{{ rotating.title }}» از همین لحظه کار نمی‌کند و باید کلید جدید روی دستگاه تنظیم شود. ادامه می‌دهید؟</p>
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-outline-secondary" @click="rotating = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-danger" @click="rotate">صدور کلید جدید</button></div>
      </div>
    </div>

    <div v-if="secret" class="inv-modal-backdrop">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">کلید دستگاه {{ secret.code }}</h2>
        <div class="alert alert-warning small">این کلید فقط همین یک بار نمایش داده می‌شود و پس از بستن این پنجره قابل بازیابی نیست. آن را کپی و روی دستگاه تنظیم کنید؛ در صورت گم شدن باید کلید جدید صادر شود.</div>
        <div class="d-flex gap-2">
          <input id="att-device-key" :value="secret.key" class="form-control form-control-sm inv-ltr" readonly @focus="($event.target as HTMLInputElement).select()" />
          <button class="btn btn-sm btn-outline-primary text-nowrap" @click="copy">{{ copied ? "کپی شد" : "کپی" }}</button>
        </div>
        <div class="d-flex justify-content-end mt-3"><button class="btn btn-sm btn-primary" @click="closeSecret">کلید را ذخیره کردم</button></div>
      </div>
    </div>
  </div>
</template>
