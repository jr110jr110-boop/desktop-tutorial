<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionQc, releaseNoteStatuses } from "@/composables/quality";

// Release note and certificate of conformity of a work order (no company form codes yet).
defineProps<{ note: any; kind: "note" | "coc" }>();
</script>

<template>
  <PrintForm
    class="q-form"
    :title="kind === 'coc' ? 'گواهی انطباق (Certificate of Conformity)' : 'برگه ترخیص'"
    :fields="[
      { label: 'شماره برگه', value: note.number, ltr: true },
      { label: 'دستور ساخت', value: note.workOrderCode },
      { label: 'وضعیت', value: releaseNoteStatuses[note.status] },
      { label: 'تاریخ صدور', value: jalali(note.releasedAt) },
      { label: 'شماره گواهی انطباق', value: note.certificateOfConformityNumber || '-', ltr: true },
      { label: 'صادرکننده', value: note.releasedByName || '-' },
    ]"
    :notes="[
      { label: 'اقلام', value: note.itemsText || '-' },
      { label: 'توضیحات', value: note.note || '-' },
    ]"
    :signatures="kind === 'coc' ? [{ title: 'کنترل کیفیت (صادرکننده)', name: note.releasedByName, date: jalali(note.releasedAt) }, { title: 'نماینده مدیریت (تضمین کیفیت)' }] : [{ title: 'کنترل کیفیت (صادرکننده)', name: note.releasedByName, date: jalali(note.releasedAt) }]"
    :distribution="distributionQc">
    <template v-if="kind === 'coc'">
      <p class="mt-1">
        بدینوسیله گواهی می‌شود اقلام یادشده برای دستور ساخت {{ note.workOrderCode }} مطابق با نقشه‌ها، مشخصات فنی و برنامه بازرسی و آزمون تاییدشده ساخته و بازرسی شده و
        تمام نقاط توقف و گزارش‌های الزامی آن بسته شده است.
      </p>
    </template>
    <table v-else>
      <thead><tr><th>بررسی</th><th>نتیجه</th><th>توضیح</th></tr></thead>
      <tbody>
        <tr v-for="c in note.checks" :key="c.code"><td>{{ c.title }}</td><td>{{ c.passed ? "برقرار" : "برقرار نیست" }}</td><td>{{ c.detail }}</td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
