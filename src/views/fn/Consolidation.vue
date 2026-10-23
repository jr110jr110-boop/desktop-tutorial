<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { getConsolidation, getLegalEntities } from "@/api/FnAPIs";
import { fail } from "@/composables/inventory";
import { money, moneyOrDash, parseAmount, yesNo } from "@/composables/fn";

// Consolidation (تلفیق): the balances of every active legal entity in the presentation currency, the intercompany eliminations and the consolidated balance per account.
const entities = ref<any[]>([]);
const filters = ref<{ From: string | null; To: string | null; IncludeDraft: boolean }>({ From: null, To: null, IncludeDraft: false });
const rates = ref<Record<number, string>>({});
const data = ref<any>(null);
const loading = ref(false);

const foreign = computed(() => {
  const main = entities.value.find((e) => e.id === 1) || entities.value[0];
  return entities.value.filter((e) => e.isActive && main && e.baseCurrency !== main.baseCurrency);
});
const entityCode = (id: number) => data.value?.entities.find((e: any) => e.entityId === id)?.code || id;
const amountOf = (row: any, id: number) => row.byEntity.find((x: any) => x.entityId === id)?.amount || 0;

async function load() {
  const rateText = Object.entries(rates.value).filter(([, v]) => parseAmount(v)).map(([id, v]) => `${id}=${parseAmount(v)}`).join(",");
  loading.value = true;
  const [d, error] = await getConsolidation({ ...filters.value, Rates: rateText });
  loading.value = false;
  if (error) {
    data.value = null;
    return fail(error);
  }
  data.value = d;
}

onMounted(async () => {
  const [e] = await getLegalEntities();
  entities.value = e || [];
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">گزارش تلفیقی</h1></div>
      <div class="inv-filters mb-2">
        <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.From" /></div>
        <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.To" /></div>
        <label class="form-check small"><input v-model="filters.IncludeDraft" class="form-check-input" type="checkbox" /> با اسناد غیرقطعی</label>
        <button :disabled="loading" class="btn btn-sm btn-primary" @click="load">{{ loading ? "..." : "نمایش گزارش" }}</button>
      </div>
      <div v-if="foreign.length" class="inv-filters mb-3">
        <div v-for="e in foreign" :key="e.id">
          <label class="form-label small mb-0">نرخ تبدیل {{ e.name }} ({{ e.baseCurrency }}) به ارز گزارش</label>
          <input v-model="rates[e.id]" class="form-control form-control-sm fn-num" />
        </div>
      </div>

      <template v-if="data">
        <p>
          ارز گزارش: <b class="inv-ltr">{{ data.presentationCurrency }}</b> -
          <span :class="data.balanced ? 'ok' : 'bad'" class="inv-badge">{{ data.balanced ? "تلفیق متوازن" : "تلفیق نامتوازن" }}</span>
        </p>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead>
              <tr><th>کد حساب</th><th>عنوان</th><th v-for="e in data.entities" :key="e.entityId">{{ e.code }} ({{ e.currency }})</th><th>جمع</th><th>حذف بین‌شرکتی</th><th>تلفیقی</th></tr>
            </thead>
            <tbody>
              <tr v-if="!data.rows.length"><td :colspan="5 + data.entities.length" class="text-center inv-muted">گردشی در این بازه نیست.</td></tr>
              <tr v-for="r in data.rows" :key="r.accountCode">
                <td class="inv-ltr">{{ r.accountCode }}</td><td>{{ r.accountTitle }}</td>
                <td v-for="e in data.entities" :key="e.entityId" class="fn-num">{{ moneyOrDash(amountOf(r, e.entityId)) }}</td>
                <td class="fn-num">{{ money(r.total) }}</td><td class="fn-num">{{ moneyOrDash(r.elimination) }}</td><td class="fn-num">{{ money(r.consolidated) }}</td>
              </tr>
              <tr class="fn-total"><td colspan="2">جمع (باید صفر شود)</td><td :colspan="data.entities.length"></td><td class="fn-num">{{ money(data.totalBefore) }}</td><td class="fn-num">{{ money(data.totalEliminations) }}</td><td class="fn-num">{{ money(data.totalConsolidated) }}</td></tr>
            </tbody>
          </table>
        </div>
        <div class="fn-section">مغایرت جفت حساب‌های بین‌شرکتی</div>
        <table class="table table-sm">
          <thead><tr><th>جفت حساب</th><th>مانده طرف الف</th><th>مانده طرف ب</th><th>اختلاف</th><th>تطبیق</th></tr></thead>
          <tbody>
            <tr v-if="!data.pairs.length"><td colspan="5" class="text-center inv-muted">جفت حسابی نیست.</td></tr>
            <tr v-for="p in data.pairs" :key="p.pairId" :class="{ 'table-danger': !p.matched }">
              <td>{{ p.title }}</td><td class="fn-num">{{ money(p.balanceA) }}</td><td class="fn-num">{{ money(p.balanceB) }}</td><td class="fn-num">{{ money(p.difference) }}</td><td>{{ yesNo(p.matched) }}</td>
            </tr>
          </tbody>
        </table>
        <p class="inv-muted">واحدها: <span v-for="e in data.entities" :key="e.entityId" class="me-2">{{ entityCode(e.entityId) }} (نرخ {{ e.rate }})</span> - مانده مثبت = بدهکار، منفی = بستانکار.</p>
      </template>
      <p v-else class="inv-muted">بازه را انتخاب و «نمایش گزارش» را بزنید.</p>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
