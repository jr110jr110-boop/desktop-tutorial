<script lang="ts" setup>
import type TableValue from "@/types/TableValue";
import type { PropType } from "vue";
import { reactive } from "vue";
import moment from "moment-jalaali";

import KsButton from "@/components/common/KsButton.vue";
import KsCheckBox from "@/components/common/KsCheckBox.vue";
import AngleLeftIcon from "@/components/icons/AngleLeftIcon.vue";
import MinusIcon from "@/components/icons/MinusIcon.vue";
import TrashIcon from "@/components/icons/TrashIcon.vue";
import EditIcon from "@/components/icons/EditIcon.vue";
import MenuIcon from "@/components/icons/MenuIcon.vue";
import { Colors } from "@/assets/colors";

defineProps({
  tHeads: {
    type: Array as PropType<string[]>,
    default: () => [],
  },
  tValues: {
    type: Array as PropType<TableValue[]>,
    required: true,
  },
  tData: {
    type: Array as PropType<any[]>,
    required: true,
  },
  updateRouteName: String,
  updateRouteQueries: {
    type: Object as PropType<any>,
    default: null,
  },
  isAllItemsSelected: Boolean as PropType<boolean>,
  hasNode: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
  nodePropName: {
    type: String,
  },
  nodeColeSpan: {
    type: Number as PropType<number>,
  },
  selectedItems: {
    type: Array as PropType<number[]>,
    default: () => [],
  },
  nodeParentId: {
    type: Number as PropType<number | null>,
    default: null,
  },
  hasDeleteBtn: {
    type: Boolean as PropType<boolean>,
    default: true,
  },
  hasEditBtn: {
    type: Boolean as PropType<boolean>,
    default: true,
  },
  hasCheckBox: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
  hasCustomActionBtn: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
  isChild: {
    type: Boolean,
    default: false,
  },
});

defineEmits([
  "onValueClick",
  "deleteItem",
  "toggleSelectedItems",
  "toggleAllSelectedItems",
  "openImagePreviewModal",
]);

const apiBaseUrl = import.meta.env.VITE_API_URL;

moment.loadPersian({ dialect: "persian-modern" });

const state = reactive({
  singleSelectedItem: null as number | null,
  nodesVisibility: [],
});

function toggleNodeVisibility(index) {
  for (let i = 0; i < state.nodesVisibility.length; i++)
    if (state.nodesVisibility[i] === index) {
      state.nodesVisibility.splice(i, 1);
      return;
    }
  state.nodesVisibility.push(index);
}

function abbreviateNumber(number: number) {
  const SiSymbols = ["", "K", "M", "G", "T", "P", "E"];

  const tier = (Math.log10(Math.abs(number)) / 3) | 0;

  if (tier === 0) return number;

  const suffix = SiSymbols[tier];
  const scale = Math.pow(10, tier * 3);

  const scaled = number / scale;

  return scaled.toFixed(1) + suffix;
}

function getDateJalali(date: string) {
  return moment(date, "YYYY-MM-DD").format("dddd jDD jMMMM jYYYY");
}
</script>

<template>
  <div class="min-w-0">
    <div
      :class="isChild ? 'child-table' : 'parent-table'"
      class="ks-table table-responsive unselectable-text">
      <table class="table table-borderless IRANYekan">
        <thead class="table-th-background fw-bold text-xSmall">
          <tr class="border-0">
            <th
              v-if="hasNode"
              class="node-button"
              scope="col"
              style="width: 24px">
              <AngleLeftIcon :fill="Colors.primaryLight" width="5" />
            </th>
            <th
              v-if="hasCheckBox"
              :class="{ 'has-node': hasNode }"
              class="checkbox-col"
              scope="col">
              <KsCheckBox
                :model-value="isAllItemsSelected"
                color="primary"
                custom-handle-change
                @change="$emit('toggleAllSelectedItems')" />
            </th>
            <th
              v-for="(head, index) in tHeads"
              :key="index + 'head'"
              :style="
                tValues[index].maxWidth
                  ? 'min-width:' + tValues[index].maxWidth + 'px'
                  : 'min-width: 92px'
              "
              scope="col">
              {{ head }}
            </th>
            <th
              v-if="hasEditBtn || hasDeleteBtn || hasCustomActionBtn"
              class="text-center"
              scope="col"
              style="width: 1px">
              عملیات
            </th>
          </tr>
        </thead>
        <transition-group
          v-for="(data, index) in tData"
          :key="index + 'data'"
          :class="{ 'has-hover': tHeads.length }"
          :duration="200"
          class="text-small"
          name="fade"
          tag="tbody">
          <tr :key="index" class="align-middle">
            <td
              v-if="hasNode"
              class="cursor-pointer"
              style="width: 24px"
              @click="data[nodePropName].length && toggleNodeVisibility(index)">
              <AngleLeftIcon
                v-if="data[nodePropName].length"
                :class="{
                  'angle-down': state.nodesVisibility.includes(index),
                }"
                :fill="Colors.primaryLight"
                width="5" />
              <MinusIcon v-else :fill="Colors.primaryLight" width="8" />
            </td>
            <td
              v-if="hasCheckBox"
              :class="{ 'has-node': hasNode }"
              class="checkbox-col">
              <KsCheckBox
                :model-value="selectedItems.includes(data.id)"
                color="primary"
                custom-handle-change
                @change="
                  (val) =>
                    $emit('toggleSelectedItems', {
                      item: data,
                      status: val,
                      parentId: nodeParentId,
                    })
                " />
            </td>
            <td
              v-for="(value, indexVal) in tValues"
              :key="indexVal + 'val'"
              :class="{ 'text-center': value.textCenter }"
              :style="value.maxWidth && 'max-width:' + value.maxWidth + 'px'"
              class="text-xSmall">
              <!--        IMAGE MODE         -->
              <button
                v-if="value.type === 'image'"
                class="bg-transparent border-0"
                @click="
                  $emit(
                    'openImagePreviewModal',
                    apiBaseUrl +
                      '/storage/' +
                      (value.subPropName
                        ? data[value.propName][value.subPropName]
                        : data[value.propName])
                  )
                ">
                <img
                  :src="
                    data[value.propName] &&
                    apiBaseUrl +
                      '/storage/' +
                      (value.subPropName
                        ? data[value.propName][value.subPropName]
                        : data[value.propName])
                  "
                  alt=""
                  class="img-fluid"
                  style="width: 52px; height: 52px; object-fit: contain" />
              </button>

              <!--        STATUS MODE         -->
              <div v-if="value.type === 'status'">
                <div
                  v-for="(status, indexStatus) in value.status"
                  :key="indexStatus + 'status'"
                  class="d-flex">
                  <KsButton
                    v-if="data[value.propName] === status.value"
                    :color="status.color"
                    :title="status.title"
                    bold
                    custom-padding="2px 8px"
                    theme="flat" />
                </div>
              </div>

              <!--        TEXT MODE        -->
              <div
                v-if="value.type === 'text'"
                :class="{ 'cursor-pointer': value.clickable }"
                @click="
                  value.clickable &&
                    $emit('onValueClick', { data: data, index: index })
                ">
                <span>{{ data[value.propName] }}</span>
              </div>

              <!--        PRICE MODE        -->
              <span
                v-if="value.type === 'price'"
                class="fw-bold"
                style="white-space: pre">
                {{ data[value.propName].toLocaleString("ar-SA") }}
              </span>

              <!--        Percent MODE        -->
              <span
                v-if="value.type === 'percent'"
                class="fw-bold"
                style="white-space: pre">
                %{{ data[value.propName] }}
              </span>

              <!--        SI-NUM MODE        -->
              <span v-if="value.type === 'si-number'" class="fw-bold">
                {{ abbreviateNumber(data[value.propName]) }}
              </span>

              <!--        DATE MODE        -->
              <span v-if="value.type === 'date'" style="white-space: pre">
                {{
                  data[value.propName]
                    ? getDateJalali(data[value.propName])
                    : ""
                }}
              </span>
            </td>
            <td v-if="hasEditBtn || hasDeleteBtn || hasCustomActionBtn">
              <div class="d-flex justify-content-end gap-2">
                <KsButton
                  color="dark"
                  fixed-width-height="32"
                  has-drop-down-menu
                  menu-direction="left"
                  theme="flat">
                  <template #content>
                    <MenuIcon width="14" />
                  </template>
                  <template #dropdown-menu>
                    <ul class="table-action-btn-menu" @click.stop>
                      <slot
                        :row-data="data"
                        :row-index="index"
                        name="custom-action-btn"></slot>
                      <li
                        v-if="hasEditBtn"
                        class="gap-3"
                        @click="
                          $router.push({
                            name: updateRouteName,
                            params: { id: data.id },
                            query: updateRouteQueries,
                          })
                        ">
                        <EditIcon :fill="Colors.black" width="16" />
                        <span class="text-small">ویرایش</span>
                      </li>
                      <li
                        v-if="hasDeleteBtn"
                        class="gap-3"
                        @click="$emit('deleteItem', data.id)">
                        <TrashIcon :fill="Colors.black" width="16" />
                        <span class="text-small">حذف</span>
                      </li>
                    </ul>
                  </template>
                </KsButton>

                <!--                        <KsButton-->
                <!--                            v-if="hasEditBtn"-->
                <!--                            color="dark"-->
                <!--                            tooltip="ویرایش"-->
                <!--                            fixed-width-height="42"-->
                <!--                            @click="$router.push({name: updateRouteName, params: {id: data.id}, query: updateRouteQueries})">-->
                <!--                           <template #content>-->
                <!--                              <EditIcon :fill="Colors.white" width="16"/>-->
                <!--                           </template>-->
                <!--                        </KsButton>-->
                <!--                        <KsButton-->
                <!--                            v-if="hasDeleteBtn"-->
                <!--                            color="danger"-->
                <!--                            tooltip="حذف"-->
                <!--                            fixed-width-height="42"-->
                <!--                            @click="$emit('deleteItem', data.id)">-->
                <!--                           <template #content>-->
                <!--                              <TrashIcon :fill="Colors.white" width="16"/>-->
                <!--                           </template>-->
                <!--                        </KsButton>-->
              </div>
            </td>
          </tr>
          <tr
            v-if="
              hasNode &&
              data[nodePropName].length &&
              state.nodesVisibility.includes(index)
            ">
            <td :colspan="nodeColeSpan">
              <KsTable
                :node-cole-span="nodeColeSpan"
                :node-parent-id="data.id"
                :node-prop-name="nodePropName"
                :selected-items="selectedItems"
                :t-data="data[nodePropName]"
                :t-heads="tHeads"
                :t-values="tValues"
                :update-route-name="updateRouteName"
                :update-route-queries="updateRouteQueries"
                has-node
                is-child
                @deleteItem="(val) => $emit('deleteItem', val)"
                @openImagePreviewModal="
                  (val) => $emit('openImagePreviewModal', val)
                "
                @toggleSelectedItems="
                  (val) => $emit('toggleSelectedItems', val)
                ">
                <template #custom-action-btn="{ rowData, rowIndex }">
                  <slot
                    :row-data="rowData"
                    :row-index="rowIndex"
                    name="custom-action-btn" />
                </template>
              </KsTable>
            </td>
          </tr>
        </transition-group>
      </table>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.table-responsive {
  &.parent-table {
    padding-bottom: 16px;

    border: {
      top-left-radius: 16px;
      top-right-radius: 16px;
    }

    & > .table {
      & > :not(caption) > * > * {
        padding: 12px 16px !important;
      }
    }
  }

  &.child-table {
    margin-top: -42px;

    thead {
      visibility: hidden;
    }

    & > .table {
      & > :not(caption) > * > * {
        padding: 12px 16px !important;
      }
    }
  }

  .has-hover > tr:hover > td {
    background-color: rgba(var(--black), 0.04);
  }

  .table {
    margin-bottom: 0;

    & > :not(caption) > * > * {
      padding-left: 0 !important;
    }

    .checkbox-col {
      width: 32px;
      padding-left: 4px !important;

      &.has-node {
        padding-right: 4px !important;
      }
    }

    .table-action-btn-menu {
      li {
        display: flex;
        flex-direction: row-reverse;
        align-items: center;
      }
    }
  }
}
</style>
