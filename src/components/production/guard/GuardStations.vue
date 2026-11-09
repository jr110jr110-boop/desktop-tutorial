<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getGuardMachines, getStations, issueStationKey, saveStation } from "@/api/GuardAPIs";
import { fail, ok } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { GuardRoles } from "@/composables/guard";

// Measuring stations: a gauge interface (a box or a PC with the gauges' receiver) pushes the values to api/v1/production/measure/device/push with headers X-Measure-Station and
// X-Measure-Key, only from its own addresses. A checklist item marked "device only" takes its value from here or from the machine probe, never from the keyboard.
const rows = ref<any[]>([]);
const machines = ref<any[]>([]);
const edit = ref<any>(null);
const key = ref<null | { code: string; key: string }>(null);
const busy = ref(false);
const canEdit = hasRole(GuardRoles.stationEditors);

async function load() {
  const [d, error] = await getStations();
  if (error) return fail(error);
  rows.value = Array.isArray(d) ? d : [];
}
onMounted(async () => {
  load();
  const [d] = await getGuardMachines();
  machines.value = Array.isArray(d) ? d : [];
});
async function save() {
  busy.value = true;
  const e = edit.value;
  const [, error] = await saveStation({ ...e, machineId: e.machineId || null });
  busy.value = false;
  if (error) return fail(error);
  ok("ایستگاه ذخیره شد.");
  edit.value = null;
  load();
}
async function newKey(s: any) {
  if (s.hasKey && !confirm("کلید فعلی ایستگاه از کار می‌افتد. ادامه می‌دهید؟")) return;
  const [d, error] = await issueStationKey(s.id);
  if (error) return fail(error);
  key.value = { code: s.code, key: d.key };
  load();
}
const copy = (t: string) => navigator.clipboard?.writeText(t).then(() => ok("کلید کپی شد."));
</script>

<template>
  <div class="gd-panel">
    <div class="d-flex align-items-center mb-2">
      <p class="gd-muted m-0">مقدارهای این ایستگاه‌ها برای بندهای «فقط از دستگاه» چک‌لیست پذیرفته می‌شود.</p>
      <button v-if="canEdit" class="gd-btn solid ms-auto" @click="edit = { code: '', title: '', allowedAddresses: '', machineId: null, isActive: true }">ایستگاه تازه</button>
    </div>
    <div v-if="!rows.length" class="gd-empty">ایستگاهی تعریف نشده است.</div>
    <table v-else>
      <thead><tr><th>کد</th><th>عنوان</th><th>ماشین</th><th>نشانی‌های مجاز</th><th>آخرین ارسال</th><th>کلید</th><th></th></tr></thead>
      <tbody>
        <tr v-for="s in rows" :key="s.id">
          <td class="gd-ltr">{{ s.code }}</td>
          <td>{{ s.title }} <span v-if="!s.isActive" class="gd-chip">غیرفعال</span></td>
          <td>{{ s.machineTitle || "—" }}</td>
          <td class="gd-ltr small">{{ s.allowedAddresses }}</td>
          <td>{{ s.lastSeenAtText || "هنوز" }}</td>
          <td><span :class="s.hasKey ? 'ok' : 'warn'" class="gd-chip">{{ s.hasKey ? "صادرشده" : "ندارد" }}</span></td>
          <td class="text-nowrap"><template v-if="canEdit"><button class="gd-btn me-1" @click="edit = { ...s }">ویرایش</button><button class="gd-btn" @click="newKey(s)">کلید تازه</button></template></td>
        </tr>
      </tbody>
    </table>

    <div v-if="edit" class="inv-modal-backdrop" @click.self="edit = null">
      <div class="inv-modal gd" style="max-width: 560px">
        <h3 class="h5 mb-3">{{ edit.id ? "ویرایش ایستگاه" : "ایستگاه اندازه‌گیری تازه" }}</h3>
        <div class="row g-2 small">
          <div class="col-md-4"><label class="form-label mb-0">کد</label><input v-model="edit.code" class="form-control form-control-sm gd-ltr" placeholder="G-01" /></div>
          <div class="col-md-8"><label class="form-label mb-0">عنوان</label><input v-model="edit.title" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label mb-0">ماشین (اختیاری)</label><select v-model="edit.machineId" class="form-select form-select-sm"><option :value="null">—</option><option v-for="m in machines" :key="m.machineId" :value="m.machineId">{{ m.machineTitle }}</option></select></div>
          <div class="col-md-6"><label class="form-label mb-0">نشانی‌های مجاز</label><input v-model="edit.allowedAddresses" class="form-control form-control-sm gd-ltr" placeholder="192.168.10.70" /></div>
          <div class="col-12 form-check ms-2"><input id="st-act" v-model="edit.isActive" class="form-check-input" type="checkbox" /><label class="form-check-label" for="st-act">فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="gd-btn" @click="edit = null">انصراف</button><button :disabled="busy" class="gd-btn solid" @click="save">ذخیره</button></div>
      </div>
    </div>
    <div v-if="key" class="inv-modal-backdrop" @click.self="key = null">
      <div class="inv-modal gd" style="max-width: 560px">
        <h3 class="h5 mb-2">کلید ایستگاه {{ key.code }}</h3>
        <p class="gd-muted">این کلید فقط همین یک بار نمایش داده می‌شود؛ آن را در تنظیمات جعبه ایستگاه وارد کنید.</p>
        <div class="gs-key gd-ltr">{{ key.key }}</div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="gd-btn" @click="copy(key.key)">کپی</button><button class="gd-btn solid" @click="key = null">بستم و ذخیره کردم</button></div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.gs-key { background: var(--page); border: 1px dashed var(--navy); border-radius: 8px; padding: 10px; word-break: break-all; font-size: 0.95rem; }
</style>
