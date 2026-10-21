<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import ContractPick from "@/components/fn/ContractPick.vue";
import VoucherPrint from "@/components/fn/print/VoucherPrint.vue";
import { deleteVoucher, getAccounts, getDetailAccounts, getVoucher, saveVoucher, voucherAction } from "@/api/FnAPIs";
import { getCostCenters } from "@/api/FinanceAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { roleTitle, sourceRoute } from "@/composables/finance";
import { badgeOfVoucher, money, parseAmount, printSheet, voucherStatuses, voucherTypes } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Voucher editor: draft -> check (another person) -> finalise (finance manager) -> reverse. Auto vouchers (from accounting events) are read-only.
const route = useRoute();
const router = useRouter();
const v = ref<any>(null);
const header = ref<{ date: string | null; description: string }>({ date: today(), description: "" });
const lines = ref<any[]>([]);
const costCenters = ref<any[]>([]);
const dialog = ref<"" | "reverse" | "delete">("");
const printing = ref(false);
const busy = ref(false);
const canWrite = hasRole(FnRoles.accountants);
const canFinalize = hasRole(FnRoles.managers);

const isNew = computed(() => !v.value || !v.value.id);
const editable = computed(() => canWrite && (isNew.value || (v.value.status < 2 && ![3, 4, 6].includes(v.value.type) && !v.value.isImported)));
const totalDebit = computed(() => lines.value.reduce((s, l) => s + (parseAmount(l.debit) || 0), 0));
const totalCredit = computed(() => lines.value.reduce((s, l) => s + (parseAmount(l.credit) || 0), 0));
const diff = computed(() => totalDebit.value - totalCredit.value);
const balanced = computed(() => diff.value === 0 && totalDebit.value > 0);
const sourceLink = computed(() => (v.value?.sourceType ? sourceRoute(v.value.sourceType, v.value.sourceId) : null));

const blankLine = () => ({ account: null as any, detail1: null as any, detail2: null as any, debit: "", credit: "", description: "", costCenterId: null as number | null, contractId: null as number | null, quantity: "", currencyCode: "", currencyAmount: "", rate: "", more: false, sourceRole: null as string | null });

const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);
const searchAccounts = async (t: string) => rowsOf((await getAccounts({ Level: 3, Contains: t, PageSize: 15 }))[0]);
const searchDetails = async (t: string) => rowsOf((await getDetailAccounts({ Contains: t, PageSize: 15 }))[0]);
const accountLabel = (a: any) => `${a.code} - ${a.title}`;

function fill(data: any) {
  v.value = data;
  header.value = { date: data.date ? String(data.date).slice(0, 10) : today(), description: data.description || "" };
  lines.value = (data.lines || []).map((l: any) => ({
    account: { id: l.accountId, code: l.accountCode, title: l.accountTitle },
    detail1: l.detail1Id ? { id: l.detail1Id, code: l.detail1Code, title: l.detail1Title } : null,
    detail2: l.detail2Id ? { id: l.detail2Id, code: l.detail2Code, title: l.detail2Title } : null,
    debit: l.debit ? String(l.debit) : "", credit: l.credit ? String(l.credit) : "", description: l.description || "",
    costCenterId: l.costCenterId, contractId: l.contractId, quantity: l.quantity ?? "", currencyCode: l.currencyCode || "", currencyAmount: l.currencyAmount ?? "", rate: l.rate ?? "",
    more: !!(l.costCenterId || l.contractId || l.quantity || l.currencyCode), sourceRole: l.sourceRole,
  }));
}

async function load() {
  const id = route.params.id;
  if (id === "new") {
    v.value = null;
    header.value = { date: today(), description: "" };
    lines.value = [blankLine(), blankLine()];
    return;
  }
  const [data, error] = await getVoucher(Number(id));
  if (error) return fail(error);
  fill(data);
}

/** Puts the difference on the opposite side: into the last line when it is empty, otherwise into a new line. */
function balance() {
  const d = diff.value;
  if (d === 0) return;
  const last = lines.value[lines.value.length - 1];
  const empty = last && !parseAmount(last.debit) && !parseAmount(last.credit);
  const target = empty ? last : blankLine();
  if (d > 0) {
    target.credit = String(d);
    target.debit = "";
  } else {
    target.debit = String(-d);
    target.credit = "";
  }
  if (!empty) lines.value.push(target);
}

function addLine() {
  lines.value.push(blankLine());
}
function removeLine(i: number) {
  if (lines.value.length > 2) lines.value.splice(i, 1);
}
function onDebit(l: any) {
  if (parseAmount(l.debit)) l.credit = "";
}
function onCredit(l: any) {
  if (parseAmount(l.credit)) l.debit = "";
}

function buildDto() {
  if (!header.value.date) throw new Error("تاریخ سند را وارد کنید.");
  const out: any[] = [];
  lines.value.forEach((l, i) => {
    const debit = parseAmount(l.debit) || 0, credit = parseAmount(l.credit) || 0;
    if (!l.account && !debit && !credit) return;
    if (!l.account) throw new Error(`ردیف ${i + 1}: حساب را انتخاب کنید.`);
    if ((debit > 0) === (credit > 0)) throw new Error(`ردیف ${i + 1}: فقط یکی از بدهکار یا بستانکار را با مبلغ مثبت بنویسید.`);
    out.push({
      accountId: l.account.id, detail1Id: l.detail1?.id || null, detail2Id: l.detail2?.id || null, debit, credit,
      currencyCode: l.currencyCode || null, currencyAmount: parseAmount(l.currencyAmount), rate: parseAmount(l.rate), quantity: parseAmount(l.quantity),
      description: l.description || null, contractId: l.contractId || null, costCenterId: l.costCenterId || null, workOrderId: null,
    });
  });
  if (out.length < 2) throw new Error("سند حداقل دو ردیف لازم دارد.");
  return { id: v.value?.id || null, date: header.value.date, description: header.value.description || null, lines: out };
}

async function save() {
  let dto: any;
  try {
    dto = buildDto();
  } catch (e: any) {
    return fail(e.message);
  }
  if (!balanced.value) return fail(`سند متوازن نیست؛ اختلاف ${money(Math.abs(diff.value))} ریال. از «تراز کردن» استفاده کنید.`);
  busy.value = true;
  const [data, error] = await saveVoucher(dto);
  busy.value = false;
  if (error) return fail(error);
  ok("سند ذخیره شد.");
  fill(data);
  if (String(route.params.id) !== String(data.id)) router.replace({ name: "FnVoucherEdit", params: { id: data.id } });
}

async function act(action: string, extra: any = {}) {
  busy.value = true;
  const [data, error] = await voucherAction(v.value.id, action, extra);
  busy.value = false;
  if (error) return fail(error);
  if (action === "reverse") {
    ok("سند برگشتی (پیش‌نویس) صادر شد.");
    router.push({ name: "FnVoucherEdit", params: { id: data.id } });
    return;
  }
  ok(action === "check" ? "سند کنترل شد." : action === "uncheck" ? "سند به پیش‌نویس برگشت." : "سند قطعی شد.");
  fill(data);
}

async function remove() {
  dialog.value = "";
  const [, error] = await deleteVoucher(v.value.id);
  if (error) return fail(error);
  ok("پیش‌نویس حذف شد.");
  router.push({ name: "FnVouchers" });
}

async function print() {
  if (!v.value) return;
  printing.value = true;
  await nextTick();
  printSheet();
}

onMounted(async () => {
  const [data] = await getCostCenters();
  costCenters.value = data || [];
  load();
});
</script>

<template>
  <div class="inv-page">
    <VoucherPrint v-if="printing && v" :v="v" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">
          {{ isNew ? "سند حسابداری جدید" : `سند ${v.number ?? "پیش‌نویس " + v.temporaryNumber}` }}
          <span v-if="v" :class="v.type === 3 ? 'warn' : 'info'" class="inv-badge">{{ voucherTypes[v.type] }}</span>
          <span v-if="v" :class="badgeOfVoucher(v.status)" class="inv-badge me-1">{{ voucherStatuses[v.status] }}</span>
        </h1>
        <div class="d-flex gap-2 flex-wrap">
          <button class="btn btn-sm btn-outline-secondary" @click="router.push({ name: 'FnVouchers' })">فهرست اسناد</button>
          <button v-if="v" class="btn btn-sm btn-outline-secondary" @click="print">چاپ سند</button>
        </div>
      </div>

      <div v-if="v && v.type === 3" class="alert alert-warning py-2">
        این سند <b>خودکار</b> است و از رویداد حسابداری ساخته شده؛ ویرایش نمی‌شود. اصلاح آن فقط با سند برگشتی ممکن است.
        <span v-if="v.sourceNumber"> مبدا: <router-link v-if="sourceLink" :to="sourceLink">{{ v.sourceNumber }}</router-link><span v-else class="inv-ltr">{{ v.sourceNumber }}</span></span>
      </div>
      <div v-if="v && v.isImported" class="alert alert-secondary py-2">سند واردشده از سپیدار است و فقط خواندنی است.</div>
      <div v-if="v && v.latePosting" class="alert alert-warning py-2">این سند با تاخیر (پس از بسته‌شدن دوره رویداد) ثبت شده است.</div>
      <div v-if="v && (v.reversesVoucherId || v.reversedByVoucherId)" class="alert alert-info py-2">
        <span v-if="v.reversesVoucherId">این سند، برگشت سند <router-link :to="{ name: 'FnVoucherEdit', params: { id: v.reversesVoucherId } }">قبلی</router-link> است. </span>
        <span v-if="v.reversedByVoucherId">برای این سند <router-link :to="{ name: 'FnVoucherEdit', params: { id: v.reversedByVoucherId } }">سند برگشتی</router-link> صادر شده است.</span>
      </div>

      <div class="row g-2 mb-3">
        <div class="col-md-3"><label class="form-label small mb-0">تاریخ سند *</label><FnDate v-model="header.date" :disabled="!editable" /></div>
        <div class="col-md-9"><label class="form-label small mb-0">شرح سند</label><input v-model="header.description" :disabled="!editable" class="form-control form-control-sm" /></div>
      </div>

      <div class="table-responsive">
        <table class="table table-sm fn-lines">
          <thead>
            <tr><th style="width: 36px">#</th><th style="min-width: 230px">حساب معین *</th><th style="min-width: 170px">تفصیلی اول</th><th style="min-width: 170px">تفصیلی دوم</th><th style="min-width: 160px">شرح</th><th style="width: 130px">بدهکار</th><th style="width: 130px">بستانکار</th><th v-if="editable" style="width: 70px"></th></tr>
          </thead>
          <tbody>
            <template v-for="(l, i) in lines" :key="i">
              <tr>
                <td>{{ i + 1 }}</td>
                <td>
                  <LookupBox v-if="editable" v-model="l.account" :fetch="searchAccounts" :label="accountLabel" placeholder="کد یا نام حساب" />
                  <span v-else>{{ l.account ? accountLabel(l.account) : "" }}</span>
                  <div v-if="l.sourceRole" class="inv-muted">{{ roleTitle(l.sourceRole) }}</div>
                </td>
                <td>
                  <LookupBox v-if="editable" v-model="l.detail1" :fetch="searchDetails" :label="accountLabel" placeholder="تفصیلی" />
                  <span v-else>{{ l.detail1 ? accountLabel(l.detail1) : "" }}</span>
                </td>
                <td>
                  <LookupBox v-if="editable" v-model="l.detail2" :fetch="searchDetails" :label="accountLabel" placeholder="پروژه / مرکز هزینه" />
                  <span v-else>{{ l.detail2 ? accountLabel(l.detail2) : "" }}</span>
                </td>
                <td><input v-model="l.description" :disabled="!editable" class="form-control form-control-sm" /></td>
                <td><input v-model="l.debit" :disabled="!editable" class="form-control form-control-sm fn-num" inputmode="numeric" @input="onDebit(l)" /></td>
                <td><input v-model="l.credit" :disabled="!editable" class="form-control form-control-sm fn-num" inputmode="numeric" @input="onCredit(l)" /></td>
                <td v-if="editable" class="text-nowrap">
                  <button class="btn btn-sm btn-link p-0 me-1" title="پروژه، مرکز هزینه، تعداد، ارز" @click="l.more = !l.more">بیشتر</button>
                  <button class="btn btn-sm btn-link text-danger p-0" @click="removeLine(i)">حذف</button>
                </td>
              </tr>
              <tr v-if="l.more">
                <td></td>
                <td colspan="7">
                  <div class="row g-2">
                    <div class="col-md-3"><label class="form-label small mb-0">پروژه / قرارداد</label><ContractPick v-model="l.contractId" :disabled="!editable" /></div>
                    <div class="col-md-3"><label class="form-label small mb-0">مرکز هزینه</label>
                      <select v-model="l.costCenterId" :disabled="!editable" class="form-select form-select-sm"><option :value="null">-</option><option v-for="c in costCenters" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select>
                    </div>
                    <div class="col-md-1"><label class="form-label small mb-0">تعداد</label><input v-model="l.quantity" :disabled="!editable" class="form-control form-control-sm fn-num" /></div>
                    <div class="col-md-1"><label class="form-label small mb-0">کد ارز</label><input v-model="l.currencyCode" :disabled="!editable" class="form-control form-control-sm inv-ltr" /></div>
                    <div class="col-md-2"><label class="form-label small mb-0">مبلغ ارزی</label><input v-model="l.currencyAmount" :disabled="!editable" class="form-control form-control-sm fn-num" /></div>
                    <div class="col-md-2"><label class="form-label small mb-0">نرخ</label><input v-model="l.rate" :disabled="!editable" class="form-control form-control-sm fn-num" /></div>
                  </div>
                </td>
              </tr>
            </template>
          </tbody>
          <tfoot>
            <tr class="fn-total">
              <td colspan="5">جمع</td>
              <td class="fn-num">{{ money(totalDebit) }}</td>
              <td class="fn-num">{{ money(totalCredit) }}</td>
              <td v-if="editable"></td>
            </tr>
          </tfoot>
        </table>
      </div>

      <div class="d-flex align-items-center gap-2 mb-3 flex-wrap">
        <span :class="balanced ? 'ok' : 'bad'" class="inv-badge">{{ balanced ? "متوازن" : `اختلاف: ${money(Math.abs(diff))} ریال ${diff > 0 ? "(بدهکار بیشتر)" : diff < 0 ? "(بستانکار بیشتر)" : ""}` }}</span>
        <template v-if="editable">
          <button class="btn btn-sm btn-outline-secondary" @click="addLine">+ ردیف</button>
          <button :disabled="diff === 0" class="btn btn-sm btn-outline-primary" @click="balance">تراز کردن (ثبت اختلاف در ردیف مقابل)</button>
        </template>
      </div>

      <div class="d-flex gap-2 flex-wrap">
        <button v-if="editable" :disabled="busy" class="btn btn-primary" @click="save">ذخیره پیش‌نویس</button>
        <template v-if="v && !isNew">
          <button v-if="canWrite && v.status === 0" :disabled="busy" class="btn btn-success" @click="act('check')">کنترل سند</button>
          <button v-if="canWrite && v.status === 1 && v.type !== 3" :disabled="busy" class="btn btn-outline-secondary" @click="act('uncheck')">بازگشت به پیش‌نویس</button>
          <button v-if="canFinalize && v.status === 1" :disabled="busy" class="btn btn-success" @click="act('finalize')">قطعی‌کردن سند</button>
          <button v-if="canWrite && v.status === 2 && ![1, 4, 6].includes(v.type) && !v.reversedByVoucherId" :disabled="busy" class="btn btn-outline-danger" @click="dialog = 'reverse'">صدور سند برگشتی</button>
          <button v-if="canWrite && v.status === 0 && v.type === 2" :disabled="busy" class="btn btn-outline-danger" @click="dialog = 'delete'">حذف پیش‌نویس</button>
        </template>
      </div>

      <div v-if="v && !isNew" class="inv-muted mt-3">
        ایجاد: {{ v.creatorName }} - {{ jalali(v.createdAt, true) }}
        <span v-if="v.checkedByName"> | کنترل: {{ v.checkedByName }} - {{ jalali(v.checkedAt, true) }}</span>
        <span v-if="v.finalizedByName"> | قطعی: {{ v.finalizedByName }} - {{ jalali(v.finalizedAt, true) }}</span>
        <span v-if="v.ceoApprovedAt"> | تایید مدیرعامل: {{ jalali(v.ceoApprovedAt, true) }}</span>
        | کنترل‌کننده و قطعی‌کننده نمی‌توانند تهیه‌کننده سند باشند.
      </div>
    </div>

    <PromptModal
      v-if="dialog === 'reverse'"
      :fields="[{ key: 'date', label: 'تاریخ سند برگشتی (خالی = تاریخ سند اصلی)', type: 'date' }, { key: 'note', label: 'علت برگشت', type: 'textarea', required: true }]"
      confirm-text="صدور سند برگشتی"
      title="سند برگشتی"
      @cancel="dialog = ''"
      @confirm="(x: any) => { dialog = ''; act('reverse', { date: x.date, note: x.note }); }" />
    <PromptModal v-if="dialog === 'delete'" :fields="[]" confirm-text="حذف" title="پیش‌نویس حذف شود؟" @cancel="dialog = ''" @confirm="remove" />
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
