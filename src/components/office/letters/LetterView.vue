<script setup lang="ts">
// Read-only view of a letter: register data, body (sanitised HTML in a container), recipients, attachments, links.
// metadataOnly: only the register data and a note; no body, attachments or notes.
import { computed } from "vue";
import { downloadLetterFile } from "@/api/OfficeAPIs";
import { classifications, deliveryMethods, letterKinds, letterStatuses, recipientTypes, relatedKinds, urgencies, badgeOfLetter } from "@/composables/office";
import { fail } from "@/composables/inventory";
import { sanitizeHtml } from "./letterKit";

const props = defineProps<{ letter: any }>();
const emit = defineEmits(["open-letter"]);
const body = computed(() => sanitizeHtml(props.letter.bodyHtml));
const meta = computed(() => !!props.letter.metadataOnly);

async function download(a: any) {
  const error = await downloadLetterFile(props.letter.id, a.documentId, a.fileName);
  if (error) fail(error);
}
async function downloadReceipt() {
  const error = await downloadLetterFile(props.letter.id, props.letter.deliveryReceiptDocumentId, "رسید نامه");
  if (error) fail(error);
}
</script>

<template>
  <div>
    <div v-if="meta" class="alert alert-warning py-2">
      محتوای این نامه (موضوع، متن، پیوست‌ها و یادداشت‌های ارجاع) طبقه‌بندی «{{ classifications[letter.classification] }}» دارد و از دبیرخانه پنهان است. فقط اطلاعات ثبت نمایش داده می‌شود.
    </div>
    <div class="row g-2 mb-2">
      <div class="col-md-3"><b>شماره:</b> <span class="inv-ltr">{{ letter.number || "بدون شماره" }}</span></div>
      <div class="col-md-3"><b>تاریخ:</b> {{ letter.dateText }}</div>
      <div class="col-md-3"><b>نوع:</b> {{ letterKinds[letter.kind] }}<span v-if="letter.imported" class="inv-badge me-1">آرشیو قدیم</span></div>
      <div class="col-md-3"><b>وضعیت:</b> <span :class="badgeOfLetter(letter.status)" class="inv-badge">{{ letterStatuses[letter.status] }}</span></div>
      <div class="col-md-3"><b>طبقه‌بندی:</b> {{ classifications[letter.classification] }}</div>
      <div class="col-md-3"><b>فوریت:</b> {{ urgencies[letter.urgency] }}</div>
      <div class="col-md-3"><b>فرستنده:</b> {{ letter.senderName }}</div>
      <div class="col-md-3"><b>گیرندگان:</b> {{ letter.recipientNames }}</div>
      <div v-if="letter.incomingNumber || letter.incomingDateText" class="col-md-6"><b>شماره و تاریخ نامه فرستنده:</b> <span class="inv-ltr">{{ letter.incomingNumber }}</span> {{ letter.incomingDateText }}</div>
      <div v-if="letter.deliveryMethod" class="col-md-6"><b>نحوه تحویل:</b> {{ deliveryMethods[letter.deliveryMethod] }} - {{ letter.sentDateText }}</div>
      <div v-if="letter.deliveryReceipt || (!meta && letter.deliveryReceiptDocumentId)" class="col-md-6">
        <b>رسید:</b> {{ letter.deliveryReceipt }}
        <button v-if="!meta && letter.deliveryReceiptDocumentId" class="btn btn-sm btn-link p-0 me-2" @click="downloadReceipt">دریافت فایل رسید</button>
      </div>
      <div v-if="letter.paperCopy" class="col-md-3"><span class="inv-badge info">نسخه کاغذی دارد</span></div>
      <div v-if="letter.cancelReason" class="col-12 text-danger"><b>دلیل ابطال:</b> {{ letter.cancelReason }}</div>
    </div>

    <template v-if="!meta">
      <h2 class="h6 mt-3">{{ letter.subject }}</h2>
      <div class="letter-body border rounded p-3 mb-3" dir="rtl" v-html="body"></div>

      <div v-if="letter.recipients && letter.recipients.length" class="mb-3">
        <b>گیرندگان:</b>
        <ul class="mb-0"><li v-for="(r, i) in letter.recipients" :key="i">{{ recipientTypes[r.type] }}: {{ r.positionTitle || r.partyName }}</li></ul>
      </div>
      <div v-if="letter.attachments && letter.attachments.length" class="mb-3">
        <b>پیوست‌ها:</b>
        <ul class="list-unstyled mb-0"><li v-for="a in letter.attachments" :key="a.documentId">{{ a.fileName || "فایل" }} <button class="btn btn-sm btn-link" @click="download(a)">دریافت</button></li></ul>
      </div>
      <div v-if="letter.links && letter.links.length" class="mb-3">
        <b>پیوندها:</b>
        <ul class="mb-0"><li v-for="(l, i) in letter.links" :key="i">{{ relatedKinds[l.kind] || "سایر" }}: {{ l.text }}</li></ul>
      </div>
      <div v-if="letter.replyToLetterId" class="mb-2"><a href="#" @click.prevent="emit('open-letter', letter.replyToLetterId)">نامه‌ای که این نامه پاسخ آن است</a></div>
      <div v-if="letter.followUpLetterId" class="mb-2"><a href="#" @click.prevent="emit('open-letter', letter.followUpLetterId)">نامه پیرو</a></div>
    </template>
  </div>
</template>

<style scoped>
.letter-body { line-height: 2; background: #fff; overflow-wrap: anywhere; }
</style>
