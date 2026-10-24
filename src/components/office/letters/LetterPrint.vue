<script setup lang="ts">
// A4 RTL print of a letter (B Zar): letterhead area, number / date / attachment line at the top left, subject, body, signature block, recipients.
// Shown only while printing (print-only); the page puts it directly inside .inv-page and calls printSheet().
import { computed } from "vue";
import Logo from "@/assets/img/logo.png";
import { classifications, letterKinds, recipientTypes } from "@/composables/office";
import { fa, sanitizeHtml } from "./letterKit";

const props = defineProps<{ letter: any; signatureUrl?: string | null }>();
const body = computed(() => sanitizeHtml(props.letter.bodyHtml));
const to = computed(() => (props.letter.recipients || []).filter((r: any) => r.type !== 2));
const cc = computed(() => (props.letter.recipients || []).filter((r: any) => r.type === 2));
const nameOf = (r: any) => r.positionTitle || r.partyName || "";
const attachCount = computed(() => (props.letter.attachments || []).length);
</script>

<template>
  <div class="print-only print-form ks-report q-form letter-sheet">
    <div class="ls-head">
      <img :src="Logo" alt="" class="ls-logo" />
      <div class="ls-letterhead"></div>
      <div class="ls-meta">
        <div><b>شماره:</b> <span class="ls-num">{{ letter.number ? fa(letter.number) : "ثبت‌نشده" }}</span></div>
        <div><b>تاریخ:</b> {{ fa(letter.dateText) }}</div>
        <div><b>پیوست:</b> {{ attachCount ? fa(attachCount) + " فقره" : "ندارد" }}</div>
        <div v-if="letter.classification > 0" class="ls-class">{{ classifications[letter.classification] }}</div>
      </div>
    </div>

    <div v-if="to.length" class="ls-to">
      <div v-for="(r, i) in to" :key="i">{{ nameOf(r) }}</div>
    </div>
    <div class="ls-kind">{{ letterKinds[letter.kind] }}</div>
    <div class="ls-subject"><b>موضوع:</b> {{ letter.subject }}</div>

    <div class="ls-body" dir="rtl" v-html="body"></div>

    <div v-if="letter.signedAt" class="ls-sign">
      <div class="ls-sign-title">{{ letter.signerPositionTitle }}</div>
      <img v-if="signatureUrl" :src="signatureUrl" alt="" class="ls-sign-img" />
      <div>{{ letter.signedByName }}</div>
      <div class="small">{{ fa(letter.signedAtText) }}</div>
    </div>

    <div v-if="cc.length" class="ls-cc">
      <b>{{ recipientTypes[2] }}:</b> <span v-for="(r, i) in cc" :key="i">{{ nameOf(r) }}<template v-if="i < cc.length - 1">، </template></span>
    </div>
    <div v-if="letter.contentHash" class="ls-foot">شناسه اصالت: <span class="inv-ltr">{{ letter.contentHash.slice(0, 16) }}</span></div>
  </div>
</template>

<style lang="scss">
.letter-sheet.print-form {
  border: none;
  font-size: 15px;
  line-height: 2;
  padding: 0;
  .ls-head { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; border-bottom: 1.5px solid #1f3a5f; padding-bottom: 0.5rem; min-height: 28mm; }
  .ls-logo { height: 20mm; }
  .ls-letterhead { flex: 1; min-height: 20mm; }
  .ls-meta { text-align: left; direction: rtl; font-size: 14px; line-height: 1.9; min-width: 48mm; }
  .ls-num { direction: ltr; unicode-bidi: isolate; }
  .ls-class { font-weight: bold; border: 1px solid #000; display: inline-block; padding: 0 0.6rem; }
  .ls-to { margin-top: 0.8rem; font-weight: bold; }
  .ls-kind { display: none; }
  .ls-subject { margin: 0.6rem 0; }
  .ls-body { min-height: 90mm; text-align: justify; overflow-wrap: anywhere; p { margin: 0 0 0.6rem; } }
  .ls-sign { margin-top: 1.5rem; margin-right: auto; margin-left: 0; width: 60mm; text-align: center; break-inside: avoid; }
  .ls-sign-title { font-weight: bold; }
  .ls-sign-img { max-height: 25mm; max-width: 55mm; display: block; margin: 0.2rem auto; }
  .ls-cc { margin-top: 1rem; font-size: 14px; }
  .ls-foot { margin-top: 1rem; font-size: 11px; color: #555; border-top: 1px solid #ccc; padding-top: 0.3rem; }
}
</style>
