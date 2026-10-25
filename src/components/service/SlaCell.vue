<script setup lang="ts">
// SLA instants of a service request (Persian date + HH:mm, as sent by the API) with the breach badges.
// Empty due times mean no SLA rule exists for the priority (slaMissing).
defineProps({ r: { type: Object, required: true } });
</script>

<template>
  <div class="small">
    <template v-if="r.slaMissing"><span class="inv-badge">بدون SLA</span></template>
    <template v-else>
      <div>پاسخ تا: {{ r.responseDueText || "-" }} <span v-if="r.responseBreached" class="inv-badge bad">تخطی</span></div>
      <div>رفع تا: {{ r.resolutionDueText || "-" }} <span v-if="r.resolutionBreached" class="inv-badge bad">تخطی</span></div>
    </template>
  </div>
</template>
