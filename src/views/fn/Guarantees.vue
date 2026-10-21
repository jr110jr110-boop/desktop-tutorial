<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import ContractPick from "@/components/fn/ContractPick.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import QPager from "@/components/quality/QPager.vue";
import { getGuarantees, guaranteeAction, saveGuarantee } from "@/api/FnAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { guaranteeDirections, guaranteeKinds, guaranteeStatuses, money, parseAmount, usePagedList } from "@/composables/fn";
import { contractLabel } from "@/composables/projectControl";
import { FnRoles, hasRole } from "@/composables/roles";

// Guarantee cheques and promissory notes, received from customers or issued to employers (a held guarantee is an off-balance record).
const filters = ref({ Kind: null as number | null, Contains: "" });
const list = usePagedList(getGuarantees, filters);
const form = ref<any>(null);
const dialog = ref<null | { g: any; action: string }>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.treasuryAndAccountants);

async function save() {
  const f = form.value;
  if (!String(f.partyName).trim()) return fail("نام طرف حساب را وارد کنید.");
  const amount = parseAmount(f.amount);
  if (!amount || amount <= 0) return fail("مبلغ را وارد کنید.");
  busy.value = true;
  const [, error] = await saveGuarantee({ kind: Number(f.kind), direction: Number(f.direction), partyName: f.partyName, contractId: f.contractId || null, amount, date: f.date, dueDate: f.dueDate, reference: f.reference || null });
  busy.value = false;
  if (error) return fail(error);
  ok("ضمانت‌نامه ثبت شد.");
  form.value = null;
  list.load();
}

async function close(v: any) {
  const d = dialog.value!;
  dialog.value = null;
  const [, error] = await guaranteeAction({ id: d.g.id, action: d.action, date: v.date });
  if (error) return fail(error);
  ok("ضمانت‌نامه بسته شد.");
  list.load();
}

onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">چک و سفته‌های تضمینی</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = { kind: 1, direction: 1, partyName: '', contractId: null, amount: '', date: today(), dueDate: null, reference: '' }">ثبت ضمانت‌نامه</button>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Kind" class="form-select form-select-sm"><option :value="null">دریافتی و صادره</option><option v-for="(t, k) in guaranteeDirections" :key="k" :value="Number(k)">{{ t }}</option></select>
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره، طرف حساب یا مرجع" @keydown.enter="list.search" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>نوع</th><th>جهت</th><th>طرف حساب</th><th>پروژه</th><th>مبلغ (ریال)</th><th>تاریخ</th><th>سررسید</th><th>مرجع</th><th>وضعیت</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="11" class="text-center inv-muted">ضمانت‌نامه‌ای نیست.</td></tr>
            <tr v-for="g in list.rows.value" :key="g.id">
              <td class="inv-ltr">{{ g.number }}</td><td>{{ guaranteeKinds[g.kind] }}</td><td>{{ guaranteeDirections[g.direction] }}</td><td>{{ g.partyName }}</td><td>{{ g.contractId ? contractLabel(g.contractId) : "" }}</td>
              <td class="fn-num">{{ money(g.amount) }}</td><td>{{ jalali(g.date) }}</td><td>{{ g.dueDate ? jalali(g.dueDate) : "" }}</td><td class="inv-ltr">{{ g.reference }}</td>
              <td><span :class="g.status === 1 ? 'info' : 'ok'" class="inv-badge">{{ guaranteeStatuses[g.status] }}</span><span v-if="g.closedDate" class="inv-muted me-1">{{ jalali(g.closedDate) }}</span></td>
              <td v-if="canWrite" class="text-nowrap">
                <template v-if="g.status === 1">
                  <button class="btn btn-sm btn-outline-secondary me-1" @click="dialog = { g, action: 'return' }">استرداد</button>
                  <button class="btn btn-sm btn-outline-danger" @click="dialog = { g, action: 'collect' }">وصول (ضبط)</button>
                </template>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <PromptModal v-if="dialog" :fields="[{ key: 'date', label: 'تاریخ', type: 'date', value: today() }]" :title="(dialog.action === 'return' ? 'استرداد ضمانت‌نامه ' : 'وصول ضمانت‌نامه ') + dialog.g.number" @cancel="dialog = null" @confirm="close" />

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">ثبت ضمانت‌نامه</h2>
        <div class="row g-2">
          <div class="col-6"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in guaranteeKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">جهت</label><select v-model="form.direction" class="form-select form-select-sm"><option v-for="(t, k) in guaranteeDirections" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">طرف حساب *</label><input v-model="form.partyName" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">پروژه / قرارداد</label><ContractPick v-model="form.contractId" /></div>
          <div class="col-6"><label class="form-label small mb-0">مبلغ (ریال) *</label><input v-model="form.amount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-6"><label class="form-label small mb-0">مرجع (شماره چک / سفته)</label><input v-model="form.reference" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-6"><label class="form-label small mb-0">تاریخ</label><FnDate v-model="form.date" /></div>
          <div class="col-6"><label class="form-label small mb-0">سررسید</label><FnDate v-model="form.dueDate" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
