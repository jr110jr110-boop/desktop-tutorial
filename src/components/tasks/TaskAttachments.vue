<script setup lang="ts">
import { ref } from "vue";
import { uploadFile } from "@/api/InventoryAPIs";
import { downloadDocument } from "@/api/OfficeAPIs";
import { attachToTask, voidTaskAttachment } from "@/api/TasksAPIs";
import { fail, ok } from "@/composables/inventory";

// Attachments of a task: the file goes through the Documents upload, the task keeps only the link. A wrong file is voided with a reason, never deleted.
const props = defineProps({ task: { type: Object, required: true }, canWrite: Boolean });
const emit = defineEmits(["changed"]);
const file = ref<File | null>(null);
const busy = ref(false);
const voiding = ref<any>(null);
const reason = ref("");

async function add() {
  if (!file.value) return fail("فایل را انتخاب کنید.");
  busy.value = true;
  const [id, upError] = await uploadFile(file.value, "پیوست وظیفه " + props.task.number);
  if (upError || !id) { busy.value = false; return fail(upError); }
  const [, error] = await attachToTask({ taskId: props.task.id, documentId: id, fileName: file.value.name });
  busy.value = false;
  if (error) return fail(error);
  file.value = null;
  ok("پیوست ثبت شد.");
  emit("changed");
}
async function doVoid() {
  if (!reason.value.trim()) return fail("دلیل ابطال را بنویسید.");
  const [, error] = await voidTaskAttachment({ taskId: props.task.id, attachmentId: voiding.value.id, reason: reason.value });
  if (error) return fail(error);
  voiding.value = null;
  reason.value = "";
  emit("changed");
}
const open = async (a: any) => { const e = await downloadDocument(a.documentId, a.fileName); if (e) fail(e); };
</script>

<template>
  <div>
    <table class="table table-sm">
      <thead><tr><th>فایل</th><th>بارگذار</th><th>تاریخ</th><th></th></tr></thead>
      <tbody>
        <tr v-if="!task.attachments.length"><td colspan="4" class="text-center inv-muted">پیوستی نیست.</td></tr>
        <tr v-for="a in task.attachments" :key="a.id" :class="{ 'text-decoration-line-through text-muted': a.isVoid }">
          <td><button class="btn btn-sm btn-link p-0" @click="open(a)">{{ a.fileName }}</button><span v-if="a.isVoid" class="inv-badge bad ms-2">باطل: {{ a.voidReason }}</span></td>
          <td>{{ a.uploadedByName }}</td><td>{{ a.uploadedAtText }}</td>
          <td><button v-if="canWrite && !a.isVoid" class="btn btn-sm btn-outline-danger" @click="voiding = a">ابطال</button></td>
        </tr>
      </tbody>
    </table>
    <div v-if="canWrite" class="d-flex gap-2 align-items-center">
      <input class="form-control form-control-sm" type="file" @change="file = ($event.target as HTMLInputElement).files?.[0] || null" />
      <button :disabled="busy" class="btn btn-sm btn-primary" @click="add">افزودن</button>
    </div>
    <div v-if="voiding" class="mt-2 d-flex gap-2"><input v-model="reason" class="form-control form-control-sm" placeholder="دلیل ابطال" /><button class="btn btn-sm btn-danger" @click="doVoid">ابطال پیوست</button><button class="btn btn-sm btn-light" @click="voiding = null">انصراف</button></div>
  </div>
</template>
