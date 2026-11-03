<script setup lang="ts">
// The treasury documents (cash legs) of a loan, a letter of credit or a bank guarantee that are not settled yet (R1t D7): the money moves only when treasury
// approves and posts the document, so the facility shows its open documents with a link to the treasury list.
import { legActions } from "@/composables/fn";
import { money } from "@/composables/fn";
import { docStatuses } from "@/composables/fn";
import { jalali } from "@/composables/inventory";

defineProps<{ legs?: any[] }>();
</script>

<template>
  <div v-if="legs?.length" class="alert alert-warning py-2 small">
    <div class="fw-bold mb-1">اسناد خزانه‌داری باز (پول فقط پس از تایید مدیر مالی و ثبت نهایی خزانه‌دار جابه‌جا می‌شود):</div>
    <ul class="mb-0">
      <li v-for="l in legs" :key="l.kind + l.id">
        <router-link :to="{ name: l.kind === 'Receipt' ? 'FnReceipts' : 'FnPayments' }">{{ l.kind === "Receipt" ? "سند دریافت" : "سند پرداخت" }} <span class="inv-ltr">{{ l.number }}</span></router-link>
        — {{ legActions[l.action] || l.action }} — {{ money(l.amount) }} ریال — {{ jalali(l.date) }} — {{ docStatuses[l.status] }}
      </li>
    </ul>
  </div>
</template>
