<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { createOverride, getGuardMachines, getOverrides } from "@/api/GuardAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, ok } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { GuardRoles, gateTitles } from "@/composables/guard";

// The one-time overrides: only the production manager lets one start (or one finish) through one gate, with a written reason; valid 30 minutes, used once, and the CEO, the production
// manager and the auditor are told. The list is the record of every override.
const rows = ref<any[]>([]);
const activeOnly = ref(false);
const machines = ref<any[]>([]);
const form = ref<null | { machineId: number | null; operationId: string; gate: string; reason: string }>(null);
const busy = ref(false);
const canCreate = hasRole(GuardRoles.overriders);
const reasonOk = computed(() => (form.value?.reason || "").trim().length >= 10);

async function load() {
  const [d, error] = await getOverrides({ activeOnly: activeOnly.value, pageSize: 200 });
  if (error) return fail(error);
  rows.value = rowsOf(d);
}
onMounted(async () => {
  load();
  const [d] = await getGuardMachines();
  machines.value = Array.isArray(d) ? d : [];
});
async function save() {
  const f = form.value!;
  busy.value = true;
  const [, error] = await createOverride({ machineId: Number(f.machineId), operationId: Number(f.operationId), gate: f.gate, reason: f.reason.trim() });
  busy.value = false;
  if (error) return fail(error);
  ok("مجوز یک‌باره ثبت شد و به مدیرعامل و حسابرس خبر داده شد.");
  form.value = null;
  load();
}
</script>

<template>
  <div class="gd-panel">
    <div class="d-flex gap-3 align-items-center flex-wrap mb-3">
      <div class="form-check m-0"><input id="ov-act" v-model="activeOnly" class="form-check-input" type="checkbox" @change="load" /><label class="form-check-label" for="ov-act">فقط مجوزهای فعال</label></div>
      <button v-if="canCreate" class="gd-btn solid ms-auto" @click="form = { machineId: null, operationId: '', gate: 'interlock', reason: '' }">مجوز یک‌باره تازه</button>
    </div>
    <p class="gd-muted">هر مجوز فقط برای یک گیت، یک عملیات و یک ماشین است؛ ۳۰ دقیقه اعتبار دارد و با اولین استفاده تمام می‌شود.</p>
    <div v-if="!rows.length" class="gd-empty">مجوزی ثبت نشده است.</div>
    <table v-else>
      <thead><tr><th>زمان</th><th>ماشین</th><th>عملیات</th><th>گیت</th><th>دلیل</th><th>صادرکننده</th><th>وضعیت</th></tr></thead>
      <tbody>
        <tr v-for="o in rows" :key="o.id">
          <td class="text-nowrap">{{ o.atText }}</td>
          <td>{{ o.machineTitle }}</td>
          <td>{{ o.operationId }}</td>
          <td>{{ o.gateText }}</td>
          <td style="max-width: 360px">{{ o.reason }}</td>
          <td>{{ o.byName }}</td>
          <td><span :class="o.active ? 'warn' : o.consumedAtText ? 'ok' : ''" class="gd-chip">{{ o.active ? "فعال تا " + o.expiresAtText : o.consumedAtText ? "استفاده‌شده " + o.consumedAtText : "منقضی" }}</span></td>
        </tr>
      </tbody>
    </table>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal gd" style="max-width: 560px">
        <h3 class="h5 mb-3">مجوز یک‌باره مدیر تولید</h3>
        <div class="row g-2 small">
          <div class="col-md-6"><label class="form-label mb-0">ماشین</label><select v-model.number="form.machineId" class="form-select form-select-sm"><option :value="null">انتخاب کنید</option><option v-for="m in machines" :key="m.machineId" :value="m.machineId">{{ m.machineTitle }}</option></select></div>
          <div class="col-md-6"><label class="form-label mb-0">شماره عملیات (از کارت کار یا ترمینال)</label><input v-model="form.operationId" class="form-control form-control-sm gd-ltr" inputmode="numeric" /></div>
          <div class="col-12"><label class="form-label mb-0">گیت</label><select v-model="form.gate" class="form-select form-select-sm"><option v-for="(t, k) in gateTitles" :key="k" :value="k">{{ t }}</option></select></div>
          <div class="col-12"><label class="form-label mb-0">دلیل (برای مدیرعامل و حسابرس فرستاده می‌شود)</label><textarea v-model="form.reason" class="form-control" rows="3"></textarea><div :class="reasonOk ? 'text-success' : 'gd-muted'" class="small">دست‌کم ۱۰ نویسه</div></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="gd-btn" @click="form = null">انصراف</button>
          <button :disabled="busy || !form.machineId || !form.operationId || !reasonOk" class="gd-btn solid" @click="save">صدور مجوز</button>
        </div>
      </div>
    </div>
  </div>
</template>
