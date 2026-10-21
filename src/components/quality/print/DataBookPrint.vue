<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { distributionQc } from "@/composables/quality";

// Cover and index of the final data book (no company form code yet). Variant: company copy or client copy.
defineProps<{ book: any; variant: "company" | "client" }>();
</script>

<template>
  <PrintForm
    class="q-form"
    :title="'دیتابوک نهایی - فهرست مطالب (' + (variant === 'client' ? 'نسخه کارفرما' : 'نسخه شرکت') + ')'"
    :fields="[
      { label: 'دستور ساخت', value: book.workOrderCode },
      { label: 'نسخه', value: variant === 'client' ? 'کارفرما' : 'شرکت' },
      { label: 'مدارک ناقص', value: book.missingCount },
    ]"
    :signatures="[{ title: 'کنترل کیفیت' }, { title: 'تضمین کیفیت' }]"
    :distribution="distributionQc">
    <table>
      <thead><tr><th>بخش</th><th>شماره</th><th>عنوان</th><th>وضعیت</th><th>فایل</th><th>الزامی</th><th>موجود</th></tr></thead>
      <tbody>
        <template v-for="s in book.sections" :key="s.no">
          <tr><td colspan="7"><b>{{ s.no }}. {{ s.name }}</b></td></tr>
          <tr v-for="(e, i) in s.entries" :key="s.no + '-' + i">
            <td></td><td class="inv-ltr">{{ e.number }}</td><td>{{ e.title }}</td><td>{{ e.status }}</td><td>{{ e.documentId ? "دارد" : "-" }}</td><td>{{ e.mandatory ? "بله" : "خیر" }}</td><td>{{ e.missing ? "ناقص" : "موجود" }}</td>
          </tr>
        </template>
      </tbody>
    </table>
  </PrintForm>
</template>
