<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import AiNotesBox from "@/components/ai/AiNotesBox.vue";
import LetterActionDialog from "@/components/office/letters/LetterActionDialog.vue";
import LetterApprovals from "@/components/office/letters/LetterApprovals.vue";
import LetterForm from "@/components/office/letters/LetterForm.vue";
import LetterPrint from "@/components/office/letters/LetterPrint.vue";
import LetterSignature from "@/components/office/letters/LetterSignature.vue";
import LetterView from "@/components/office/letters/LetterView.vue";
import ReferralPanel from "@/components/office/letters/ReferralPanel.vue";
import { loadMyPositions, useDirectory, type MyPosition } from "@/components/office/letters/letterKit";
import { getLetter, getLetterTemplates, letterAction, openLetterFile, saveLetter } from "@/api/OfficeAPIs";
import { actionTitles, badgeOfLetter, can, letterKinds, letterStatuses } from "@/composables/office";
import { printSheet, camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { hasRole, OfficeRoles } from "@/composables/roles";

// Letter: editor of a draft and the full life of a letter. Every button comes from allowedActions of the record; the server checks again.
const route = useRoute();
const router = useRouter();
const isNew = computed(() => route.params.id === "new");
const letter = ref<any>(null);
const form = ref<any>(blank(2));
const myPositions = ref<MyPosition[]>([]);
const templates = ref<any[]>([]);
const replyToText = ref("");
const dialog = ref("");
const busy = ref(false);
const loaded = ref(false);
const signatureUrl = ref<string | null>(null);
const { positions, parties, loadPositions, loadParties } = useDirectory();
const isSecretariat = hasRole(OfficeRoles.secretariat);

const meta = computed(() => !!letter.value?.metadataOnly);
const editable = computed(() => isNew.value || (!!letter.value && !meta.value && can(letter.value, "save")));
const savedIds = computed<string[]>(() => (letter.value?.attachments || []).map((a: any) => a.documentId));
const actionOrder = ["submit", "approve", "return", "sign", "register", "send", "receipt", "archive", "cancel"];
const actions = computed(() => actionOrder.filter((a) => can(letter.value, a)));
const templateChain = computed<number[]>(() => templates.value.find((t) => t.id === (form.value.templateId ?? letter.value?.templateId))?.approvalChain || []);
const title = computed(() => (isNew.value ? "نامه جدید" : letter.value ? `${letterKinds[letter.value.kind]} ${letter.value.number || "(بدون شماره)"}` : "نامه"));

function blank(kind: number) {
  return { id: null, kind, subject: "", bodyHtml: "", senderPositionId: null, senderPartyId: null, classification: 0, urgency: 0, incomingNumber: "", incomingDate: null, signerPositionId: null, paperCopy: false, replyToLetterId: null, templateId: null, recipients: [], attachments: [], links: [], register: false, rowVersion: null };
}
function fill(l: any) {
  form.value = {
    id: l.id, kind: l.kind, subject: l.subject || "", bodyHtml: l.bodyHtml || "", senderPositionId: l.senderPositionId ?? null, senderPartyId: l.senderPartyId ?? null, classification: l.classification, urgency: l.urgency,
    incomingNumber: l.incomingNumber || "", incomingDate: l.incomingDate ? String(l.incomingDate).slice(0, 10) : null, signerPositionId: l.signerPositionId ?? null, paperCopy: !!l.paperCopy,
    replyToLetterId: l.replyToLetterId ?? null, templateId: l.templateId ?? null,
    recipients: (l.recipients || []).map((r: any) => ({ type: r.type, positionId: r.positionId ?? null, partyId: r.partyId ?? null, positionTitle: r.positionTitle, partyName: r.partyName })),
    attachments: (l.attachments || []).map((a: any) => ({ documentId: a.documentId, fileName: a.fileName })),
    links: (l.links || []).map((k: any) => ({ kind: k.kind, recordId: k.recordId ?? null, text: k.text })),
    register: false, rowVersion: l.rowVersion,
  };
}
function defaultSender() {
  const own = myPositions.value.filter((p) => !p.onBehalf);
  return (own[0] || myPositions.value[0])?.positionId ?? null;
}

async function loadSignature() {
  if (signatureUrl.value) URL.revokeObjectURL(signatureUrl.value);
  signatureUrl.value = null;
  const l = letter.value;
  if (!l || l.metadataOnly || !l.signatureDocumentId) return;
  const [url] = await openLetterFile(l.id, l.signatureDocumentId);
  signatureUrl.value = url;
}

async function load() {
  loaded.value = false;
  letter.value = null;
  replyToText.value = "";
  if (isNew.value) {
    const q = Number(route.query.kind) || 2;
    form.value = blank(q === 1 && !isSecretariat ? 2 : q);
    form.value.senderPositionId = defaultSender();
    const replyTo = Number(route.query.replyTo);
    if (replyTo) {
      const [src] = await getLetter(replyTo);
      if (src && !src.metadataOnly) {
        form.value.kind = src.kind === 1 ? 2 : src.kind;
        form.value.replyToLetterId = src.id;
        form.value.subject = "پاسخ: " + (src.subject || "");
        replyToText.value = `${src.number || "بدون شماره"} - ${src.subject || ""}`;
        if (src.senderPositionId) form.value.recipients = [{ type: 1, positionId: src.senderPositionId, partyId: null }];
        else if (src.senderPartyId) form.value.recipients = [{ type: 1, positionId: null, partyId: src.senderPartyId }];
      }
    }
  } else {
    const [data, error] = await getLetter(Number(route.params.id));
    if (error) {
      fail(error);
      loaded.value = true;
      return;
    }
    letter.value = data;
    fill(data);
    if (data.replyToLetterId && !data.metadataOnly) {
      const [src] = await getLetter(data.replyToLetterId);
      if (src && !src.metadataOnly) replyToText.value = `${src.number || "بدون شماره"} - ${src.subject || ""}`;
    }
    loadSignature();
  }
  loaded.value = true;
}

function pickTemplate(value: string) {
  const t = templates.value.find((x) => String(x.id) === value);
  if (!t) {
    form.value.templateId = null;
    return;
  }
  if (form.value.bodyHtml && !window.confirm("متن فعلی با متن قالب جایگزین شود؟")) return;
  form.value.templateId = t.id;
  form.value.bodyHtml = t.bodyHtml || "";
  if (t.signerPositionId && !form.value.signerPositionId) form.value.signerPositionId = t.signerPositionId;
}

/** Saves the draft; returns true when saved. Stays on the page and reloads the record. */
async function saveDraft(silent = false): Promise<boolean> {
  const f = form.value;
  if (!f.subject.trim()) {
    fail("موضوع نامه را وارد کنید.");
    return false;
  }
  const incoming = f.kind === 1;
  const dto = {
    id: f.id, kind: f.kind, subject: f.subject.trim(), bodyHtml: f.bodyHtml || null,
    senderPositionId: incoming ? null : f.senderPositionId, senderPartyId: incoming ? f.senderPartyId : null,
    classification: f.classification, urgency: f.urgency, incomingNumber: incoming ? f.incomingNumber || null : null, incomingDate: incoming ? f.incomingDate : null,
    signerPositionId: incoming ? null : f.signerPositionId, paperCopy: f.paperCopy, replyToLetterId: f.replyToLetterId, templateId: f.templateId,
    recipients: f.recipients.map((r: any) => ({ type: r.type, positionId: r.positionId, partyId: r.partyId })),
    attachments: f.attachments, links: f.links, register: incoming && isSecretariat ? f.register : false, rowVersion: f.rowVersion,
  };
  busy.value = true;
  const [data, error] = await saveLetter(dto);
  busy.value = false;
  if (error) {
    fail(error);
    return false;
  }
  if (!silent) ok("نامه ذخیره شد.");
  const id = Number(data?.id ?? (typeof data === "number" ? data : f.id));
  if (!id) return true;
  if (isNew.value || Number(route.params.id) !== id) await router.replace({ name: "OfLetter", params: { id } });
  else await load();
  return true;
}

async function openAction(action: string) {
  if (editable.value && !isNew.value && action === "submit" && !(await saveDraft(true))) return;
  dialog.value = action;
}
async function runAction(payload: any) {
  const action = dialog.value;
  busy.value = true;
  const [, error] = await letterAction({ id: letter.value.id, action, rowVersion: letter.value.rowVersion, ...payload });
  busy.value = false;
  if (error) return fail(error);
  dialog.value = "";
  ok(`«${actionTitles[action]}» انجام شد.`);
  await load();
}

function printLetter() {
  printSheet();
}
function reply() {
  router.push({ name: "OfLetter", params: { id: "new" }, query: { replyTo: letter.value.id } });
}
function openLetter(id: number) {
  router.push({ name: "OfLetter", params: { id } });
}

onMounted(async () => {
  const [mine, tpl] = await Promise.all([loadMyPositions(), getLetterTemplates({ PageNumber: 1, PageSize: 200 }), loadPositions(), loadParties()]);
  myPositions.value = mine;
  const d = camelize<any>(tpl[0]);
  templates.value = Array.isArray(d) ? d : d?.data || [];
  await load();
});
watch(() => route.params.id, (v, o) => { if (v !== o && route.name === "OfLetter") load(); });
watch(() => route.query.replyTo, () => { if (isNew.value && route.name === "OfLetter") load(); });
onBeforeUnmount(() => { if (signatureUrl.value) URL.revokeObjectURL(signatureUrl.value); });
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">
          {{ title }}
          <span v-if="letter" :class="badgeOfLetter(letter.status)" class="inv-badge">{{ letterStatuses[letter.status] }}</span>
        </h1>
        <div class="d-flex gap-2 flex-wrap">
          <button class="btn btn-sm btn-outline-secondary" @click="router.push({ name: 'OfLetters' })">بازگشت به فهرست</button>
          <button v-if="letter && !meta" class="btn btn-sm btn-outline-secondary" @click="printLetter">چاپ نامه</button>
          <button v-if="letter && !meta && letter.status >= 3 && letter.status !== 7" class="btn btn-sm btn-outline-secondary" @click="reply">پاسخ به نامه</button>
          <button v-if="editable" :disabled="busy" class="btn btn-sm btn-primary" @click="saveDraft()">ذخیره</button>
          <button v-for="a in actions" :key="a" :class="a === 'cancel' ? 'btn-outline-danger' : 'btn-success'" :disabled="busy" class="btn btn-sm" @click="openAction(a)">{{ actionTitles[a] }}</button>
        </div>
      </div>

      <p v-if="!loaded" class="inv-muted">در حال بارگذاری...</p>
      <p v-else-if="!isNew && !letter" class="inv-muted">نامه پیدا نشد یا اجازه دیدن آن را ندارید.</p>
      <template v-else>
        <LetterForm
          v-if="editable" :form="form" :is-secretariat="isSecretariat" :letter-id="letter ? letter.id : null" :locked="!isNew" :my-positions="myPositions" :parties="parties" :positions="positions"
          :reply-to-text="replyToText" :saved-attachment-ids="savedIds" :templates="templates" @party-created="loadParties" @pick-template="pickTemplate" />
        <LetterView v-else-if="letter" :letter="letter" @open-letter="openLetter" />

        <template v-if="letter && !meta">
          <hr />
          <LetterSignature :letter="letter" />
          <h2 class="h6">زنجیره تایید</h2>
          <LetterApprovals :approvals="letter.approvals" />
          <hr />
          <ReferralPanel :can-refer="can(letter, 'refer')" :is-secretariat="isSecretariat" :letter="letter" :my-positions="myPositions" :positions="positions" @changed="load" />
        </template>
      </template>
    </div>

    <!-- draft reply notes of the AI seat OFFICE-DRAFT (never for a confidential letter shown as register data only) -->
    <AiNotesBox v-if="letter && !meta && letter.id" :target-id="letter.id" target-type="OfficeLetter" />

    <LetterPrint v-if="letter && !meta" :letter="letter" :signature-url="signatureUrl" />

    <LetterActionDialog v-if="dialog && letter" :action="dialog" :letter="letter" :my-positions="myPositions" :positions="positions" :template-chain="templateChain" @cancel="dialog = ''" @confirm="runAction" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
