<script lang="ts" setup>
import { useEquipmentAssembly } from "@/store";

import KsModal from "@/components/common/KsModal.vue";
import KsButton from "@/components/common/KsButton.vue";
import BomPreview from "./BomPreview.vue";
import ProcessPreview from "./ProcessPreview.vue";

defineProps({
  visible: {
    type: Boolean,
    default: false,
  },
});
const { previewModal } = useEquipmentAssembly.value;
</script>

<template>
  <KsModal
    :full-screen="previewModal.mode === 'process'"
    :model-value="previewModal.visible"
    :order="0"
    blur
    prevent-close>
    <div
      v-if="previewModal.visible"
      class="p-1 position-relative d-flex flex-column justify-content-between h-100 w-100 overflow-hidden">
      <div
        :style="previewModal.mode !== 'process' && 'max-width: 380px'"
        class="ps-3"
        style="overflow-x: hidden; overflow-y: auto; height: 80vh">
        <BomPreview v-if="previewModal.mode === 'bom'" />
        <ProcessPreview v-else />
      </div>

      <div class="d-flex justify-content-end gap-2 mt-4">
        <KsButton
          color="dark"
          theme="flat"
          title="بستن"
          @click="previewModal.visible = false" />
      </div>
    </div>
  </KsModal>
</template>

<style lang="scss" scoped>
.item-title {
  width: 180px;
  font-weight: bold;
}
</style>
