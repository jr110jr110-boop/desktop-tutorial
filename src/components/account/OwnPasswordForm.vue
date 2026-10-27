<script lang="ts" setup>
// The signed-in user changes the OWN password (POST Accounts/change-password): the current password is always sent.
// Nobody changes another user's password here; the PMO sets a temporary password for others (see views/user/Update.vue).
import { ref } from "vue";
import { changeOwnPassword } from "@/api/UserAPIs";
import { failureText } from "@/composables/apiMessage";
import { passwordProblem, passwordRulesText, storeSession } from "@/composables/session";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showWarnNotification from "@/composables/showWarnNotification";

import KsInput from "@/components/common/KsInput.vue";
import KsButton from "@/components/common/KsButton.vue";

const emit = defineEmits(["changed"]);

const form = ref({ currentPassword: "", newPassword: "", repeatPassword: "" });
const loading = ref(false);

/** Persian text of the first problem of the form, or null when it can be sent (the API checks the same rules again). */
function formProblem(): string | null {
  const f = form.value;
  if (!f.currentPassword) return "رمز عبور فعلی را وارد کنید.";
  const rule = passwordProblem(f.newPassword);
  if (rule) return rule;
  if (f.newPassword !== f.repeatPassword) return "رمز جدید و تکرار آن یکسان نیستند.";
  if (f.newPassword === f.currentPassword) return "رمز جدید باید با رمز فعلی فرق داشته باشد.";
  return null;
}

async function submit() {
  if (loading.value) return;
  const problem = formProblem();
  if (problem) {
    showWarnNotification(problem);
    return;
  }
  loading.value = true;
  try {
    const [data, error] = await changeOwnPassword({
      currentPassword: form.value.currentPassword,
      newPassword: form.value.newPassword,
    });
    if (error || !data?.success) {
      showErrorNotification(failureText(data, error, "تغییر رمز انجام نشد."));
      return;
    }
    // The old tokens of the user end with the old password, so the answer is a new sign-in: keep it or the next request would be refused.
    if (data.data?.accessToken) storeSession(data.data);
    form.value = { currentPassword: "", newPassword: "", repeatPassword: "" };
    showSuccessNotification("رمز عبور با موفقیت تغییر کرد.");
    emit("changed");
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <div class="own-password-form text-end" dir="rtl">
    <div class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
      <KsInput
        v-model="form.currentPassword"
        class="flex-fill"
        input-type="password"
        placeholder="رمز عبور فعلی" />
    </div>
    <div class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
      <KsInput
        v-model="form.newPassword"
        class="flex-fill"
        input-type="password"
        placeholder="رمز عبور جدید" />
      <KsInput
        v-model="form.repeatPassword"
        class="flex-fill"
        input-type="password"
        placeholder="تکرار رمز عبور جدید" />
    </div>
    <p class="text-xSmall mt-3 mb-0">
      {{ passwordRulesText }}
      پس از تغییر رمز، ورود شما در سایر پنجره‌ها و دستگاه‌ها بسته می‌شود.
    </p>
    <div class="d-flex mt-4">
      <KsButton
        :loading="loading"
        color="warn"
        theme="flat"
        title="تغییر رمز عبور"
        @click="submit" />
    </div>
  </div>
</template>
