<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { money } from "@/composables/fn";

// Balance confirmation letter sent to a customer (نامه تایید مانده حساب).
defineProps<{ letter: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="نامه تایید مانده حساب"
    :fields="[
      { label: 'شماره نامه', value: letter.number },
      { label: 'مخاطب', value: letter.customerName },
      { label: 'کد اقتصادی', value: letter.economicCode, ltr: true },
      { label: 'تاریخ مانده', value: jalali(letter.asOf) },
      { label: 'مانده طبق دفاتر ما (ریال)', value: money(letter.ledgerBalance) },
      { label: 'تعداد اسناد باز', value: letter.openDocuments },
    ]"
    :notes="[
      { label: 'متن', value: `با سلام و احترام؛ مانده حساب جنابعالی نزد شرکت ${letter.companyName || ''} در تاریخ ${jalali(letter.asOf)} مبلغ ${money(letter.ledgerBalance)} ریال است. خواهشمند است در صورت مطابقت با دفاتر خود، ذیل نامه را مهر و امضا کرده و بازگردانید و در صورت مغایرت، مبلغ و علت را اعلام فرمایید.` },
      { label: 'پاسخ مشتری', value: 'مانده فوق □ مورد تایید است     □ مغایرت دارد؛ مانده طبق دفاتر ما: ................ ریال     علت: ........................' },
    ]"
    :signatures="[{ title: 'مدیر مالی شرکت' }, { title: 'مهر و امضای مشتری' }]">
    <table>
      <thead><tr><th>تاریخ</th><th>نوع</th><th>شماره</th><th>شرح</th><th>بدهکار</th><th>بستانکار</th><th>مانده</th></tr></thead>
      <tbody>
        <tr v-for="(r, i) in letter.rows" :key="i"><td>{{ jalali(r.date) }}</td><td>{{ r.kind }}</td><td class="inv-ltr">{{ r.number }}</td><td>{{ r.description }}</td><td class="num inv-ltr">{{ money(r.debit) }}</td><td class="num inv-ltr">{{ money(r.credit) }}</td><td class="num inv-ltr">{{ money(r.balance) }}</td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
