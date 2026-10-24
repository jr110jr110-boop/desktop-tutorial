<script setup lang="ts">
// Editor of a draft letter. The parent owns the form object (mutated here) and the lists.
import { computed } from "vue";
import AttachmentsEditor from "./AttachmentsEditor.vue";
import LinksEditor from "./LinksEditor.vue";
import PickSelect from "./PickSelect.vue";
import RecipientsEditor from "./RecipientsEditor.vue";
import RichBox from "./RichBox.vue";
import FnDate from "@/components/fn/FnDate.vue";
import { classifications, letterKinds, urgencies } from "@/composables/office";
import { positionText, type MyPosition } from "./letterKit";

const props = defineProps<{
  form: any;
  letterId: number | null;
  savedAttachmentIds: string[];
  myPositions: MyPosition[];
  positions: any[];
  parties: any[];
  templates: any[];
  isSecretariat: boolean;
  replyToText: string;
  locked: boolean; // existing letter: the kind cannot change
}>();
const emit = defineEmits(["pick-template", "party-created"]);

const kindOptions = computed(() => Object.entries(letterKinds).filter(([k]) => Number(k) !== 1 || props.isSecretariat || props.form.kind === 1));
const incoming = computed(() => props.form.kind === 1);
const senderOptions = computed(() => props.myPositions.map((p) => ({ value: p.positionId, text: positionText(p) + (p.onBehalf ? " (به نیابت)" : "") })));
const signerOptions = computed(() => props.positions.filter((p) => p.canSign).map((p) => ({ value: p.id, text: positionText(p) })));
const partyOptions = computed(() => props.parties.map((p) => ({ value: p.id, text: p.name })));
const templateOptions = computed(() => props.templates.filter((t) => t.isActive !== false && (t.letterKind == null || t.letterKind === props.form.kind)));
</script>

<template>
  <div class="row g-3">
    <div class="col-md-3">
      <label class="form-label small mb-0">نوع نامه</label>
      <select v-model.number="form.kind" :disabled="locked" class="form-select form-select-sm"><option v-for="[k, t] in kindOptions" :key="k" :value="Number(k)">{{ t }}</option></select>
    </div>
    <div class="col-md-3">
      <label class="form-label small mb-0">طبقه‌بندی</label>
      <select v-model.number="form.classification" class="form-select form-select-sm"><option v-for="(t, k) in classifications" :key="k" :value="Number(k)">{{ t }}</option></select>
    </div>
    <div class="col-md-3">
      <label class="form-label small mb-0">فوریت</label>
      <select v-model.number="form.urgency" class="form-select form-select-sm"><option v-for="(t, k) in urgencies" :key="k" :value="Number(k)">{{ t }}</option></select>
    </div>
    <div v-if="!incoming" class="col-md-3">
      <label class="form-label small mb-0">قالب نامه</label>
      <select :value="form.templateId" class="form-select form-select-sm" @change="emit('pick-template', ($event.target as HTMLSelectElement).value)">
        <option value="">بدون قالب</option>
        <option v-for="t in templateOptions" :key="t.id" :value="t.id">{{ t.title }}</option>
      </select>
    </div>

    <div class="col-12">
      <label class="form-label small mb-0">موضوع *</label>
      <input v-model="form.subject" class="form-control form-control-sm" maxlength="300" />
      <div v-if="replyToText" class="small inv-muted mt-1">در پاسخ به نامه: {{ replyToText }}</div>
    </div>

    <template v-if="incoming">
      <div class="col-md-4">
        <label class="form-label small mb-0">فرستنده (طرف خارجی) *</label>
        <PickSelect v-model="form.senderPartyId" :options="partyOptions" placeholder="انتخاب فرستنده" />
      </div>
      <div class="col-md-4"><label class="form-label small mb-0">شماره نامه فرستنده</label><input v-model="form.incomingNumber" class="form-control form-control-sm inv-ltr" /></div>
      <div class="col-md-4"><label class="form-label small mb-0">تاریخ نامه فرستنده</label><FnDate v-model="form.incomingDate" /></div>
    </template>
    <template v-else>
      <div class="col-md-6">
        <label class="form-label small mb-0">سمت فرستنده *</label>
        <PickSelect v-model="form.senderPositionId" :options="senderOptions" placeholder="انتخاب سمت" />
      </div>
      <div class="col-md-6">
        <label class="form-label small mb-0">سمت امضاکننده</label>
        <PickSelect v-model="form.signerPositionId" :options="signerOptions" placeholder="انتخاب امضاکننده (دارای حق امضا)" />
      </div>
    </template>

    <div class="col-12">
      <label class="form-label small mb-1">گیرندگان</label>
      <RecipientsEditor v-model="form.recipients" :parties="parties" :positions="positions" @party-created="emit('party-created', $event)" />
    </div>

    <div class="col-12">
      <label class="form-label small mb-1">متن نامه</label>
      <RichBox v-model="form.bodyHtml" />
    </div>

    <div class="col-md-6">
      <label class="form-label small mb-1">پیوست‌ها</label>
      <AttachmentsEditor v-model="form.attachments" :letter-id="letterId" :saved-ids="savedAttachmentIds" />
    </div>
    <div class="col-md-6">
      <label class="form-label small mb-1">پیوند به رکوردها</label>
      <LinksEditor v-model="form.links" />
    </div>

    <div class="col-12 d-flex gap-4 flex-wrap">
      <label class="small d-flex align-items-center gap-1"><input v-model="form.paperCopy" type="checkbox" /> نسخه کاغذی نیز وجود دارد</label>
      <label v-if="incoming && isSecretariat" class="small d-flex align-items-center gap-1"><input v-model="form.register" type="checkbox" /> همین حالا ثبت و شماره‌گذاری شود</label>
    </div>
  </div>
</template>
