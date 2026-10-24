<script setup lang="ts">
// The ledger entries behind a statement line (account prefix): each row opens its voucher.
import { onMounted, ref } from "vue";
import { getStatementDrill } from "@/api/FnAPIs";
import { fail, jalali } from "@/composables/inventory";
import { money, moneyOrDash } from "@/composables/fn";

const props = defineProps<{ account: string; params: any }>();
const emit = defineEmits(["close"]);
const data = ref<any>(null);

onMounted(async () => {
  const [d, error] = await getStatementDrill(props.account, props.params);
  if (error) {
    fail(error);
    emit("close");
    return;
  }
  data.value = d;
});
</script>

<template>
  <div class="inv-modal-backdrop screen-only" @click.self="emit('close')">
    <div class="inv-modal wide">
      <h2 class="inv-title mb-2">ریز گردش حساب <span class="inv-ltr">{{ account }}</span></h2>
      <p v-if="!data" class="inv-muted">در حال بارگذاری...</p>
      <template v-else>
        <p>جمع بدهکار: <b>{{ money(data.totalDebit) }}</b> - جمع بستانکار: <b>{{ money(data.totalCredit) }}</b> ریال</p>
        <div class="table-responsive" style="max-height: 420px; overflow: auto">
          <table class="table table-sm">
            <thead><tr><th>تاریخ</th><th>سند</th><th>حساب</th><th>شرح</th><th>بدهکار</th><th>بستانکار</th><th>منبع</th></tr></thead>
            <tbody>
              <tr v-if="!data.rows.length"><td colspan="7" class="text-center inv-muted">گردشی نیست.</td></tr>
              <tr v-for="(r, i) in data.rows" :key="i">
                <td>{{ jalali(r.date) }}</td>
                <td><router-link :to="'/fn/vouchers/' + r.voucherId">{{ r.number ?? "موقت " + r.temporaryNumber }}</router-link></td>
                <td class="inv-ltr">{{ r.accountCode }}</td><td>{{ r.description }}</td><td class="fn-num">{{ moneyOrDash(r.debit) }}</td><td class="fn-num">{{ moneyOrDash(r.credit) }}</td><td>{{ r.sourceType }} {{ r.sourceNumber }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
      <div class="d-flex justify-content-end mt-2"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">بستن</button></div>
    </div>
  </div>
</template>
