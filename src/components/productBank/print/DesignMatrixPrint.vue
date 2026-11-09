<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { inputKinds, reviewOutcomes, reviewTypes, revisionText, traceResults } from "@/composables/designControl";

// Traceability matrix of a design review: every design input against every output with the judged result and its evidence. A4, B Zar through PrintForm.
const props = defineProps<{ matrix: any; review: any }>();
const cell = (row: any, outputId: number) => (row.cells || []).find((c: any) => c.outputId === outputId);
const distribution = "توزیع نسخ: ۱- مهندسی ۲- تضمین کیفیت ۳- پرونده طراحی محصول";
</script>

<template>
  <PrintForm
    class="q-form"
    :title="'ماتریس ردیابی ' + reviewTypes[matrix.type]"
    :fields="[
      { label: 'بازبینی', value: matrix.reviewNumber, ltr: true },
      { label: 'محصول', value: matrix.productCode, ltr: true },
      { label: 'بازنگری', value: revisionText(matrix.productRevision), ltr: true },
      { label: 'نتیجه', value: reviewOutcomes[matrix.outcome] },
      { label: 'تاریخ برگزاری', value: jalali(props.review?.heldDate) },
      { label: 'تاریخ بستن', value: jalali(props.review?.closedAt) },
    ]"
    :notes="[{ label: 'عنوان', value: matrix.reviewTitle }]"
    :signatures="[
      { title: 'بسته‌کننده بازبینی', name: props.review?.closedByName, date: props.review?.closedAt ? jalali(props.review.closedAt) : '' },
    ]"
    :distribution="distribution">
    <table>
      <thead>
        <tr><th>ورودی</th><th>نوع</th><th>الزام</th><th>معیار پذیرش</th><th v-for="o in matrix.outputs" :key="o.id">{{ o.label }}<br />{{ o.revisionText }}</th></tr>
      </thead>
      <tbody>
        <tr v-for="r in matrix.rows" :key="r.input.inputId">
          <td>{{ r.input.code }}</td><td>{{ inputKinds[r.input.kind] }}</td><td>{{ r.input.statement }}</td><td>{{ r.input.acceptanceCriterion }}</td>
          <td v-for="o in matrix.outputs" :key="o.id">
            <b>{{ traceResults[cell(r, o.id)?.result ?? 0] }}</b>
            <div v-if="cell(r, o.id)?.evidence">{{ cell(r, o.id).evidence }} <span v-if="cell(r, o.id).evidenceRef">({{ cell(r, o.id).evidenceRef }})</span></div>
            <div v-if="cell(r, o.id)?.judgedByName">{{ cell(r, o.id).judgedByName }}</div>
          </td>
        </tr>
      </tbody>
    </table>
    <p v-if="matrix.uncovered.length" class="mt-2"><b>ورودی‌های بدون پوشش:</b> {{ matrix.uncovered.map((u: any) => u.code + " (" + u.reason + ")").join("؛ ") }}</p>
  </PrintForm>
</template>
