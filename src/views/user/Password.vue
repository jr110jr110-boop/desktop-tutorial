<script lang="ts" setup>
// "My password" page: every signed-in user changes the own password here (the navbar button opens it).
import OwnPasswordForm from "@/components/account/OwnPasswordForm.vue";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import LockIcon from "@/components/icons/LockIcon.vue";
import { Colors } from "@/assets/colors";
</script>

<template>
  <KsUpdateForm
    :has-submit-btn="false"
    :is-edit="true"
    form-title="رمز عبور من">
    <template #formIcon>
      <LockIcon :fill="Colors.black" class="ms-3" width="20" />
    </template>
    <template #form>
      <div key="1" class="col-12 col-md-10 mb-5">
        <OwnPasswordForm />
      </div>
    </template>
  </KsUpdateForm>
</template>
