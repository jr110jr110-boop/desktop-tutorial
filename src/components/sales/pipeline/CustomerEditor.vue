<script setup lang="ts">
import { computed, ref } from "vue";
import UserPick from "@/components/office/wf/UserPick.vue";
import { saveSlCustomer, setSlCredit } from "@/api/SalesAPIs";
import { fail, ok } from "@/composables/inventory";
import { customerStatuses, customerTypes, industries, rialText } from "@/composables/sales";
import { SalesRoles, hasRole } from "@/composables/roles";
import { groupDigits, numOrNull } from "./pipelineKit";

// Customer profile editor. The credit limit is not part of the save DTO: it is shown only when the API returned it (finance roles) and set by the finance manager alone.
// Name, economic code and national id of an existing customer belong to finance (identityChangers); for the other roles they are read-only here.
const props = defineProps({ row: { type: Object, default: null } });
const emit = defineEmits(["close", "saved"]);

const r = props.row;
const isNew = !r;
const f = ref<any>({
  id: r?.id ?? null, name: r?.name || "", economicCode: r?.economicCode || "", nationalId: r?.nationalId || "", postalCode: r?.postalCode || "", address: r?.address || "", phone: r?.phone || "",
  employerName: r?.employerName || "", type: r?.type ?? null, industry: r?.industry ?? null, country: r?.country || "", city: r?.city || "", status: r?.status ?? 1,
  ownerUserId: r?.ownerUserId ?? null, paymentTermDays: r?.paymentTermDays ?? null, note: r?.note || "", rowVersion: r?.rowVersion ?? null,
});
const credit = ref(r?.creditLimitRial !== null && r?.creditLimitRial !== undefined ? groupDigits(r.creditLimitRial) : "");
const busy = ref(false);
const canWrite = hasRole(SalesRoles.masterWriters);
const canIdentity = isNew || hasRole(SalesRoles.identityChangers);
const canSeeCredit = computed(() => hasRole(SalesRoles.creditViewers) || (r && r.creditLimitRial !== null && r.creditLimitRial !== undefined));
const canSetCredit = hasRole(SalesRoles.creditSetters) && !isNew;

async function save() {
  if (!String(f.value.name).trim()) return fail({ message: "نام مشتری را بنویسید." });
  busy.value = true;
  const t = (v: any) => String(v ?? "").trim();
  const [d, error] = await saveSlCustomer({
    id: f.value.id, name: t(f.value.name), economicCode: t(f.value.economicCode), nationalId: t(f.value.nationalId), postalCode: t(f.value.postalCode), address: t(f.value.address), phone: t(f.value.phone),
    employerName: t(f.value.employerName), type: f.value.type, industry: f.value.industry, country: t(f.value.country), city: t(f.value.city), status: f.value.status,
    ownerUserId: f.value.ownerUserId, paymentTermDays: numOrNull(f.value.paymentTermDays), note: t(f.value.note), rowVersion: f.value.rowVersion,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("مشتری ذخیره شد.");
  if (d?.rowVersion) f.value.rowVersion = d.rowVersion;
  emit("saved", d);
}

async function saveCredit() {
  busy.value = true;
  const [d, error] = await setSlCredit({ customerId: f.value.id, creditLimitRial: numOrNull(credit.value) });
  busy.value = false;
  if (error) return fail(error);
  ok("سقف اعتبار ثبت شد.");
  if (d?.rowVersion) f.value.rowVersion = d.rowVersion;
  credit.value = d?.creditLimitRial !== null && d?.creditLimitRial !== undefined ? groupDigits(d.creditLimitRial) : "";
  emit("saved", d);
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 860px">
      <h2 class="inv-title mb-2">{{ isNew ? "مشتری جدید" : "پروفایل مشتری" }}</h2>
      <p v-if="!canIdentity" class="small text-muted mb-2">نام، کد اقتصادی و شناسه ملی مشتری موجود را فقط مالی تغییر می‌دهد.</p>
      <div class="row g-2">
        <div class="col-md-6"><label class="form-label small mb-0">نام</label><input v-model="f.name" :disabled="!canIdentity || !canWrite" class="form-control form-control-sm" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">کد اقتصادی</label><input v-model="f.economicCode" :disabled="!canIdentity || !canWrite" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">شناسه ملی</label><input v-model="f.nationalId" :disabled="!canIdentity || !canWrite" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">نوع مشتری</label><select v-model="f.type" :disabled="!canWrite" class="form-select form-select-sm"><option :value="null">-</option><option v-for="(t, k) in customerTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-4"><label class="form-label small mb-0">صنعت</label><select v-model="f.industry" :disabled="!canWrite" class="form-select form-select-sm"><option :value="null">-</option><option v-for="(t, k) in industries" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-4"><label class="form-label small mb-0">وضعیت</label><select v-model="f.status" :disabled="!canWrite" class="form-select form-select-sm"><option v-for="(t, k) in customerStatuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-3"><label class="form-label small mb-0">کشور</label><input v-model="f.country" :disabled="!canWrite" class="form-control form-control-sm" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">شهر</label><input v-model="f.city" :disabled="!canWrite" class="form-control form-control-sm" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">تلفن</label><input v-model="f.phone" :disabled="!canWrite" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">کد پستی</label><input v-model="f.postalCode" :disabled="!canWrite" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-8"><label class="form-label small mb-0">نشانی</label><input v-model="f.address" :disabled="!canWrite" class="form-control form-control-sm" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">نام کارفرما در قرارداد</label><input v-model="f.employerName" :disabled="!canWrite" class="form-control form-control-sm" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">مسئول فروش</label><UserPick v-model="f.ownerUserId" :fallback-text="r?.ownerName || ''" :disabled="!canWrite" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">مهلت پرداخت (روز)</label><input v-model="f.paymentTermDays" :disabled="!canWrite" class="form-control form-control-sm inv-ltr" inputmode="numeric" /></div>
        <div class="col-12"><label class="form-label small mb-0">یادداشت</label><textarea v-model="f.note" :disabled="!canWrite" class="form-control form-control-sm" rows="2" /></div>
      </div>
      <div v-if="canSeeCredit && !isNew" class="border rounded p-2 mt-3">
        <label class="form-label small mb-1">سقف اعتبار (ریال)</label>
        <div class="d-flex gap-2 align-items-center">
          <input v-model="credit" :disabled="!canSetCredit" class="form-control form-control-sm inv-ltr" style="max-width: 240px" inputmode="numeric" placeholder="تعیین نشده" @blur="credit = groupDigits(credit)" />
          <button v-if="canSetCredit" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="saveCredit">ثبت سقف اعتبار</button>
          <span v-else class="small text-muted">فقط مدیر مالی سقف اعتبار را تعیین می‌کند. مقدار فعلی: {{ rialText(r?.creditLimitRial) }}</span>
        </div>
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3">
        <button class="btn btn-sm btn-outline-secondary" @click="emit('close')">بستن</button>
        <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
      </div>
    </div>
  </div>
</template>
