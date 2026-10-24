<script setup lang="ts">
// New / edit request dialog for the seven kinds. Fields depend on the kind; "ذخیره" keeps a draft, "ذخیره و ارسال" sends it for approval.
import { ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { saveLeave } from "@/api/OfficeAPIs";
import { uploadFile } from "@/api/InventoryAPIs";
import { parseAmount } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { leaveKinds } from "@/composables/office";
import EmployeePick from "./EmployeePick.vue";
import TimeField from "./TimeField.vue";

const props = defineProps<{ isHr: boolean; leave?: any | null }>();
const emit = defineEmits(["close", "saved"]);
const l = props.leave;
const form = ref<any>({
  id: l?.id ?? null, rowVersion: l?.rowVersion ?? null, employee: null,
  kind: l?.kind ?? 1, from: l?.fromDate ? String(l.fromDate).slice(0, 10) : null, to: l?.toDate ? String(l.toDate).slice(0, 10) : null,
  fromTime: l?.fromTime || "", toTime: l?.toTime || "", hours: l?.hours != null ? String(l.hours) : "", direction: l?.direction || 1, reason: l?.reason || "",
  attachmentDocumentId: l?.attachmentDocumentId || null,
});
const file = ref<File | null>(null);
const busy = ref(false);
const k = () => Number(form.value.kind);
const hasRange = () => [1, 3, 4, 5].includes(k());

function onFile(e: any) {
  file.value = e.target.files?.[0] || null;
}

async function save(submit: boolean) {
  const f = form.value;
  const kind = k();
  if (!f.from) return fail(kind === 1 || kind === 3 || kind === 4 || kind === 5 ? "تاریخ شروع را وارد کنید." : "تاریخ را وارد کنید.");
  if (hasRange() && f.to && f.to < f.from) return fail("تاریخ پایان از تاریخ شروع زودتر است.");
  if (kind === 2 && (!f.fromTime || !f.toTime)) return fail("ساعت شروع و پایان مرخصی را وارد کنید.");
  if (kind === 2 && f.toTime <= f.fromTime) return fail("ساعت پایان باید بعد از ساعت شروع باشد.");
  if (kind === 5) {
    if (!f.reason.trim()) return fail("مقصد و موضوع ماموریت را بنویسید.");
    if ((f.fromTime || f.toTime) && (!f.fromTime || !f.toTime || f.toTime <= f.fromTime || (f.to && f.to !== f.from))) return fail("ماموریت ساعتی باید در یک روز باشد و ساعت شروع و پایان درست داشته باشد.");
  }
  let hours: number | null = null;
  if (kind === 6) {
    hours = parseAmount(f.hours);
    if (!hours || hours <= 0 || hours > 12) return fail("ساعت اضافه‌کار باید بین ۰ و ۱۲ باشد.");
  }
  if (kind === 7) {
    if (!f.fromTime) return fail("ساعت تردد فراموش‌شده را وارد کنید.");
    if (!f.reason.trim()) return fail("دلیل فراموشی تردد را بنویسید.");
  }
  if (kind === 3 && submit && !file.value && !f.attachmentDocumentId) return fail("برای مرخصی استعلاجی گواهی پزشکی را پیوست کنید.");
  busy.value = true;
  let documentId = f.attachmentDocumentId;
  if (file.value) {
    const [id, upError] = await uploadFile(file.value, `پیوست درخواست ${leaveKinds[kind]}`);
    if (upError) {
      busy.value = false;
      return fail(upError);
    }
    documentId = id;
  }
  const dto: any = {
    id: f.id, rowVersion: f.rowVersion, employeeId: props.isHr && !f.id && f.employee ? f.employee.id : null, kind,
    fromDate: f.from, toDate: hasRange() ? f.to || f.from : f.from,
    fromTime: kind === 2 || kind === 7 || (kind === 5 && f.fromTime) ? f.fromTime : null, toTime: kind === 2 || (kind === 5 && f.toTime) ? f.toTime : null,
    hours, direction: kind === 7 ? Number(f.direction) : null, reason: f.reason.trim() || null, attachmentDocumentId: documentId, submit,
  };
  const [d, error] = await saveLeave(dto);
  busy.value = false;
  if (error) return fail(error);
  ok(submit ? "درخواست ثبت و برای تایید ارسال شد." : "پیش‌نویس ذخیره شد.");
  emit("saved", d);
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal">
      <h2 class="inv-title mb-2">{{ form.id ? "ویرایش درخواست" : "درخواست جدید" }}</h2>
      <div class="row g-2">
        <div v-if="isHr && !form.id" class="col-12"><label class="form-label small mb-0">برای کارمند (اختیاری، فقط اداری)</label><EmployeePick v-model="form.employee" placeholder="خودم" /></div>
        <div class="col-12"><label class="form-label small mb-0">نوع درخواست *</label><select v-model="form.kind" :disabled="!!form.id" class="form-select form-select-sm"><option v-for="(t, v) in leaveKinds" :key="v" :value="Number(v)">{{ t }}</option></select></div>

        <template v-if="hasRange()">
          <div class="col-md-6"><label class="form-label small mb-0">از تاریخ *</label><FnDate v-model="form.from" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="form.to" /></div>
          <template v-if="k() === 5">
            <div class="col-md-6"><label class="form-label small mb-0">ساعت شروع (ماموریت ساعتی)</label><TimeField v-model="form.fromTime" /></div>
            <div class="col-md-6"><label class="form-label small mb-0">ساعت پایان</label><TimeField v-model="form.toTime" /></div>
          </template>
        </template>
        <template v-else>
          <div class="col-12"><label class="form-label small mb-0">تاریخ *</label><FnDate v-model="form.from" /></div>
          <template v-if="k() === 2">
            <div class="col-md-6"><label class="form-label small mb-0">از ساعت *</label><TimeField v-model="form.fromTime" /></div>
            <div class="col-md-6"><label class="form-label small mb-0">تا ساعت *</label><TimeField v-model="form.toTime" /></div>
          </template>
          <div v-if="k() === 6" class="col-12"><label class="form-label small mb-0">ساعت اضافه‌کار (حداکثر ۱۲) *</label><input v-model="form.hours" class="form-control form-control-sm fn-num" placeholder="مثلا 2.5" /></div>
          <template v-if="k() === 7">
            <div class="col-md-6"><label class="form-label small mb-0">ساعت تردد فراموش‌شده *</label><TimeField v-model="form.fromTime" /></div>
            <div class="col-md-6"><label class="form-label small mb-0">جهت *</label><select v-model="form.direction" class="form-select form-select-sm"><option :value="1">ورود</option><option :value="2">خروج</option></select></div>
          </template>
        </template>

        <div class="col-12"><label class="form-label small mb-0">{{ k() === 5 ? "مقصد و موضوع *" : k() === 7 ? "دلیل *" : "توضیح / دلیل" }}</label><textarea v-model="form.reason" class="form-control form-control-sm" rows="2"></textarea></div>
        <div class="col-12">
          <label class="form-label small mb-0">پیوست {{ k() === 3 ? "(گواهی پزشکی برای ارسال لازم است)" : "(اختیاری)" }}</label>
          <input class="form-control form-control-sm" type="file" @change="onFile" />
          <div v-if="form.attachmentDocumentId && !file" class="inv-muted small">این درخواست پیوست دارد؛ انتخاب فایل جدید آن را جایگزین می‌کند.</div>
        </div>
        <div v-if="k() === 6" class="col-12 inv-muted small">اضافه‌کار فقط با تایید قبلی در محاسبه حضور و غیاب حساب می‌شود.</div>
        <div v-if="k() === 7" class="col-12 inv-muted small">پس از تایید مدیر، واحد اداری تردد را در حضور و غیاب ثبت می‌کند.</div>
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3">
        <button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button>
        <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="save(false)">ذخیره</button>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="save(true)">ذخیره و ارسال</button>
      </div>
    </div>
  </div>
</template>
