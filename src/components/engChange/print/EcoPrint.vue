<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, num } from "@/composables/inventory";
import { changeActions, changeKinds, ecClasses, ecUrgencies, impactCategories } from "@/composables/engChange";

// Printed engineering change order with the impact lists and the disposition of every item (A4, B Zar through PrintForm).
const props = defineProps<{ order: any }>();
const counts = () => (props.order.impact?.counts || []) as any[];
const active = () => (props.order.dispositions || []).filter((d: any) => !d.stale);
</script>

<template>
  <PrintForm
    title="دستور تغییر مهندسی (ECO)"
    :fields="[
      { label: 'شماره', value: order.number, ltr: true },
      { label: 'درخواست', value: order.requestNumber, ltr: true },
      { label: 'عنوان', value: order.title },
      { label: 'وضعیت', value: order.statusText },
      { label: 'کلاس', value: ecClasses[order.classification] },
      { label: 'فوریت', value: ecUrgencies[order.urgency] },
      { label: 'نقطه اجرا (Effectivity)', value: order.effectivity || '-' },
      { label: 'قطعات', value: order.parts || '-', ltr: true },
    ]"
    :notes="[
      { label: 'دلیل', value: order.requestReason },
      { label: 'خلاصه', value: order.summary || '-' },
      { label: 'توضیح نقطه اجرا', value: order.effectivityNote || '-' },
      { label: 'اثر انگشت تحلیل اثر (SHA-256)', value: order.impact ? order.impact.sha256 : '-', ltr: true },
    ]"
    :signatures="[
      { title: 'سازنده', name: order.creatorName, date: jalali(order.createdAt) },
      { title: 'تایید مهندسی', hint: '(مدیر مهندسی)', name: order.engineeringApprovedByName, date: order.engineeringApprovedAt ? jalali(order.engineeringApprovedAt) : '' },
      { title: 'تایید تضمین کیفیت', hint: '(QA)', name: order.qaApprovedByName, date: order.qaApprovedAt ? jalali(order.qaApprovedAt) : '' },
    ]"
    distribution="توزیع نسخ: ۱- مهندسی ۲- تضمین کیفیت ۳- مدیریت پروژه (PMO) ۴- مدیرعامل (اطلاع) ۵- مدیران پروژه متاثر (اطلاع)">
    <h3>بازنگری‌های این دستور</h3>
    <table>
      <thead><tr><th>نوع</th><th>عمل</th><th>مورد</th><th>از بازنگری</th><th>به بازنگری</th></tr></thead>
      <tbody>
        <tr v-if="!order.changes.length"><td colspan="5">-</td></tr>
        <tr v-for="c in order.changes" :key="c.id"><td>{{ changeKinds[c.kind] }}</td><td>{{ changeActions[c.action] }}</td><td>{{ c.reference }}</td><td>{{ c.oldRevision ?? "-" }}</td><td>{{ c.newRevision ?? "-" }}</td></tr>
      </tbody>
    </table>
    <h3>تحلیل اثر (محاسبه‌شده توسط سیستم)</h3>
    <table>
      <thead><tr><th>فهرست</th><th>تعداد</th><th>دارای تکلیف</th></tr></thead>
      <tbody><tr v-for="c in counts()" :key="c.category"><td>{{ impactCategories[c.category] }}</td><td>{{ num(c.count) }}</td><td>{{ num(c.decided) }}</td></tr></tbody>
    </table>
    <h3>تکلیف هر مورد</h3>
    <table>
      <thead><tr><th>فهرست</th><th>مورد</th><th>تکلیف</th><th>مسئول</th><th>مهلت</th><th>توضیح</th></tr></thead>
      <tbody>
        <tr v-if="!active().length"><td colspan="6">مورد اثر‌پذیری پیدا نشد.</td></tr>
        <tr v-for="d in active()" :key="d.id">
          <td>{{ d.categoryTitle }}</td><td>{{ d.text }}</td><td>{{ d.dispositionTitle || "—" }}</td><td>{{ d.ownerName }}</td><td>{{ d.dueDate ? jalali(d.dueDate) : "" }}</td><td>{{ d.note }}</td>
        </tr>
      </tbody>
    </table>
    <h3 v-if="order.tasks.length">تکلیف‌های اجرایی</h3>
    <table v-if="order.tasks.length">
      <thead><tr><th>تکلیف</th><th>مسئول</th><th>مهلت</th><th>وضعیت</th><th>تایید QA</th></tr></thead>
      <tbody><tr v-for="t in order.tasks" :key="t.id"><td>{{ t.title }}</td><td>{{ t.ownerName }}</td><td>{{ jalali(t.dueDate) }}</td><td>{{ t.statusText }}</td><td>{{ t.verifiedByName || "" }}</td></tr></tbody>
    </table>
  </PrintForm>
</template>
