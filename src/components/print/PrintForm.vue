<script setup lang="ts">
// A4 printed form modelled on the company's warehouse forms (receipt, issue, delivery):
// logo and title, a box with the number / date / parties, the table of lines (default slot),
// optional text, the signature boxes and the footer with the distribution of the copies and the form code.
// Shown only when printing (class print-only); the screen has its own page.
import { computed } from "vue";
import Logo from "@/assets/img/logo.png";
import { NO_FORM_CODE } from "@/composables/inventory";

export interface FormField {
  label: string;
  value?: string | number | null;
  ltr?: boolean;
}
export interface FormSignature {
  title: string;
  /** small text under the title, for example "(مطابق نقشه)" */
  hint?: string;
  /** name of the person who signed in the system (never typed by hand) */
  name?: string | null;
  date?: string | null;
}

const props = defineProps({
  title: { type: String, required: true },
  /** Form code of the company; empty means the form has no code yet. */
  formCode: { type: String, default: "" },
  /** Short fields in a two column grid (document number, date, warehouse, ...). */
  fields: { type: Array as () => FormField[], default: () => [] },
  /** Long fields, one per row (address, description). */
  notes: { type: Array as () => FormField[], default: () => [] },
  signatures: { type: Array as () => FormSignature[], default: () => [] },
  distribution: { type: String, default: "" },
  /** The form is printed on screen too (used by the preview of a report page). */
  printOnly: { type: Boolean, default: true },
});

const codeText = computed(() => (props.formCode ? props.formCode : NO_FORM_CODE));
const hasCode = computed(() => !!props.formCode);
const colsStyle = computed(() => ({ "--cols": String(props.signatures.length) } as any));
</script>

<template>
  <div :class="{ 'print-only': printOnly }" class="print-form ks-report">
    <div class="pf-head">
      <h1 class="pf-title">{{ title }}</h1>
      <img :src="Logo" alt="" class="pf-logo" />
    </div>

    <div v-if="fields.length || notes.length" class="pf-box">
      <div v-if="fields.length" class="pf-grid">
        <div v-for="(f, i) in fields" :key="i" class="pf-field">
          <b>{{ f.label }}:</b>
          <span :class="{ 'inv-ltr': f.ltr }"> {{ f.value === null || f.value === undefined || f.value === "" ? "" : f.value }}</span>
        </div>
      </div>
      <div v-for="(f, i) in notes" :key="'n' + i" class="pf-note">
        <b>{{ f.label }}:</b>
        <span> {{ f.value }}</span>
      </div>
    </div>

    <div class="pf-body">
      <slot />
    </div>

    <div v-if="$slots.statement" class="pf-statement">
      <slot name="statement" />
    </div>

    <div v-if="signatures.length" class="pf-signatures" :style="colsStyle">
      <div v-for="(s, i) in signatures" :key="i" class="pf-sign">
        <div class="pf-sign-title">{{ s.title }}</div>
        <div v-if="s.hint" class="pf-sign-hint">{{ s.hint }}</div>
        <div class="pf-sign-space"></div>
        <div v-if="s.name || s.date" class="pf-sign-name">{{ s.name }} <span v-if="s.date" class="inv-ltr">{{ s.date }}</span></div>
      </div>
    </div>

    <div class="pf-footer">
      <div class="pf-dist">{{ distribution }}</div>
      <div class="pf-code">
        <template v-if="hasCode">
          کد: <span class="inv-ltr">{{ formCode }}</span>
        </template>
        <template v-else>{{ codeText }}</template>
      </div>
    </div>
  </div>
</template>
