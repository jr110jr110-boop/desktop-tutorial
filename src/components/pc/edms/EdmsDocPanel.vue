<script setup lang="ts">
// The life of one engineering document (E4, E5, E9, E12): its revisions with the lifecycle buttons, files, check-out, links to parts / tags / WBS / activities, and the audit trail.
import { computed, onMounted, ref } from "vue";
import { addRevisionFile, cancelCheckOut, checkIn, checkOut, compareRevisions, createRevision, getDocHistory, getTags, removeRevisionFile, revisionAction, saveDocLink, voidDocLink } from "@/api/EdmsAPIs";
import { downloadFile, uploadFile } from "@/api/InventoryAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { pcContractId } from "@/composables/projectControl";

const props = defineProps<{ deliverableId: number; contractId?: number | null }>();
const emit = defineEmits(["close", "changed"]);

const h = ref<any>(null);
const busy = ref(false);
const note = ref<any>(null); // { rev, action, text } for the actions that need a reason
const cmp = ref<any>(null);
const cmpPick = ref<number[]>([]);
const newRev = ref<any>(null);
const link = ref<any>(null);
const tags = ref<any[]>([]);
const fileRole = ref(2);

const statusText: Record<number, string> = { 1: "پیش‌نویس", 2: "در حال بررسی", 3: "تاییدشده", 4: "منتشرشده", 5: "جایگزین‌شده", 6: "منسوخ", 7: "As-Built" };
const statusClass = (s: number) => (s === 4 ? "ok" : s === 5 || s === 6 ? "info" : "warn");
const actionText: Record<string, string> = { submit: "ارسال برای بررسی", review: "بررسی", approve: "تایید", reject: "رد", release: "انتشار", "as-built": "As-Built", obsolete: "منسوخ", "link-quality": "پیوند NCR/QC", "link-change": "پیوند تغییر" };
const needsNote = ["reject", "obsolete"];
const linkKinds: Record<number, string> = { 1: "کالا", 2: "ردیف BOM", 3: "شماره قطعه", 4: "گره WBS", 5: "فعالیت", 6: "تگ تجهیز" };
const eventText: Record<number, string> = { 1: "بازنگری ساخته شد", 2: "فایل", 3: "ارسال برای بررسی", 4: "بررسی", 5: "تایید", 6: "رد", 7: "انتشار", 8: "جایگزین شد", 9: "منسوخ", 10: "As-Built", 11: "Check-out", 12: "Check-in", 13: "لغو Check-out", 14: "مشاهده", 15: "چاپ کنترل‌شده", 16: "پیوند", 17: "حذف پیوند", 18: "تایید دریافت", 19: "دریافت از کارفرما" };

async function load() {
  const [d, e] = await getDocHistory(props.deliverableId);
  if (e) return fail(e);
  h.value = d;
}
async function run(call: Promise<any>, msg = "انجام شد.") {
  busy.value = true;
  const [, e] = await call;
  busy.value = false;
  if (e) return fail(e);
  ok(msg);
  note.value = null;
  newRev.value = null;
  await load();
  emit("changed");
}
function act(rev: any, action: string) {
  if (needsNote.includes(action) || action === "review" || action === "approve") note.value = { rev, action, text: "" };
  else if (action === "link-quality") note.value = { rev, action, text: "", qualityKind: 1, qualityTargetId: null };
  else if (action === "link-change") note.value = { rev, action, text: "", changeRequestId: null };
  else run(revisionAction({ id: rev.id, action }));
}
function confirmNote() {
  const n = note.value;
  const body: any = { id: n.rev.id, action: n.action, note: n.text };
  if (n.action === "link-quality") { body.qualityKind = n.qualityKind; body.qualityTargetId = n.qualityTargetId; }
  if (n.action === "link-change") body.changeRequestId = n.changeRequestId;
  run(revisionAction(body));
}
const noteRequired = computed(() => note.value && needsNote.includes(note.value.action));
async function upload(rev: any, ev: Event) {
  const file = (ev.target as HTMLInputElement).files?.[0];
  if (!file) return;
  busy.value = true;
  const [id, e] = await uploadFile(file, file.name, "EDMS");
  if (e) { busy.value = false; return fail(e); }
  await run(addRevisionFile({ revisionId: rev.id, documentId: id, role: fileRole.value, fileName: file.name }), "فایل افزوده شد.");
  (ev.target as HTMLInputElement).value = "";
}
function removeFile(f: any) {
  const reason = window.prompt("علت حذف فایل (حداقل ۳ حرف):");
  if (reason) run(removeRevisionFile({ fileId: f.id, reason }), "فایل پنهان شد.");
}
const doCheckOut = () => run(checkOut({ deliverableId: props.deliverableId, note: window.prompt("توضیح (اختیاری):") || null }));
const doCheckIn = () => run(checkIn({ deliverableId: props.deliverableId }));
function doCancel() {
  const reason = window.prompt("علت لغو Check-out دیگری (حداقل ۳ حرف):");
  if (reason) run(cancelCheckOut({ deliverableId: props.deliverableId, reason }));
}
async function doCompare() {
  const [a, b] = cmpPick.value;
  const [d, e] = await compareRevisions(a, b);
  if (e) return fail(e);
  cmp.value = d;
}
function togglePick(id: number) {
  const i = cmpPick.value.indexOf(id);
  if (i >= 0) cmpPick.value.splice(i, 1);
  else cmpPick.value = [...cmpPick.value, id].slice(-2);
}
function startLink() { link.value = { kind: 3, partNumber: "", stageCode2: "", itemId: null, bomLineId: null, wbsNodeId: null, activityId: null, tagId: null, note: "" }; }
const saveLink = () => run(saveDocLink({ ...link.value, deliverableId: props.deliverableId }), "پیوند ثبت شد.").then(() => (link.value = null));
function unlink(l: any) {
  const reason = window.prompt("علت حذف پیوند (حداقل ۳ حرف):");
  if (reason) run(voidDocLink({ id: l.id, reason }), "پیوند برداشته شد.");
}
const linkText = (l: any) => (l.partNumber ? l.partNumber + (l.stageCode2 ? "-" + l.stageCode2 : "") : l.itemCode || l.tagCode || (l.activityId ? "فعالیت " + l.activityId : l.wbsNodeId ? "گره " + l.wbsNodeId : ""));
onMounted(async () => {
  await load();
  const c = props.contractId || pcContractId.value;
  if (c) { const [t] = await getTags(c); tags.value = t || []; }
});
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 1100px; width: 96vw; max-height: 92vh; overflow: auto">
      <template v-if="h">
        <div class="d-flex justify-content-between align-items-start">
          <div>
            <h3 class="h6 mb-0"><span class="inv-ltr">{{ h.documentNumber }}</span> — {{ h.title }}</h3>
            <span class="inv-muted small">رشته: {{ h.discipline || "—" }} · طرح کد بازنگری: {{ h.revisionScheme }}</span>
          </div>
          <button class="btn btn-sm btn-outline-secondary" @click="emit('close')">بستن</button>
        </div>
        <p v-if="h.checkedOutByUserId" class="alert alert-warning py-1 my-2 small">برداشته‌شده برای ویرایش توسط {{ h.checkedOutByName }} از {{ jalali(h.checkedOutAt, true) }} <span v-if="h.checkOutNote">— {{ h.checkOutNote }}</span></p>
        <div class="d-flex flex-wrap gap-2 my-2">
          <button v-if="h.deliverableActions.includes('new-revision')" :disabled="busy" class="btn btn-sm btn-primary" @click="newRev = { reason: '', changeImpact: false }">بازنگری جدید</button>
          <button v-if="h.deliverableActions.includes('check-out')" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="doCheckOut">Check-out</button>
          <button v-if="h.deliverableActions.includes('check-in')" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="doCheckIn">Check-in</button>
          <button v-if="h.deliverableActions.includes('cancel-check-out')" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="doCancel">لغو Check-out دیگری</button>
          <button v-if="cmpPick.length === 2" class="btn btn-sm btn-outline-info" @click="doCompare">مقایسه‌ی دو بازنگری</button>
        </div>

        <div class="table-responsive">
          <table class="table table-sm align-middle">
            <thead><tr><th></th><th>کد</th><th>وضعیت</th><th>علت</th><th>تهیه</th><th>بررسی</th><th>تایید</th><th>انتشار</th><th>فایل‌ها</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!h.revisions.length"><td colspan="10" class="text-center inv-muted">هنوز بازنگری ثبت نشده است.</td></tr>
              <tr v-for="r in h.revisions" :key="r.id">
                <td><input type="checkbox" :checked="cmpPick.includes(r.id)" title="انتخاب برای مقایسه" @change="togglePick(r.id)" /></td>
                <td class="inv-ltr"><b>{{ r.code }}</b></td>
                <td><span class="inv-badge" :class="statusClass(r.status)">{{ statusText[r.status] }}</span><span v-if="r.receivedFromClient" class="inv-badge info ms-1">کارفرما</span></td>
                <td class="small">{{ r.reason }}<div v-if="r.rejectReason" class="text-danger">رد: {{ r.rejectReason }}</div><div v-if="r.obsoleteReason">منسوخ: {{ r.obsoleteReason }}</div><div v-if="r.changeRequestNumber" class="inv-ltr">CR {{ r.changeRequestNumber }}</div></td>
                <td class="small">{{ r.preparedByName }}<br />{{ jalali(r.createdAt) }}</td>
                <td class="small">{{ r.reviewerName }}<br />{{ jalali(r.reviewedAt) }}</td>
                <td class="small">{{ r.approverName }}<br />{{ jalali(r.approvedAt) }}</td>
                <td class="small">{{ r.releasedByName }}<br />{{ jalali(r.releasedAt) }}</td>
                <td class="small">
                  <div v-for="f in r.files" :key="f.id"><a href="#" @click.prevent="downloadFile(f.documentId, f.fileName)">{{ f.fileName }}</a><button v-if="r.actions.includes('remove-file')" class="btn btn-link btn-sm text-danger p-0 ms-1" @click="removeFile(f)">حذف</button></div>
                  <div v-if="r.actions.includes('add-file')" class="d-flex gap-1 mt-1">
                    <select v-model.number="fileRole" class="form-select form-select-sm" style="width: 90px"><option :value="2">PDF</option><option :value="1">اصل</option><option :value="3">پیوست</option></select>
                    <input type="file" class="form-control form-control-sm" style="width: 170px" @change="upload(r, $event)" />
                  </div>
                </td>
                <td class="text-nowrap">
                  <template v-for="a in r.actions" :key="a"><button v-if="actionText[a] && a !== 'add-file' && a !== 'remove-file'" class="btn btn-sm me-1 mb-1" :class="a === 'reject' || a === 'obsolete' ? 'btn-outline-danger' : 'btn-outline-primary'" :disabled="busy" @click="act(r, a)">{{ actionText[a] }}</button></template>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <h4 class="h6 mt-3">پیوند به قطعه / تگ / WBS / فعالیت <button class="btn btn-sm btn-outline-primary ms-2" @click="startLink">پیوند جدید</button></h4>
        <ul class="small">
          <li v-if="!h.links.length" class="inv-muted">پیوندی نیست؛ سند روی کف کارگاه فقط با پیوند به قطعه دیده می‌شود.</li>
          <li v-for="l in h.links" :key="l.id">{{ linkKinds[l.kind] }}: <b class="inv-ltr">{{ linkText(l) }}</b> <span v-if="l.note" class="inv-muted">{{ l.note }}</span> <button class="btn btn-link btn-sm text-danger p-0" @click="unlink(l)">برداشتن</button></li>
        </ul>

        <h4 class="h6 mt-3">سابقه</h4>
        <div class="small" style="max-height: 180px; overflow: auto">
          <div v-for="(e, i) in h.events" :key="i">{{ jalali(e.at, true) }} — {{ e.userName }} — {{ eventText[e.kind] || e.kind }} <span v-if="e.note" class="inv-muted">({{ e.note }})</span></div>
        </div>
      </template>
      <p v-else class="inv-muted">در حال بارگذاری…</p>

      <div v-if="note" class="inv-modal-backdrop" @click.self="note = null">
        <div class="inv-modal">
          <h3 class="h6">{{ actionText[note.action] }} — بازنگری <span class="inv-ltr">{{ note.rev.code }}</span></h3>
          <template v-if="note.action === 'link-quality'">
            <select v-model.number="note.qualityKind" class="form-select form-select-sm mb-2"><option :value="1">NCR</option><option :value="2">رکورد QC</option></select>
            <input v-model.number="note.qualityTargetId" type="number" class="form-control form-control-sm mb-2" placeholder="شناسه‌ی NCR یا رکورد QC" />
          </template>
          <input v-if="note.action === 'link-change'" v-model.number="note.changeRequestId" type="number" class="form-control form-control-sm mb-2" placeholder="شناسه‌ی درخواست تغییر" />
          <textarea v-model="note.text" class="form-control form-control-sm" rows="3" :placeholder="noteRequired ? 'علت (الزامی، حداقل ۳ حرف)' : 'توضیح (اختیاری)'" />
          <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-secondary" @click="note = null">انصراف</button><button :disabled="busy || (noteRequired && note.text.trim().length < 3)" class="btn btn-sm btn-primary" @click="confirmNote">ثبت</button></div>
        </div>
      </div>
      <div v-if="newRev" class="inv-modal-backdrop" @click.self="newRev = null">
        <div class="inv-modal">
          <h3 class="h6">بازنگری جدید</h3>
          <textarea v-model="newRev.reason" class="form-control form-control-sm mb-2" rows="2" placeholder="علت بازنگری" />
          <label class="form-check small"><input v-model="newRev.changeImpact" type="checkbox" class="form-check-input" /> این بازنگری زمان، هزینه یا مواد را تغییر می‌دهد (نیاز به درخواست تغییر)</label>
          <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-secondary" @click="newRev = null">انصراف</button><button :disabled="busy || !newRev.reason.trim()" class="btn btn-sm btn-primary" @click="run(createRevision({ deliverableId, reason: newRev.reason, changeImpact: newRev.changeImpact }))">ساخت</button></div>
        </div>
      </div>
      <div v-if="link" class="inv-modal-backdrop" @click.self="link = null">
        <div class="inv-modal">
          <h3 class="h6">پیوند جدید</h3>
          <select v-model.number="link.kind" class="form-select form-select-sm mb-2"><option v-for="(n, k) in linkKinds" :key="k" :value="Number(k)">{{ n }}</option></select>
          <template v-if="link.kind === 3 || link.kind === 2">
            <input v-model="link.partNumber" class="form-control form-control-sm inv-ltr mb-2" placeholder="شماره قطعه (BOM)" />
            <input v-model="link.stageCode2" class="form-control form-control-sm inv-ltr mb-2" maxlength="2" placeholder="کد مرحله‌ی OPC (اختیاری، دو حرف)" />
          </template>
          <input v-if="link.kind === 1" v-model.number="link.itemId" type="number" class="form-control form-control-sm mb-2" placeholder="شناسه‌ی کالا" />
          <input v-if="link.kind === 4" v-model.number="link.wbsNodeId" type="number" class="form-control form-control-sm mb-2" placeholder="شناسه‌ی گره WBS" />
          <input v-if="link.kind === 5" v-model.number="link.activityId" type="number" class="form-control form-control-sm mb-2" placeholder="شناسه‌ی فعالیت" />
          <select v-if="link.kind === 6" v-model.number="link.tagId" class="form-select form-select-sm mb-2"><option :value="null">تگ را انتخاب کنید</option><option v-for="t in tags" :key="t.id" :value="t.id">{{ t.code }} {{ t.title }}</option></select>
          <input v-model="link.note" class="form-control form-control-sm" placeholder="توضیح" />
          <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-secondary" @click="link = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveLink">ثبت</button></div>
        </div>
      </div>
      <div v-if="cmp" class="inv-modal-backdrop" @click.self="cmp = null">
        <div class="inv-modal" style="max-width: 760px; width: 94vw">
          <h3 class="h6">مقایسه‌ی بازنگری <span class="inv-ltr">{{ cmp.a.code }}</span> و <span class="inv-ltr">{{ cmp.b.code }}</span></h3>
          <table class="table table-sm"><thead><tr><th>مورد</th><th>{{ cmp.a.code }}</th><th>{{ cmp.b.code }}</th></tr></thead>
            <tbody><tr v-for="d in cmp.differences" :key="d.field"><td>{{ d.field }}</td><td>{{ d.a }}</td><td>{{ d.b }}</td></tr><tr v-if="!cmp.differences.length"><td colspan="3" class="text-center inv-muted">تفاوتی در مشخصات نیست.</td></tr></tbody></table>
          <div class="row small"><div class="col-6"><b>فایل‌های {{ cmp.a.code }}</b><div v-for="f in cmp.a.files" :key="f.id"><a href="#" @click.prevent="downloadFile(f.documentId, f.fileName)">{{ f.fileName }}</a></div></div><div class="col-6"><b>فایل‌های {{ cmp.b.code }}</b><div v-for="f in cmp.b.files" :key="f.id"><a href="#" @click.prevent="downloadFile(f.documentId, f.fileName)">{{ f.fileName }}</a></div></div></div>
          <div class="text-end mt-3"><button class="btn btn-sm btn-secondary" @click="cmp = null">بستن</button></div>
        </div>
      </div>
    </div>
  </div>
</template>
