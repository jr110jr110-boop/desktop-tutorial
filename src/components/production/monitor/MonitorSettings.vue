<script setup lang="ts">
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import { getMonitors, saveMonitor, testMonitorInputs } from "@/api/ShopAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { DncRoles } from "@/composables/dnc";
import { hasRole } from "@/composables/roles";

// How the state of every machine is read: a Modbus TCP digital-input module wired to the stack light (green = running, red = alarm, yellow / control on = power), or the edge box of the machine.
const filters = ref({ Contains: "" });
const list = usePagedList(getMonitors, filters);
const canEdit = hasRole(DncRoles.linkEditors);
const canTest = hasRole(DncRoles.linkTesters);
const sources: Record<number, string> = { 0: "بدون پایش", 1: "ماژول ورودی دیجیتال (Modbus TCP)", 2: "گیت‌وی لبه ماشین" };
const f = ref<any>(null);
const busy = ref(false);

function edit(r: any) {
  f.value = {
    machineTitle: r.machineTitle, hasGatewayLink: r.hasGatewayLink, machineId: r.machineId, source: r.source, host: r.host || "", port: r.port ?? null, unitId: r.unitId ?? 1, function: r.function ?? 2,
    startAddress: r.startAddress ?? 0, runInput: r.runInput ?? 0, alarmInput: r.alarmInput ?? 1, powerInput: r.powerInput ?? 2, invertMask: r.invertMask ?? 0, pollSeconds: r.pollSeconds ?? null,
    alarmCreatesRequest: !!r.alarmCreatesRequest, mismatchMinutes: r.mismatchMinutes ?? 15, isActive: r.id ? !!r.isActive : true, note: r.note || "", rowVersion: r.rowVersion || null,
  };
}
const n = (v: any) => (v === "" || v === null || v === undefined ? null : Number(v));
async function save() {
  const x = f.value;
  busy.value = true;
  const [, error] = await saveMonitor({
    ...x, host: x.host.trim() || null, port: n(x.port), unitId: Number(x.unitId) || 1, startAddress: Number(x.startAddress) || 0, runInput: n(x.runInput), alarmInput: n(x.alarmInput), powerInput: n(x.powerInput),
    invertMask: Number(x.invertMask) || 0, pollSeconds: n(x.pollSeconds), mismatchMinutes: Number(x.mismatchMinutes) || 15, note: x.note.trim() || null,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("تنظیم پایش ذخیره شد.");
  f.value = null;
  list.load();
}
async function test(r: any) {
  const [d, error] = await testMonitorInputs(r.machineId);
  if (error) return fail(error);
  d.ok ? ok(d.message + " — ورودی‌ها: " + d.inputs.map((b: boolean, i: number) => i + ":" + (b ? "۱" : "۰")).join(" ")) : fail(d.message);
}
onMounted(list.load);
</script>

<template>
  <div>
    <div class="inv-toolbar"><div class="inv-filters"><input v-model="filters.Contains" class="form-control form-control-sm" placeholder="نام ماشین" @keyup.enter="list.search" /><button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button></div></div>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>ماشین</th><th>منبع وضعیت</th><th>نشانی</th><th>ورودی کار / آلارم / روشن</th><th>آخرین ارتباط</th><th>خطا</th><th></th></tr></thead>
        <tbody>
          <tr v-for="r in list.rows.value" :key="r.machineId">
            <td>{{ r.machineTitle }}</td><td>{{ sources[r.source] }}<span v-if="r.id && !r.isActive" class="inv-badge ms-1">غیرفعال</span></td>
            <td class="inv-ltr">{{ r.host ? r.host + ":" + (r.port || 502) : "-" }}</td>
            <td class="inv-ltr">{{ r.source === 1 ? `${r.runInput ?? "-"} / ${r.alarmInput ?? "-"} / ${r.powerInput ?? "-"}` : "-" }}</td>
            <td>{{ r.lastSeenAtText || "-" }}</td><td class="small text-danger">{{ r.lastError || "" }}</td>
            <td class="text-nowrap"><button v-if="canEdit" class="btn btn-sm btn-outline-primary" @click="edit(r)">تنظیم</button><button v-if="canTest && r.source === 1" class="btn btn-sm btn-outline-secondary ms-1" @click="test(r)">آزمون ورودی‌ها</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

    <div v-if="f" class="inv-modal-backdrop" @click.self="f = null">
      <div class="inv-modal" style="max-width: 700px">
        <h3 class="inv-title mb-3">پایش ماشین {{ f.machineTitle }}</h3>
        <div class="row g-2 small">
          <div class="col-md-12"><label class="form-label mb-0">منبع وضعیت</label><select v-model.number="f.source" class="form-select form-select-sm"><option v-for="(t, k) in sources" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <template v-if="f.source === 1">
            <div class="col-md-5"><label class="form-label mb-0">نشانی ماژول (IP)</label><input v-model="f.host" class="form-control form-control-sm inv-ltr" placeholder="192.168.10.51" /></div>
            <div class="col-md-2"><label class="form-label mb-0">درگاه</label><input v-model="f.port" class="form-control form-control-sm inv-ltr" placeholder="502" /></div>
            <div class="col-md-2"><label class="form-label mb-0">شناسه واحد</label><input v-model="f.unitId" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-3"><label class="form-label mb-0">تابع</label><select v-model.number="f.function" class="form-select form-select-sm"><option :value="2">Discrete Inputs (02)</option><option :value="1">Coils (01)</option></select></div>
            <div class="col-md-3"><label class="form-label mb-0">نشانی شروع</label><input v-model="f.startAddress" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-3"><label class="form-label mb-0">ورودی «در حال کار» (سبز)</label><input v-model="f.runInput" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-3"><label class="form-label mb-0">ورودی «آلارم» (قرمز)</label><input v-model="f.alarmInput" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-3"><label class="form-label mb-0">ورودی «روشن» (زرد)</label><input v-model="f.powerInput" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-4"><label class="form-label mb-0">ماسک ورودی‌های معکوس</label><input v-model="f.invertMask" class="form-control form-control-sm inv-ltr" /><div class="inv-muted">بیت n = ورودی n فعال‌در‌صفر</div></div>
            <div class="col-md-4"><label class="form-label mb-0">فاصله پایش (ثانیه)</label><input v-model="f.pollSeconds" class="form-control form-control-sm inv-ltr" placeholder="10" /></div>
          </template>
          <div v-if="f.source === 2" class="col-md-12"><div :class="f.hasGatewayLink ? 'alert-info' : 'alert-warning'" class="alert py-1 mb-0">{{ f.hasGatewayLink ? "جعبه ماشین با همان کلید گیت‌وی اتصال DNC وضعیت را ارسال می‌کند." : "ابتدا در «برنامه‌های CNC ← اتصال ماشین‌ها» نوع اتصال را «گیت‌وی لبه» کنید و کلید صادر کنید." }}</div></div>
          <div class="col-md-4"><label class="form-label mb-0">آستانه ناهمخوانی با ثبت زمان (دقیقه)</label><input v-model="f.mismatchMinutes" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-5 form-check ms-2 mt-4"><input id="mon-req" v-model="f.alarmCreatesRequest" class="form-check-input" type="checkbox" /><label class="form-check-label" for="mon-req">آلارم ماشین درخواست تعمیر باز کند</label></div>
          <div class="col-md-2 form-check ms-2 mt-4"><input id="mon-act" v-model="f.isActive" class="form-check-input" type="checkbox" /><label class="form-check-label" for="mon-act">فعال</label></div>
          <div class="col-md-12"><label class="form-label mb-0">توضیح</label><input v-model="f.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="f = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>
