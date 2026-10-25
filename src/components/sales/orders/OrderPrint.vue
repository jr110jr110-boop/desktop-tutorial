<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { orderStatuses, persianOf, qtyText, rial, rialText } from "@/composables/sales";

// Printed sales order (A4, RTL, B Zar through the .ks-report font of PrintForm). Printed with printSheet(); the company has not issued a form code for it yet.
defineProps<{ order: any }>();
</script>

<template>
  <PrintForm
    title="سفارش فروش"
    :fields="[
      { label: 'شماره سفارش', value: order.number, ltr: true },
      { label: 'تاریخ', value: persianOf(order.orderDateText, order.orderDate) },
      { label: 'مشتری', value: order.customerName },
      { label: 'شماره سفارش مشتری', value: order.customerOrderNumber, ltr: true },
      { label: 'تاریخ تحویل درخواستی', value: persianOf(order.requestedDeliveryDateText, order.requestedDeliveryDate) },
      { label: 'وضعیت', value: orderStatuses[order.status] },
    ]"
    :signatures="[{ title: 'کارشناس فروش' }, { title: 'مدیر فروش' }, { title: 'مدیر مالی (در صورت عبور از سقف اعتبار)' }]"
    distribution="نسخه ۱: فروش - نسخه ۲: انبار - نسخه ۳: مالی">
    <table>
      <thead><tr><th>ردیف</th><th>کد کالا</th><th>شرح کالا</th><th>واحد</th><th>تعداد</th><th>قیمت واحد (ریال)</th><th>تخفیف (٪)</th><th>مبلغ (ریال)</th></tr></thead>
      <tbody>
        <tr v-for="(l, i) in order.lines" :key="l.id || i">
          <td>{{ i + 1 }}</td>
          <td class="inv-ltr">{{ l.itemCode }}</td>
          <td>{{ l.itemName }}<div v-if="l.description" class="small">{{ l.description }}</div></td>
          <td>{{ l.unit }}</td>
          <td class="num">{{ qtyText(l.quantity) }}</td>
          <td class="num">{{ rial(l.unitPrice) }}</td>
          <td class="num">{{ qtyText(l.discountPercent) }}</td>
          <td class="num">{{ rial(l.amount) }}</td>
        </tr>
      </tbody>
      <tfoot><tr><td colspan="7">جمع خالص (ریال)</td><td class="num">{{ rialText(order.netAmount) }}</td></tr></tfoot>
    </table>
  </PrintForm>
</template>
