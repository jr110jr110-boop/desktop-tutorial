<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionTreasury, issuedChequeStatuses, money, receivedChequeStatuses } from "@/composables/fn";

// Cheque register (دفتر ثبت چک): received cheques (with their status) or issued cheques (by cheque book).
const props = defineProps<{ kind: "received" | "issued"; rows: any[]; subtitle?: string }>();
const total = () => props.rows.reduce((s, r) => s + (Number(r.amount) || 0), 0);
</script>

<template>
  <PrintForm
    class="q-form"
    :title="kind === 'received' ? 'دفتر ثبت چک‌های دریافتی' : 'دفتر ثبت چک‌های صادرشده'"
    :fields="[{ label: 'تعداد چک', value: rows.length }, { label: 'جمع مبالغ (ریال)', value: money(total()) }, ...(subtitle ? [{ label: 'فیلتر', value: subtitle }] : [])]"
    :signatures="[{ title: 'خزانه‌دار' }, { title: 'مدیر مالی' }]"
    :distribution="distributionTreasury">
    <table v-if="kind === 'received'">
      <thead><tr><th>ردیف</th><th>صیادی</th><th>سریال</th><th>بانک / شعبه</th><th>صاحب حساب</th><th>مشتری</th><th>تاریخ دریافت</th><th>سررسید</th><th>وضعیت</th><th>مبلغ (ریال)</th></tr></thead>
      <tbody>
        <tr v-for="(c, i) in rows" :key="c.id">
          <td>{{ i + 1 }}</td><td class="inv-ltr">{{ c.sayadId }}</td><td class="inv-ltr">{{ c.serial }}</td><td>{{ c.bankName }} {{ c.branch }}</td><td>{{ c.accountHolder }}</td><td>{{ c.customerName }}</td>
          <td>{{ jalali(c.receivedDate) }}</td><td>{{ jalali(c.dueDate) }}</td><td>{{ receivedChequeStatuses[c.status] }}</td><td class="num inv-ltr">{{ money(c.amount) }}</td>
        </tr>
      </tbody>
    </table>
    <table v-else>
      <thead><tr><th>ردیف</th><th>حساب بانکی</th><th>سری</th><th>سریال</th><th>صیادی</th><th>در وجه</th><th>سررسید</th><th>وضعیت</th><th>مبلغ (ریال)</th></tr></thead>
      <tbody>
        <tr v-for="(c, i) in rows" :key="c.id">
          <td>{{ i + 1 }}</td><td>{{ c.bankAccountTitle }}</td><td class="inv-ltr">{{ c.series }}</td><td class="inv-ltr">{{ c.serial }}</td><td class="inv-ltr">{{ c.sayadId }}</td><td>{{ c.payeeName }}</td>
          <td>{{ c.dueDate ? jalali(c.dueDate) : "" }}</td><td>{{ issuedChequeStatuses[c.status] }}</td><td class="num inv-ltr">{{ c.amount ? money(c.amount) : "" }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
