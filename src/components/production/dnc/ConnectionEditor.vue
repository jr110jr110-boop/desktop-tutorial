<script setup lang="ts">
import { ref } from "vue";
import { newGatewayKey, saveConnection } from "@/api/DncAPIs";
import { fail, ok } from "@/composables/inventory";
import { controllerFamilies, lineEndings, linkKinds, linkPresets } from "@/composables/dnc";

// The DNC link of one machine (production manager / PMO). The secret of an FTP login is never typed here: only its key name; the value is set in the server settings (Dnc:Credentials:<key>).
const props = defineProps<{ row: any }>();
const emit = defineEmits(["close", "saved"]);
const r = props.row;
const f = ref({
  machineId: r.machineId, controllerFamily: r.controllerFamily ?? 0, controllerModel: r.controllerModel || "", linkKind: r.linkKind ?? 1, host: r.host || "", port: r.port ?? null,
  remotePath: r.remotePath || "", credentialKey: r.credentialKey || "", programNameFormat: r.programNameFormat || "", fileExtension: r.fileExtension || "", lineEnding: r.lineEnding ?? 1,
  requirePercent: !!r.requirePercent, maxProgramBytes: r.maxProgramBytes ?? null, axes: r.axes || "", maxSpindleRpm: r.maxSpindleRpm ?? null, maxFeedMmPerMin: r.maxFeedMmPerMin ?? null,
  toolCapacity: r.toolCapacity ?? null, isLathe: !!r.isLathe, baudRate: r.baudRate ?? null, requireConfirmedProgram: r.id ? !!r.requireConfirmedProgram : true, allowedAddresses: r.allowedAddresses || "",
  isActive: r.id ? !!r.isActive : true, note: r.note || "", rowVersion: r.rowVersion || null,
});
const preset = ref("");
const busy = ref(false);
const key = ref<string | null>(null);

function applyPreset() {
  const p = linkPresets[preset.value];
  if (p) Object.assign(f.value, p);
}
const num = (v: any) => (v === "" || v === null || v === undefined ? null : Number(v));
const txt = (v: string) => (v && v.trim() ? v.trim() : null);

async function save() {
  busy.value = true;
  const x = f.value;
  const [, error] = await saveConnection({
    ...x, host: txt(x.host), port: num(x.port), remotePath: txt(x.remotePath), credentialKey: txt(x.credentialKey), programNameFormat: txt(x.programNameFormat), fileExtension: txt(x.fileExtension),
    controllerModel: txt(x.controllerModel), maxProgramBytes: num(x.maxProgramBytes), axes: txt(x.axes), maxSpindleRpm: num(x.maxSpindleRpm), maxFeedMmPerMin: num(x.maxFeedMmPerMin),
    toolCapacity: num(x.toolCapacity), baudRate: num(x.baudRate), allowedAddresses: txt(x.allowedAddresses), note: txt(x.note),
  });
  busy.value = false;
  if (error) return fail(error);
  ok("اتصال ماشین ذخیره شد.");
  emit("saved");
}

async function makeKey() {
  if (r.hasGatewayKey && !confirm("کلید فعلی گیت‌وی باطل می‌شود. ادامه می‌دهید؟")) return;
  busy.value = true;
  const [d, error] = await newGatewayKey(r.machineId);
  busy.value = false;
  if (error) return fail(error);
  key.value = d.key;
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 820px">
      <h3 class="inv-title mb-3">اتصال DNC ماشین {{ r.machineTitle }}</h3>
      <div class="row g-2 small">
        <div class="col-md-12">
          <label class="form-label mb-0">مقادیر پیشنهادی برای ماشین‌های شرکت</label>
          <div class="d-flex gap-2"><select v-model="preset" class="form-select form-select-sm"><option value="">انتخاب کنید (اختیاری)</option><option v-for="(_, k) in linkPresets" :key="k" :value="k">{{ k }}</option></select><button :disabled="!preset" class="btn btn-sm btn-outline-primary" @click="applyPreset">اعمال</button></div>
        </div>
        <div class="col-md-4"><label class="form-label mb-0">خانواده کنترلر</label><select v-model.number="f.controllerFamily" class="form-select form-select-sm"><option v-for="(t, k) in controllerFamilies" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-4"><label class="form-label mb-0">مدل کنترلر</label><input v-model="f.controllerModel" class="form-control form-control-sm inv-ltr" placeholder="TNC 426/430" /></div>
        <div class="col-md-4"><label class="form-label mb-0">نوع اتصال</label><select v-model.number="f.linkKind" class="form-select form-select-sm"><option v-for="(t, k) in linkKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <template v-if="f.linkKind === 3 || f.linkKind === 4">
          <div class="col-md-5"><label class="form-label mb-0">نشانی دستگاه (IP)</label><input v-model="f.host" class="form-control form-control-sm inv-ltr" placeholder="192.168.10.21" /></div>
          <div class="col-md-3"><label class="form-label mb-0">درگاه</label><input v-model="f.port" class="form-control form-control-sm inv-ltr" :placeholder="f.linkKind === 3 ? '21' : '4001'" /></div>
          <div v-if="f.linkKind === 4" class="col-md-4"><label class="form-label mb-0">نرخ باود</label><select v-model.number="f.baudRate" class="form-select form-select-sm"><option :value="null">9600 (پیش‌فرض)</option><option v-for="b in [2400, 4800, 9600, 19200, 38400, 57600, 115200]" :key="b" :value="b">{{ b }}</option></select></div>
        </template>
        <div v-if="f.linkKind === 2 || f.linkKind === 3" class="col-md-8"><label class="form-label mb-0">{{ f.linkKind === 2 ? "مسیر پوشه اشتراکی" : "پوشه روی کنترلر" }}</label><input v-model="f.remotePath" class="form-control form-control-sm inv-ltr" :placeholder="f.linkKind === 2 ? '\\\\server\\cnc\\mikron' : '/nc'" /></div>
        <div v-if="f.linkKind === 3" class="col-md-4"><label class="form-label mb-0">نام کلید اعتبارنامه</label><input v-model="f.credentialKey" class="form-control form-control-sm inv-ltr" placeholder="mikron" /><div class="inv-muted">رمز در تنظیمات سرور ثبت می‌شود{{ r.credentialConfigured ? " (ثبت شده)" : "" }}.</div></div>
        <div class="col-md-4"><label class="form-label mb-0">نام برنامه روی کنترلر</label><input v-model="f.programNameFormat" class="form-control form-control-sm inv-ltr" placeholder="{number} یا O{number}" /></div>
        <div class="col-md-2"><label class="form-label mb-0">پسوند</label><input v-model="f.fileExtension" class="form-control form-control-sm inv-ltr" placeholder=".H" /></div>
        <div class="col-md-3"><label class="form-label mb-0">پایان خط</label><select v-model.number="f.lineEnding" class="form-select form-select-sm"><option v-for="(t, k) in lineEndings" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-3"><label class="form-label mb-0">حافظه کنترلر (بایت)</label><input v-model="f.maxProgramBytes" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-3"><label class="form-label mb-0">محورها</label><input v-model="f.axes" class="form-control form-control-sm inv-ltr" placeholder="XYZ" /></div>
        <div class="col-md-3"><label class="form-label mb-0">حداکثر دور اسپیندل</label><input v-model="f.maxSpindleRpm" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-3"><label class="form-label mb-0">حداکثر پیشروی (mm/min)</label><input v-model="f.maxFeedMmPerMin" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-3"><label class="form-label mb-0">ظرفیت مخزن ابزار</label><input v-model="f.toolCapacity" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-4 form-check ms-2"><input id="dnc-pct" v-model="f.requirePercent" class="form-check-input" type="checkbox" /><label class="form-check-label" for="dnc-pct">آغاز و پایان با % (قالب نوار ISO)</label></div>
        <div class="col-md-3 form-check ms-2"><input id="dnc-lathe" v-model="f.isLathe" class="form-check-input" type="checkbox" /><label class="form-check-label" for="dnc-lathe">ماشین تراش</label></div>
        <div class="col-md-4 form-check ms-2"><input id="dnc-gate" v-model="f.requireConfirmedProgram" class="form-check-input" type="checkbox" /><label class="form-check-label" for="dnc-gate">شروع کار فقط با برنامه تأییدشده روی کنترلر</label></div>
        <div class="col-md-3 form-check ms-2"><input id="dnc-act" v-model="f.isActive" class="form-check-input" type="checkbox" /><label class="form-check-label" for="dnc-act">فعال</label></div>
        <div v-if="f.linkKind === 5" class="col-md-12"><label class="form-label mb-0">نشانی‌های مجاز گیت‌وی (اختیاری)</label><input v-model="f.allowedAddresses" class="form-control form-control-sm inv-ltr" placeholder="192.168.10.30 یا 192.168.10.0/24" /></div>
        <div class="col-md-12"><label class="form-label mb-0">توضیح</label><input v-model="f.note" class="form-control form-control-sm" /></div>
      </div>
      <div v-if="f.linkKind === 4" class="inv-muted small mt-2">روی کنترلر هایدن‌هاین، رابط RS-232 را در حالت EXT1 و با همان نرخ باود مبدل تنظیم کنید؛ پیش از ارسال، کنترلر را در حالت دریافت قرار دهید.</div>
      <div v-if="r.id && r.linkKind === 5 && f.linkKind === 5" class="mt-3 small">
        <button :disabled="busy" class="btn btn-sm btn-outline-warning" @click="makeKey">{{ r.hasGatewayKey ? "صدور کلید تازه گیت‌وی" : "صدور کلید گیت‌وی" }}</button>
        <span v-if="r.gatewayKeyRotatedAtText" class="inv-muted ms-2">آخرین صدور: {{ r.gatewayKeyRotatedAtText }}</span>
        <div v-if="key" class="alert alert-warning mt-2 mb-0">این کلید فقط همین یک بار نمایش داده می‌شود؛ آن را در گیت‌وی ماشین وارد کنید: <code class="inv-ltr d-block mt-1">{{ key }}</code></div>
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">بستن</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
    </div>
  </div>
</template>
