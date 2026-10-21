<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import DocAttach from "@/components/quality/DocAttach.vue";
import { addCalibration, equipmentAction, getEquipment, saveEquipment } from "@/api/QualityAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { equipmentBadge, equipmentStatuses, equipmentTypes } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// One instrument: identity, calibration history, calibration entry (QC only), out-of-service.
const route = useRoute();
const router = useRouter();
const eq = ref<any>(null);
const cal = ref<any>(null);
const reason = ref("");
const busy = ref(false);
const canQc = hasRole(QualityRoles.inspectors);
const lastEvent = computed(() => (eq.value?.events || [])[0] || null);

function blankEvent() {
  return {
    equipmentId: eq.value.id, eventDate: today(), labName: "", labAccredited: false, certificateNumber: "", certificateDocumentId: null,
    traceabilityStatement: "", asFoundPass: true, asLeftPass: true, adjusted: false, intervalMonths: null, note: "",
  };
}

async function save() {
  busy.value = true;
  const [data, error] = await saveEquipment({ ...eq.value, intervalMonths: eq.value.intervalMonths || null });
  busy.value = false;
  if (error) return fail(error);
  eq.value = data;
  ok("ذخیره شد.");
}

async function saveEvent() {
  const c = cal.value;
  busy.value = true;
  const [data, error] = await addCalibration({ ...c, intervalMonths: c.intervalMonths || null });
  busy.value = false;
  if (error) return fail(error);
  eq.value = data;
  cal.value = null;
  ok(c.asFoundPass ? "کالیبراسیون ثبت شد." : "ثبت شد؛ پرونده خارج از تلرانس ساخته شد و سوابق استفاده‌کننده فهرست شد.");
}

async function action(name: string) {
  busy.value = true;
  const [data, error] = await equipmentAction(eq.value.id, name, reason.value);
  busy.value = false;
  if (error) return fail(error);
  eq.value = data;
  reason.value = "";
  ok("انجام شد.");
}

onMounted(async () => {
  const [data, error] = await getEquipment(Number(route.params.id));
  if (error) return fail(error);
  eq.value = data;
});
</script>

<template>
  <div v-if="eq" class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">{{ eq.name }} <span class="inv-ltr ms-2">{{ eq.code }}</span></h1>
          <span :class="equipmentBadge(eq.status)" class="inv-badge">{{ equipmentStatuses[eq.status] }}</span>
          <span class="inv-muted ms-2">آخرین کالیبراسیون {{ jalali(eq.lastCalibrated) || "—" }} — سررسید {{ jalali(eq.nextDue) || "—" }}</span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'QualityCalibration' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="canQc" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره مشخصات</button>
          <button v-if="canQc && eq.requiresCalibration" class="btn btn-sm btn-outline-success" @click="cal = blankEvent()">ثبت کالیبراسیون</button>
        </div>
      </div>
      <div class="row g-2">
        <div class="col-md-4"><label class="form-label small mb-0">نام</label><input v-model="eq.name" :disabled="!canQc" class="form-control form-control-sm" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">نوع</label><select v-model="eq.type" :disabled="!canQc" class="form-select form-select-sm"><option v-for="(t, k) in equipmentTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-2"><label class="form-label small mb-0">سازنده</label><input v-model="eq.maker" :disabled="!canQc" class="form-control form-control-sm" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">مدل</label><input v-model="eq.model" :disabled="!canQc" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">شماره سریال</label><input v-model="eq.serialNumber" :disabled="!canQc" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">واحد مالک</label><input v-model="eq.ownerUnit" :disabled="!canQc" class="form-control form-control-sm" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">محل</label><input v-model="eq.location" :disabled="!canQc" class="form-control form-control-sm" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">فاصله (ماه)</label><input v-model.number="eq.intervalMonths" :disabled="!canQc" class="form-control form-control-sm" type="number" min="1" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">محدوده</label><input v-model="eq.range" :disabled="!canQc" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">دقت / تفکیک</label><input v-model="eq.accuracy" :disabled="!canQc" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">جدول تصحیح</label><DocAttach v-model="eq.correctionTableDocumentId" :disabled="!canQc" title="جدول تصحیح" /></div>
        <div class="col-md-3 d-flex align-items-end"><label class="form-check small"><input v-model="eq.requiresCalibration" :disabled="!canQc" class="form-check-input" type="checkbox" /> نیاز به کالیبراسیون دارد</label></div>
        <div class="col-md-12"><label class="form-label small mb-0">توضیح</label><input v-model="eq.note" :disabled="!canQc" class="form-control form-control-sm" /></div>
      </div>
      <div v-if="canQc" class="d-flex flex-wrap gap-2 align-items-end mt-3">
        <template v-if="eq.status !== 3">
          <input v-model="reason" class="form-control form-control-sm" placeholder="دلیل خارج‌کردن از رده" style="max-width: 280px" />
          <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="action('out-of-service')">خارج از رده</button>
        </template>
        <button v-else :disabled="busy" class="btn btn-sm btn-outline-success" @click="action('return-to-service')">بازگشت به سرویس (کالیبراسیون معتبر لازم)</button>
        <span v-if="eq.outOfServiceReason" class="small text-danger">دلیل: {{ eq.outOfServiceReason }}</span>
      </div>
    </div>

    <div v-if="cal" class="inv-card screen-only">
      <h2 class="inv-title mb-2">ثبت کالیبراسیون</h2>
      <div class="row g-2">
        <div class="col-md-2"><label class="form-label small mb-0">تاریخ</label><input v-model="cal.eventDate" class="form-control form-control-sm" type="date" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">آزمایشگاه کالیبراسیون *</label><input v-model="cal.labName" class="form-control form-control-sm" /></div>
        <div class="col-md-3 d-flex align-items-end"><label class="form-check small"><input v-model="cal.labAccredited" class="form-check-input" type="checkbox" /> آزمایشگاه دارای گواهی ISO/IEC 17025</label></div>
        <div class="col-md-3"><label class="form-label small mb-0">شماره گواهی *</label><input v-model="cal.certificateNumber" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">بیانیه ردیابی‌پذیری به استاندارد ملی / بین‌المللی *</label><input v-model="cal.traceabilityStatement" class="form-control form-control-sm" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">فایل گواهی</label><DocAttach v-model="cal.certificateDocumentId" title="گواهی کالیبراسیون" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">فاصله بعدی (ماه)</label><input v-model.number="cal.intervalMonths" class="form-control form-control-sm" :placeholder="eq.intervalMonths" type="number" min="1" max="60" /></div>
        <div class="col-md-3 d-flex align-items-end"><label class="form-check small"><input v-model="cal.asFoundPass" class="form-check-input" type="checkbox" /> نتیجه «همان‌طور که یافت شد» قبول است</label></div>
        <div class="col-md-3 d-flex align-items-end"><label class="form-check small"><input v-model="cal.asLeftPass" class="form-check-input" type="checkbox" /> نتیجه پس از تنظیم قبول است</label></div>
        <div class="col-md-3 d-flex align-items-end"><label class="form-check small"><input v-model="cal.adjusted" class="form-check-input" type="checkbox" /> تنظیم شد</label></div>
        <div class="col-md-12"><label class="form-label small mb-0">توضیح</label><input v-model="cal.note" class="form-control form-control-sm" /></div>
      </div>
      <p v-if="!cal.asFoundPass" class="text-danger small mt-2 mb-0">مردود بودن نتیجه «همان‌طور که یافت شد» پرونده خارج از تلرانس می‌سازد و همه سوابق استفاده‌کننده از آخرین کالیبراسیون خوب فهرست می‌شود.</p>
      <div class="d-flex gap-2 mt-3"><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveEvent">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="cal = null">انصراف</button></div>
    </div>

    <div class="inv-card screen-only">
      <h2 class="inv-title mb-2">تاریخچه کالیبراسیون</h2>
      <table class="table table-sm">
        <thead><tr><th>تاریخ</th><th>آزمایشگاه</th><th>17025</th><th>گواهی</th><th>یافت‌شده</th><th>پس از تنظیم</th><th>تنظیم</th><th>فاصله</th><th>سررسید بعدی</th><th>خارج از تلرانس</th></tr></thead>
        <tbody>
          <tr v-if="!eq.events.length"><td colspan="10" class="text-center inv-muted">کالیبراسیونی ثبت نشده.</td></tr>
          <tr v-for="e in eq.events" :key="e.id">
            <td>{{ jalali(e.eventDate) }}</td><td>{{ e.labName }}</td><td>{{ e.labAccredited ? "بله" : "خیر" }}</td><td class="inv-ltr">{{ e.certificateNumber }}</td>
            <td><span :class="e.asFoundPass ? 'ok' : 'bad'" class="inv-badge">{{ e.asFoundPass ? "قبول" : "مردود" }}</span></td>
            <td><span :class="e.asLeftPass ? 'ok' : 'bad'" class="inv-badge">{{ e.asLeftPass ? "قبول" : "مردود" }}</span></td>
            <td>{{ e.adjusted ? "بله" : "" }}</td><td>{{ e.intervalMonths }}</td><td>{{ jalali(e.nextDue) }}</td>
            <td><a v-if="e.ootCaseId" href="#" class="inv-ltr" @click.prevent="router.push({ name: 'QualityOotCase', params: { id: e.ootCaseId } })">{{ e.ootNumber }}</a></td>
          </tr>
        </tbody>
      </table>
      <p v-if="lastEvent" class="inv-muted mb-0">آخرین گواهی: <span class="inv-ltr">{{ lastEvent.certificateNumber }}</span> — {{ lastEvent.traceabilityStatement }}</p>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
