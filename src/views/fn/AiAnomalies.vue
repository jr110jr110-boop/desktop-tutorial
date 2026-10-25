<script setup lang="ts">
// «یادداشت‌های ناهنجاری مالی»: the findings of the seat FIN-ANOMALY (duplicate or round postings, postings on a day off, unmatched bank lines, a changed IBAN of a supplier) for the finance roles.
// The seat only reports; accepting or dismissing a note closes it and never changes a voucher, a bank line or a supplier.
import AiNotesList from "@/components/ai/AiNotesList.vue";
import { financeNoteTypes } from "@/composables/ai";
</script>

<template>
  <div class="inv-page">
    <AiNotesList
      :types="financeNoteTypes"
      :with-seat="false"
      with-rule
      description="موارد مشکوک که دستیار مالی در سندها، صورت‌حساب بانکی و تغییر شبای تامین‌کنندگان یافته است. دستیار فقط گزارش می‌دهد؛ پذیرش یا ردِ یادداشت آن را می‌بندد و سند، ردیف بانکی یا تامین‌کننده را تغییر نمی‌دهد."
      title="یادداشت‌های ناهنجاری مالی" />
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
