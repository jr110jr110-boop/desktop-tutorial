<script lang="ts" setup>
import { onMounted } from "vue";
import { useEquipmentAssembly } from "@/store";

import AssemblyItem from "@/components/app/equipment/assembly/assemblyItem";
import BomItem from "@/components/app/equipment/assembly/bomItem";
import ProcessItem from "@/components/app/equipment/assembly/processItem";
import PreviewModal from "@/components/app/equipment/assembly/previewModal";
import AddAssemblyModal from "@/components/app/equipment/assembly/assemblyItem/Update.vue";
import ListHeader from "@/components/app/equipment/assembly/ListHeader.vue";
import Pagination from "@/components/app/equipment/assembly/Pagination.vue";
import NoDataFound from "@/layouts/NoDataFound.vue";
import ListLoader from "@/layouts/ListLoader.vue";

const props = defineProps({
  equipmentId: {
    type: String,
    required: true,
  },
});

const {
  config,
  equipmentAssemblyList,
  updateAssemblyModal,
  pasteLoading,
  onInit,
} = useEquipmentAssembly.value;

onMounted(async () => {
  await onInit(props.equipmentId);
});
</script>

<template>
  <div class="position-relative">
    <div
      :class="{ 'opacity-50 no-pointer-event': pasteLoading }"
      class="background-color p-3 shadow-sm rounded-3 text-end">
      <ListHeader />

      <!--   List   -->
      <transition-group mode="out-in" name="fade">
        <div
          v-if="!config.isLoading && equipmentAssemblyList.length"
          key="list"
          class="d-flex flex-column gap-2">
          <AssemblyItem
            v-for="(assembly, indexAssembly) in equipmentAssemblyList"
            :key="indexAssembly"
            :assembly="assembly">
            <BomItem
              v-for="(bom, indexBom) in assembly.equipmentBoms"
              :key="indexBom"
              :assembly-id="assembly.id"
              :bom="bom"
              :other-boms="assembly.equipmentBoms">
              <transition-group
                v-if="bom.equipmentProcesses && bom.equipmentProcesses.length"
                class="d-flex flex-column gap-2"
                name="list"
                tag="div">
                <ProcessItem
                  v-for="(process, indexProcess) in bom.equipmentProcesses"
                  :key="indexProcess"
                  :bom-id="bom.id"
                  :index="indexProcess"
                  :is-last-item="
                    indexProcess === bom.equipmentProcesses.length - 1
                  "
                  :process="process" />
              </transition-group>
            </BomItem>
          </AssemblyItem>
        </div>

        <div key="KsSkeletonLoader">
          <ListLoader :active="config.isLoading" />
        </div>
        <div key="ListPagination">
          <Pagination />
        </div>
        <div key="ListNoDataFound">
          <NoDataFound
            v-if="!config.isLoading && !equipmentAssemblyList.length" />
        </div>
      </transition-group>

      <!--    MODALS   -->
      <PreviewModal />
      <AddAssemblyModal
        v-if="
          updateAssemblyModal.visible &&
          !updateAssemblyModal.isEdit &&
          !updateAssemblyModal.parentId
        " />
    </div>

    <div v-if="pasteLoading" class="custom-loader center" />
  </div>
</template>

<style lang="scss" scoped>
.sort-btn {
  width: 28px;
  height: 24px;
  display: flex;
  place-content: center;
  border-radius: 50%;

  &:hover {
    background: rgba(var(--text-color), 0.1);
  }
}

.list-move {
  transition: transform 0.8s ease;
}
</style>
