<script setup lang="ts">
// Printed minutes (صورتجلسه): A4, right to left, B Zar (PrintForm + report.scss). Persian dates only.
import PrintForm from "@/components/print/PrintForm.vue";
import { dateText, dateTimeText, decisionStatuses, meetingKinds, meetingStatuses } from "@/composables/office";

defineProps<{ m: any }>();
const who = (a: any) => [a.positionTitle || a.externalName, a.userName].filter(Boolean).join(" - ") || "-";
</script>

<template>
  <PrintForm
    title="صورتجلسه"
    :fields="[
      { label: 'شماره جلسه', value: m.number, ltr: true },
      { label: 'وضعیت', value: meetingStatuses[m.status] },
      { label: 'عنوان', value: m.title },
      { label: 'نوع', value: meetingKinds[m.kind] },
      { label: 'زمان شروع', value: m.startAtText, ltr: true },
      { label: 'پایان', value: m.endAt ? dateTimeText(m.endAt) : '', ltr: true },
      { label: 'محل', value: m.place },
      { label: 'رئیس جلسه', value: m.chairPositionTitle },
      { label: 'دبیر جلسه', value: m.secretaryPositionTitle },
      { label: 'تاریخ تایید صورتجلسه', value: m.minutesApprovedAtText, ltr: true },
    ]"
    :signatures="[
      { title: 'دبیر جلسه', hint: m.secretaryPositionTitle },
      { title: 'رئیس جلسه', hint: m.chairPositionTitle, name: m.minutesApprovedByName, date: m.minutesApprovedAtText },
    ]">
    <h3 class="mp-h">حاضران</h3>
    <table>
      <thead><tr><th>ردیف</th><th>نام / سمت</th><th>حضور</th><th>امضای صورتجلسه</th></tr></thead>
      <tbody>
        <tr v-for="(a, i) in m.attendees" :key="i"><td>{{ i + 1 }}</td><td>{{ who(a) }}</td><td>{{ a.present ? "حاضر" : "غایب" }}</td><td class="inv-ltr">{{ a.signedAtText }}</td></tr>
      </tbody>
    </table>

    <h3 class="mp-h">دستور جلسه و مذاکرات</h3>
    <table>
      <thead><tr><th style="width: 3rem">بند</th><th>موضوع</th><th>خلاصه مذاکرات</th></tr></thead>
      <tbody>
        <tr v-for="a in m.agenda" :key="a.seq"><td>{{ a.seq }}</td><td>{{ a.title }}</td><td style="white-space: pre-wrap">{{ a.minutes }}</td></tr>
      </tbody>
    </table>

    <h3 class="mp-h">مصوبات</h3>
    <table>
      <thead><tr><th style="width: 3rem">ردیف</th><th>مصوبه</th><th>مسئول اجرا</th><th>مهلت</th><th>وضعیت</th></tr></thead>
      <tbody>
        <tr v-if="!(m.decisions || []).length"><td colspan="5">مصوبه‌ای ثبت نشده است.</td></tr>
        <tr v-for="(d, i) in m.decisions" :key="i">
          <td>{{ d.seq || i + 1 }}</td><td style="white-space: pre-wrap">{{ d.text }}</td><td>{{ d.ownerPositionTitle }}</td><td class="inv-ltr">{{ d.dueDateText || dateText(d.dueDate) }}</td><td>{{ decisionStatuses[d.status] }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>

<style scoped>
.mp-h { font-size: 1rem; margin: 0.8rem 0 0.3rem; }
</style>
