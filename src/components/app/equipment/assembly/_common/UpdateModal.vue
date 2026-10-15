<script setup lang="ts">
import KsModal from "@/components/common/KsModal.vue";
import KsButton from "@/components/common/KsButton.vue";

defineProps({
  visible: {
    type: Boolean,
    default: false,
  },
  submitLoading: {
    type: Boolean,
    default: false,
  },
  isEdit: {
    type: Boolean,
    default: false,
  },
});

const emits = defineEmits(["submit", "cancel"]);
</script>

<template>
  <KsModal :model-value="visible" blur @onClose="emits('cancel')">
    <div
      class="p-1 position-relative d-flex flex-column justify-content-between h-100 overflow-hidden">
      <slot />
      <div class="d-flex justify-content-end gap-2 mt-4">
        <KsButton
          :loading="submitLoading"
          :title="isEdit ? 'ویرایش' : 'افزودن'"
          color="primary"
          @click="emits('submit')" />
        <KsButton
          color="dark"
          theme="flat"
          title="لغو"
          @click="emits('cancel')" />
      </div>
    </div>
  </KsModal>
</template>
