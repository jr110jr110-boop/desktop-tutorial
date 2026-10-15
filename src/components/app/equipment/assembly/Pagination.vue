<script lang="ts" setup>
import KsPagination from "@/components/common/KsPagination.vue";
import { useEquipmentAssembly } from "@/store";

const { config, equipmentAssemblyList, fetchEquipmentAssemblyList } =
  useEquipmentAssembly.value;
</script>

<template>
  <KsPagination
    v-if="equipmentAssemblyList.length"
    v-model="config.activePage"
    :length="config.totalPage"
    class="mt-3"
    @onChange="fetchEquipmentAssemblyList" />
</template>
