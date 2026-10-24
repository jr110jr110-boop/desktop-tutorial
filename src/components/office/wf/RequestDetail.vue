<script setup lang="ts">
// Request detail: values, approvals timeline and the actions of allowedActions (submit, approve, reject, cancel, complete; save = edit).
import { ref, watch } from "vue";
import { getRequest, requestAction } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";
import { actionTitles, approvalStatuses, badgeOfRequest, can, requestStatuses } from "@/composables/office";
import RequestForm from "./RequestForm.vue";

const props = defineProps({ id: { type: Number, required: true } });
const emit = defineEmits(["changed"]);
const r = ref<any>(null);
const comment = ref("");
const busy = ref(false);
const editing = ref(false);
const buttons = ["submit", "approve", "reject", "cancel", "complete"];

async function load() {
  const [d, error] = await getRequest(props.id);
  if (error) return fail(error);
  r.value = d;
}
watch(() => props.id, load, { immediate: true });

async function act(action: string) {
  if ((action === "reject" || action === "cancel") && !comment.value.trim()) return fail({ message: "علت را در کادر توضیح بنویسید." });
  if (action === "cancel" && !confirm("درخواست ابطال شود؟")) return;
  busy.value = true;
  const [d, error] = await requestAction(r.value.id, action as any, comment.value, r.value.rowVersion);
  busy.value = false;
  if (error) return fail(error);
  ok("انجام شد.");
  comment.value = "";
  r.value = d;
  emit("changed");
}
// date values arrive from the API already as Persian text; show them as they are
const show = (_f: any, v: string | undefined) => (v === undefined || v === null || v === "" ? "-" : v);
const stepBadge = (s: number) => (s === 1 ? "ok" : s === 2 ? "bad" : "warn");
</script>

<template>
  <div v-if="r">
    <div class="inv-toolbar">
      <h2 class="inv-title">
        درخواست {{ r.number }} - {{ r.typeTitle }}
        <span :class="badgeOfRequest(r.status)" class="inv-badge ms-2">{{ requestStatuses[r.status] }}</span>
      </h2>
      <button v-if="can(r, 'save')" class="btn btn-sm btn-outline-primary" @click="editing = true">ویرایش</button>
    </div>
    <p class="small mb-1">درخواست‌کننده: {{ r.requesterName }} <span v-if="r.submittedAtText">- ارسال: <span class="inv-ltr">{{ r.submittedAtText }}</span></span><span v-if="r.completedAtText"> - انجام: <span class="inv-ltr">{{ r.completedAtText }}</span></span></p>
    <p v-if="r.targetNote" class="small inv-muted">{{ r.targetNote }}</p>
    <p v-if="r.note" class="small">یادداشت: {{ r.note }}</p>

    <table class="table table-sm" style="max-width: 40rem">
      <tbody>
        <tr v-for="f in r.fields" :key="f.name"><th style="width: 12rem">{{ f.label }}</th><td>{{ show(f, r.values?.[f.name]) }}</td></tr>
      </tbody>
    </table>

    <h3 class="h6 mt-3">روند تایید</h3>
    <ol class="list-unstyled">
      <li v-if="!r.approvals?.length" class="inv-muted small">هنوز برای تایید ارسال نشده است.</li>
      <li v-for="a in r.approvals" :key="a.step" class="border-start border-3 ps-2 mb-2" :class="a.status === 1 ? 'border-success' : a.status === 2 ? 'border-danger' : 'border-warning'">
        <b>{{ a.step }}. {{ a.positionTitle }}</b> <span :class="stepBadge(a.status)" class="inv-badge">{{ approvalStatuses[a.status] }}</span>
        <div v-if="a.actedAtText" class="small inv-muted">{{ a.actedByName }}{{ a.onBehalf ? " (به نیابت)" : "" }} - <span class="inv-ltr">{{ a.actedAtText }}</span></div>
        <div v-if="a.comment" class="small">{{ a.comment }}</div>
      </li>
    </ol>

    <div v-if="buttons.some((b) => can(r, b))" class="border rounded p-2 bg-light">
      <textarea v-model="comment" class="form-control form-control-sm mb-2" rows="2" placeholder="توضیح (برای رد یا ابطال الزامی است)"></textarea>
      <div class="d-flex flex-wrap gap-2">
        <template v-for="b in buttons" :key="b">
          <button v-if="can(r, b)" :disabled="busy" :class="b === 'reject' || b === 'cancel' ? 'btn-outline-danger' : 'btn-success'" class="btn btn-sm" @click="act(b)">{{ actionTitles[b] }}</button>
        </template>
      </div>
    </div>
    <RequestForm v-if="editing" :request="r" @close="editing = false" @saved="editing = false; load(); emit('changed')" />
  </div>
</template>
