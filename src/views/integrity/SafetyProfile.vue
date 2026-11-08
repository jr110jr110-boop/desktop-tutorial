<script setup lang="ts">
// «پروفایل سخت‌گیرانه تولید» (Package D): the settings that weaken a control (revision requirement of transmittals, receipt and price tolerances, purchase-order rule, close checklist,
// AI seats, SMS and e-mail off, ...) with their strict value and the value now; a deviation is highlighted. A security-relevant setting is never changed from its own page:
// here a person writes a request with a reason, and a SECOND person (PMO or CEO, not the requester, never an AI seat) approves it; then the value is applied. Both are in the audit chain.
import { computed, onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import { cancelSafetyChange, decideSafetyChange, getSafetyChanges, getSafetyProfile, requestSafetyChange, type SafetyChange, type SafetyItem } from "@/api/IntegrityAPIs";
import { fail, ok } from "@/composables/inventory";
import { hasRole, IntegrityRoles } from "@/composables/roles";

const storeTitles: Record<string, string> = {
  ProjectControl: "کنترل پروژه",
  ProductBank: "بانک محصول و BOM",
  Procurement: "تدارکات",
  Finance: "مالی",
  Quality: "کیفیت",
  AiSeat: "دستیارهای هوش مصنوعی",
};
const canApprove = hasRole(IntegrityRoles.approvers);
const items = ref<SafetyItem[]>([]);
const pending = ref<SafetyChange[]>([]);
const history = ref<SafetyChange[]>([]);
const onlyDeviations = ref(false);
const busy = ref(false);
const form = ref<{ item: SafetyItem | null; value: string; reason: string }>({ item: null, value: "", reason: "" });
const decision = ref<{ change: SafetyChange | null; note: string }>({ change: null, note: "" });

const shown = computed(() => items.value.filter((i) => !onlyDeviations.value || i.deviates));
const deviations = computed(() => items.value.filter((i) => i.deviates).length);
const weaker = computed(() => items.value.filter((i) => i.deviates && i.weaker).length);
const flagText = (v: string | null) => (v === "1" || v === "true" ? "روشن" : v === "0" || v === "false" ? "خاموش" : v ?? "-");

async function load() {
  const [profile, error] = await getSafetyProfile();
  if (error) return fail(error);
  items.value = profile || [];
  const [open] = await getSafetyChanges({ Status: "Pending", PageSize: 0 });
  pending.value = open?.data || [];
  const [done] = await getSafetyChanges({ PageNumber: 1, PageSize: 15 });
  history.value = (done?.data || []).filter((c: SafetyChange) => c.status !== "Pending");
}

function ask(item: SafetyItem) {
  form.value = { item, value: item.strict, reason: "" };
}

async function send() {
  const f = form.value;
  if (!f.item) return;
  busy.value = true;
  const [, error] = await requestSafetyChange({ store: f.item.store, key: f.item.key, value: f.value, reason: f.reason });
  busy.value = false;
  if (error) return fail(error);
  ok("درخواست ثبت شد و منتظر تایید نفر دوم است.");
  form.value = { item: null, value: "", reason: "" };
  load();
}

async function decide(approve: boolean) {
  const d = decision.value;
  if (!d.change) return;
  busy.value = true;
  const [, error] = await decideSafetyChange(d.change.id, approve, d.note || null);
  busy.value = false;
  if (error) return fail(error);
  ok(approve ? "تایید شد و تغییر اعمال شد." : "درخواست رد شد.");
  decision.value = { change: null, note: "" };
  load();
}

async function cancel(c: SafetyChange) {
  busy.value = true;
  const [, error] = await cancelSafetyChange(c.id);
  busy.value = false;
  if (error) return fail(error);
  ok("درخواست لغو شد.");
  load();
}

onMounted(load);
</script>

<template>
  <div class="inv-page" dir="rtl">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">پروفایل سخت‌گیرانه تولید</h1>
        <RouterLink :to="{ name: 'Integrity' }" class="btn btn-sm btn-outline-secondary">سلامت سوابق</RouterLink>
      </div>
      <p class="inv-muted small">
        تنظیماتی که کنترل‌ها را سست می‌کنند فقط با درخواست و دلیل و تایید نفر دوم (PMO یا مدیرعامل؛ نه درخواست‌دهنده و نه هوش مصنوعی) تغییر می‌کنند و هر تغییر در زنجیره سوابق ثبت می‌شود.
        مقدار پیش‌فرض نرم‌افزار تغییر نکرده است؛ این صفحه فاصله هر تنظیم با مقدار سخت‌گیرانه را نشان می‌دهد.
      </p>
      <div class="d-flex gap-3 align-items-center mb-2">
        <span :class="deviations ? 'warn' : 'ok'" class="inv-badge">{{ deviations }} انحراف از پروفایل</span>
        <span v-if="weaker" class="inv-badge bad">{{ weaker }} مورد سست‌تر از مقدار سخت‌گیرانه</span>
        <label class="form-check mb-0"><input v-model="onlyDeviations" class="form-check-input" type="checkbox" /> <span class="form-check-label">فقط انحراف‌ها</span></label>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>بخش</th><th>تنظیم</th><th>مقدار سخت‌گیرانه</th><th>مقدار فعلی</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!shown.length"><td colspan="6" class="text-center inv-muted">تنظیمی برای نمایش نیست.</td></tr>
            <tr v-for="i in shown" :key="i.store + i.key" :class="{ 'table-warning': i.deviates }">
              <td>{{ storeTitles[i.store] || i.store }}</td>
              <td>{{ i.title }}<div class="inv-ltr inv-muted small">{{ i.key }}</div><div v-if="i.note" class="inv-muted small">{{ i.note }}</div></td>
              <td>{{ flagText(i.strict) }}</td>
              <td>{{ flagText(i.current) }}</td>
              <td>
                <span v-if="!i.deviates" class="inv-badge ok">مطابق</span>
                <span v-else :class="i.weaker ? 'bad' : 'warn'" class="inv-badge">{{ i.weaker ? "سست‌تر" : "متفاوت" }}</span>
                <span v-if="i.pendingRequestId" class="inv-badge warn ms-1">درخواست باز</span>
              </td>
              <td><button :disabled="!!i.pendingRequestId" class="btn btn-sm btn-outline-primary" @click="ask(i)">درخواست تغییر</button></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div v-if="form.item" class="inv-card">
      <h2 class="inv-title">درخواست تغییر «{{ form.item.title }}»</h2>
      <div class="inv-filters mb-2">
        <div>
          <label class="form-label small mb-0">مقدار تازه</label>
          <input v-model="form.value" class="form-control form-control-sm inv-ltr" />
          <div class="inv-muted small">مقدار فعلی: {{ flagText(form.item.current) }}؛ سخت‌گیرانه: {{ flagText(form.item.strict) }}. برای روشن/خاموش 1 یا 0 (در بانک محصول true یا false).</div>
        </div>
        <div class="flex-grow-1">
          <label class="form-label small mb-0">دلیل (الزامی)</label>
          <input v-model="form.reason" class="form-control form-control-sm" maxlength="1000" />
        </div>
      </div>
      <button :disabled="busy" class="btn btn-sm btn-primary me-2" @click="send">ثبت درخواست</button>
      <button class="btn btn-sm btn-outline-secondary" @click="form.item = null">انصراف</button>
    </div>

    <div class="inv-card">
      <h2 class="inv-title">درخواست‌های منتظر تایید نفر دوم</h2>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>زمان</th><th>تنظیم</th><th>از</th><th>به</th><th>درخواست‌دهنده</th><th>دلیل</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!pending.length"><td colspan="7" class="text-center inv-muted">درخواست بازی نیست.</td></tr>
            <tr v-for="c in pending" :key="c.id">
              <td>{{ c.requestedAtText }}</td>
              <td>{{ c.title }}<span v-if="c.weakening" class="inv-badge bad ms-1">سست‌کننده</span></td>
              <td>{{ flagText(c.oldValue) }}</td>
              <td>{{ flagText(c.newValue) }}</td>
              <td>{{ c.requestedByName }}</td>
              <td>{{ c.reason }}</td>
              <td class="text-nowrap">
                <button v-if="canApprove" class="btn btn-sm btn-outline-primary me-1" @click="decision = { change: c, note: '' }">بررسی</button>
                <button class="btn btn-sm btn-outline-secondary" @click="cancel(c)">لغو (درخواست‌دهنده)</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-if="decision.change" class="border rounded p-3 mt-2">
        <div><b>{{ decision.change.title }}</b>: {{ flagText(decision.change.oldValue) }} ← {{ flagText(decision.change.newValue) }} — {{ decision.change.reason }}</div>
        <input v-model="decision.note" class="form-control form-control-sm my-2" placeholder="توضیح (برای رد الزامی است)" />
        <button :disabled="busy" class="btn btn-sm btn-success me-2" @click="decide(true)">تایید و اعمال</button>
        <button :disabled="busy" class="btn btn-sm btn-danger me-2" @click="decide(false)">رد</button>
        <button class="btn btn-sm btn-outline-secondary" @click="decision.change = null">بستن</button>
        <div class="inv-muted small mt-1">تایید توسط خود درخواست‌دهنده پذیرفته نمی‌شود.</div>
      </div>
    </div>

    <div class="inv-card">
      <h2 class="inv-title">آخرین تصمیم‌ها</h2>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>تنظیم</th><th>از</th><th>به</th><th>درخواست‌دهنده</th><th>تصمیم‌گیرنده</th><th>زمان تصمیم</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!history.length"><td colspan="7" class="text-center inv-muted">هنوز تصمیمی ثبت نشده است.</td></tr>
            <tr v-for="c in history" :key="c.id">
              <td>{{ c.title }}</td>
              <td>{{ flagText(c.oldValue) }}</td>
              <td>{{ flagText(c.newValue) }}</td>
              <td>{{ c.requestedByName }}</td>
              <td>{{ c.decidedByName || "-" }}</td>
              <td>{{ c.decidedAtText || "-" }}</td>
              <td>{{ c.statusText }}<div v-if="c.decisionNote" class="inv-muted small">{{ c.decisionNote }}</div></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>
