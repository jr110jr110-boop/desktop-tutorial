<script lang="ts" setup>
import { ref, onMounted, onUnmounted, computed } from "vue";
import type { Ref, PropType } from "vue";
import {
  addAllWorkflow,
  addSingleWorkflow,
  getWorkflowById,
} from "@/api/WorkflowAPIs";
import { getUserListEngineerAll } from "@/api/UserAPIs";
import { Colors } from "@/assets/colors";
import router from "@/router";

import KsButton from "@/components/common/KsButton.vue";
import KsModal from "@/components/common/KsModal.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import KsInput from "@/components/common/KsInput.vue";

import type Workflow from "@/types/Workflow";
import type User from "@/types/User";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showWarnNotification from "@/composables/showWarnNotification";

import MenuIcon from "@/components/icons/MenuIcon.vue";
import AngleLeftIcon from "@/components/icons/AngleLeftIcon.vue";
import AddIcon from "@/components/icons/AddIcon.vue";

const submitButtonLoading: Ref<boolean> = ref(false);
const users: Ref<User[]> = ref([]);

const lines = ref([]);
const parentBox: Ref<Workflow> = ref();
const boxIndex = ref();

let dragIndex: number | null = null;
let offsetX = 0;
let offsetY = 0;

const props = defineProps({
  id: {
    type: [Number, String] as PropType<number | string | null>,
    default: null,
  },
});

const workflowSingleInfo: Ref<Workflow> = ref({
  id: 0,
  userId: 0,
  workflowGroupId: props.id,
  fromStation: 0,
  positionX: 70,
  positionY: 70,
  title: "دیسیپلین اول",
  displayName: "",
  isStart: false,
  isEnd: false,
  isDeleted: false,
});

const workflowInfo: Ref<Workflow[]> = ref([
  {
    id: 0,
    userId: 0,
    displayName: "",
    workflowGroupId: props.id,
    fromStation: 0,
    positionX: 70,
    positionY: 70,
    title: "دیسیپلین اول",
    isStart: false,
    isEnd: false,
    isDeleted: false,
  },
]);

const addBoxModal = ref({
  activeIndex: null,
  visible: false,
});

const openAddBoxModal = async (index) => {
  addBoxModal.value = {
    activeIndex: index,
    visible: true,
  };
  workflowSingleInfo.value.title = null;
  workflowSingleInfo.value.userId = 0;
  parentBox.value = workflowInfo.value[index];
};

const editBoxModal = ref({
  activeIndex: null,
  visible: false,
});

const openEditBoxModal = async (index) => {
  editBoxModal.value = {
    activeIndex: index,
    visible: true,
  };
  boxIndex.value = index;
  workflowSingleInfo.value.title = workflowInfo.value[boxIndex.value].title;
  workflowSingleInfo.value.userId = workflowInfo.value[boxIndex.value].userId;
  workflowSingleInfo.value.fromStation =
    workflowInfo.value[boxIndex.value].fromStation;
};

const deleteBoxModal = ref({
  visible: false,
});

function openDeleteBoxModal() {
  deleteBoxModal.value = {
    visible: true,
  };
}

const isFormValid = computed(() => {
  return (
    workflowSingleInfo.value.title !== "" &&
    workflowSingleInfo.value.userId !== 0
  );
});

function startDrag(index: number, event: MouseEvent) {
  dragIndex = index;
  offsetX = event.clientX - workflowInfo.value[index].positionX;
  offsetY = event.clientY - workflowInfo.value[index].positionY;
  window.addEventListener("mousemove", onDrag);
  window.addEventListener("mouseup", stopDrag);
}

function onDrag(event: MouseEvent) {
  if (dragIndex !== null) {
    const box = workflowInfo.value[dragIndex];
    box.positionX = event.clientX - offsetX;
    box.positionY = event.clientY - offsetY;

    updateLines(dragIndex);
  }
}

function stopDrag() {
  updateLines(dragIndex);
  dragIndex = null;
  window.removeEventListener("mousemove", onDrag);
  window.removeEventListener("mouseup", stopDrag);
}

function updateLines(boxIndex: number) {
  const box = workflowInfo.value[boxIndex];

  lines.value.forEach((line) => {
    if (line.from === boxIndex) {
      line.x1 = box.positionX + 75;
      line.y1 = box.positionY + 45;
    }
    if (line.to === boxIndex) {
      line.x2 = box.positionX + 75;
      line.y2 = box.positionY + 45;
    }
    line.lineLength = calculateLineLength(line.x1, line.y1, line.x2, line.y2);
  });
}

function addLineBetweenBoxes(fromBox, toBox) {
  const fromIndex = workflowInfo.value.indexOf(fromBox);
  const toIndex = workflowInfo.value.indexOf(toBox);

  const x1 = fromBox.positionX + 75;
  const y1 = fromBox.positionY + 45;
  const x2 = toBox.positionX + 75;
  const y2 = toBox.positionY + 45;

  const lineLength = calculateLineLength(x1, y1, x2, y2);

  const line = {
    x1: x1,
    y1: y1,
    x2: x2,
    y2: y2,
    from: fromIndex,
    to: toIndex,
    lineLength: lineLength,
  };
  lines.value.push(line);
}

function calculateLineLength(x1, y1, x2, y2) {
  return Math.sqrt((x2 - x1) ** 2 + (y2 - y1) ** 2);
}

function editBox() {
  if (
    workflowInfo.value.length == 1 &&
    workflowInfo.value[0].title == "دیسیپلین اول"
  ) {
    workflowInfo.value[0].title = workflowSingleInfo.value.title;
    workflowInfo.value[0].userId = workflowSingleInfo.value.userId;
  } else {
    workflowInfo.value[boxIndex.value].title = workflowSingleInfo.value.title;
    workflowInfo.value[boxIndex.value].userId = workflowSingleInfo.value.userId;
  }
  editBoxModal.value.visible = false;
}

function deleteBoxItem() {
  const index = editBoxModal.value.activeIndex;

  if (index === 0) {
    showWarnNotification("اولین باکس قابل حذف نیست!");
    deleteBoxModal.value.visible = false;
    return;
  }

  lines.value = lines.value.filter(
    (line) => line.from !== index && line.to !== index
  );

  workflowInfo.value[index].isDeleted = true;
  sendAllInPageWorkflow();

  deleteBoxModal.value.visible = false;
  editBoxModal.value.visible = false;
}

async function fetchDetail() {
  submitButtonLoading.value = true;
  const [data, error] = await getWorkflowById(props.id);
  if (error) {
    if (error.message != "Request failed with status code 401") {
      showErrorNotification(data?.message || error.message);
    }
  } else {
    if (data.success) {
      if (data.data == null || data.data.length == 0) {
        workflowSingleInfo.value.fromStation = 0;
        workflowSingleInfo.value.title = workflowSingleInfo.value.title;
        workflowSingleInfo.value.userId = users.value[0].id;
        sendSingleWorkflow();
        editBoxModal.value.visible = true;
      }

      if (data.data.length >= 1) {
        workflowInfo.value = data.data;
        workflowInfo.value.forEach((box) => {
          if (box.fromStation !== 0) {
            const fromBox = workflowInfo.value.find(
              (b) => b.id === box.fromStation
            );
            if (fromBox) {
              addLineBetweenBoxes(fromBox, box);
            }
          }
        });
      }
    } else {
      showErrorNotification(data?.message || error.message);
    }
  }
  submitButtonLoading.value = false;
}

async function fetchUserList() {
  submitButtonLoading.value = true;
  const [data, error] = await getUserListEngineerAll();
  if (error) {
    if (error.message != "Request failed with status code 401") {
      showErrorNotification(data?.message || error.message);
    }
  } else {
    if (data.success) {
      if (data.data.data.length >= 1) {
        users.value = data.data.data;
      }
    } else {
      showErrorNotification(data?.message || error.message);
    }
  }
  submitButtonLoading.value = false;
}

async function addNewBox() {
  const newBox: Workflow = {
    id: 0,
    userId: workflowSingleInfo.value.userId,
    workflowGroupId: Number(props.id),
    positionX: parentBox.value.positionX + 200,
    positionY: parentBox.value.positionY,
    title: workflowSingleInfo.value.title,
    displayName: workflowSingleInfo.value.displayName,
    isStart: false,
    isEnd: false,
    isDeleted: false,
    fromStation: parentBox.value.id,
  };

  workflowInfo.value.push(newBox);
  addBoxModal.value.visible = false;
  addLineBetweenBoxes(parentBox.value, newBox);
  workflowSingleInfo.value.fromStation = 0;
  workflowSingleInfo.value.title = workflowSingleInfo.value.title;
  workflowSingleInfo.value.userId = workflowSingleInfo.value.userId;
  sendSingleWorkflow();
}

async function sendSingleWorkflow() {
  submitButtonLoading.value = true;

  const [data, error] = await addSingleWorkflow(workflowSingleInfo.value);

  if (error) {
    showErrorNotification(data?.message || error.message);
  } else {
    if (data.success == true) {
      workflowInfo.value[workflowInfo.value.length - 1].id = data.data;
      sendAllInPageWorkflow();
      fetchDetail();
    } else {
      showErrorNotification(data?.message || error.message);
    }
  }
  submitButtonLoading.value = false;
}

async function sendAllInPageWorkflow() {
  submitButtonLoading.value = true;
  const [data, error] = await addAllWorkflow(workflowInfo.value);

  if (error) {
    showErrorNotification(data?.message || error.message);
  } else {
    if (data.success == true) {
      // showSuccessNotification("باکس ها با موفقیت ثبت شدند !");
      // router.back();
    } else {
      showErrorNotification(data?.message || error.message);
    }
  }
  submitButtonLoading.value = false;
}

async function sendAllWorkflow() {
  submitButtonLoading.value = true;
  const [data, error] = await addAllWorkflow(workflowInfo.value);

  if (error) {
    showErrorNotification(data?.message || error.message);
  } else {
    if (data.success == true) {
      showSuccessNotification("باکس ها با موفقیت ثبت شدند !");
      router.back();
    } else {
      showErrorNotification(data?.message || error.message);
    }
  }
  submitButtonLoading.value = false;
}

function getLastVisibleIndex() {
  for (let i = workflowInfo.value.length - 1; i >= 0; i--) {
    if (!workflowInfo.value[i].isDeleted) {
      return i;
    }
  }
  return -1;
}

function getFirstVisibleIndex() {
  for (let i = 0; i < workflowInfo.value.length; i++) {
    if (!workflowInfo.value[i].isDeleted) {
      return i;
    }
  }
  return -1;
}

onMounted(async () => {
  await fetchUserList();
  await fetchDetail();
});

onUnmounted(() => {
  window.removeEventListener("mousemove", onDrag);
  window.removeEventListener("mouseup", stopDrag);
});
</script>

<template>
  <div class="flowchart">
    <svg class="connections">
      <defs v-for="(line, index) in lines" :key="index">
        <marker
          :id="'arrow-' + index"
          viewBox="0 0 10 10"
          :refX="line.lineLength / 2 - 10"
          refY="5"
          markerWidth="6"
          markerHeight="6"
          orient="auto-start-reverse">
          <path d="M 0 0 L 10 5 L 0 10 z" fill="black" />
        </marker>
      </defs>
      <line
        v-for="(line, index) in lines"
        :key="index"
        :x1="line.x1"
        :y1="line.y1"
        :x2="line.x2"
        :y2="line.y2"
        stroke="black"
        stroke-width="2"
        :marker-end="'url(#arrow-' + index + ')'" />
    </svg>

    <div v-for="(box, index) in workflowInfo" :key="index">
      <div class="position-relative" v-if="!box.isDeleted">
        <div
          :style="{ top: box.positionY + 'px', left: box.positionX + 'px' }"
          class="box"
          @mousedown="startDrag(index, $event)">
          <div class="menu-btn-box" @mousedown.stop>
            <MenuIcon
              @click="openEditBoxModal(index)"
              fill="#664D03"
              class="menu-btn" />
          </div>
          <span class="box-name input-color">{{ box.title }}</span>
          <span class="user-name">{{ box.displayName }} </span>

          <div class="add-btn-box">
            <KsButton
              v-if="index === getLastVisibleIndex()"
              @click="openAddBoxModal(index)"
              tooltip="اضافه"
              color="success"
              fixed-width-height="30">
              <template #content>
                <AddIcon class="btn-icons" :fill="Colors.white" width="11" />
              </template>
            </KsButton>
          </div>
        </div>
      </div>
    </div>

    <div class="d-grid d-sm-flex justify-content-sm-end" dir="rtl">
      <KsButton
        color="dark"
        theme="flat"
        @click="$router.back()"
        fixed-width-height="42"
        class="p-1">
        <template #content>
          <AngleLeftIcon width="6" :fill="Colors.primary" />
        </template>
      </KsButton>
    </div>
    <div
      class="position-fixed translate-middle"
      style="top: 90%; left: 50%"
      dir="rtl">
      <div class="d-flex justify-content-center">
        <KsButton
          @click="sendAllWorkflow()"
          class="py-1 px-2 fw-bold text-small"
          title="ثبت و خروج"
          color="secondary" />
      </div>
    </div>

    <KsModal blur v-model="editBoxModal.visible" dir="rtl">
      <div class="background-color p-1 rounded-3 text-end">
        <div class="list-title">
          <span class="fw-bold text-large text-color">دیسیپلین کنترل کیفی</span>
        </div>
        <div
          class="d-flex flex-column gap-3 justify-content-between mt-3"
          dir="rtl">
          <KsInput
            v-model="workflowSingleInfo.title"
            placeholder="عنوان"
            class="d-block mt-3" />
          <KsSelect
            v-model="workflowSingleInfo.userId"
            :options="users"
            placeholder="کاربر"
            class="mt-3"
            value-prop-name="id"
            label-prop-name="displayName" />
        </div>
        <div class="d-flex justify-content-center gap-3 mt-4" dir="rtl">
          <KsButton
            @click="editBox"
            color="success"
            title="ویرایش"
            theme="flat"
            class="mt-2" />
          <KsButton
            @click="openDeleteBoxModal"
            :disabled="
              editBoxModal.activeIndex !== getLastVisibleIndex() ||
              editBoxModal.activeIndex === getFirstVisibleIndex()
            "
            color="danger"
            title="حذف"
            theme="flat"
            class="mt-2" />
          <KsButton
            @click="editBoxModal.visible = false"
            color="warn"
            title="بستن"
            theme="flat"
            class="mt-2" />
        </div>
      </div>
    </KsModal>

    <KsModal blur v-model="addBoxModal.visible">
      <div class="background-color p-1 rounded-3 text-end">
        <div class="list-title px-2">
          <span class="fw-bold text-large text-color" dir="rtl">
            دیسیپلین کنترل کیفی
          </span>
        </div>
        <div
          class="d-flex flex-column gap-3 justify-content-between mt-3"
          dir="rtl">
          <KsInput
            v-model="workflowSingleInfo.title"
            placeholder="عنوان"
            class="d-block mt-3" />
          <KsSelect
            v-model="workflowSingleInfo.userId"
            :options="users"
            placeholder="کاربر"
            class="mt-3"
            value-prop-name="id"
            label-prop-name="displayName" />
        </div>
        <div class="d-flex justify-content-center gap-3 mt-4" dir="rtl">
          <KsButton
            @click="addNewBox"
            :disabled="!isFormValid"
            color="success"
            title="تایید"
            theme="flat"
            class="mt-2" />
          <KsButton
            @click="addBoxModal.visible = false"
            color="danger"
            title="لغو"
            theme="flat"
            class="mt-2" />
        </div>
      </div>
    </KsModal>

    <KsModal blur v-model="deleteBoxModal.visible">
      <div class="text-center pt-2">
        <span class="color-danger fw-bold text-large"> هشدار </span>
      </div>
      <div class="text-center my-3" dir="rtl">
        <span class="text-small"> از حذف آیتم انتخاب شده مطمئن هستید؟ </span>
      </div>
      <div class="d-flex gap-2 mt-3" dir="ltr">
        <KsButton
          color="danger"
          theme="flat"
          title="تایید"
          @click="deleteBoxItem" />
        <KsButton
          @click="deleteBoxModal.visible = false"
          color="transparent"
          title="لغو" />
      </div>
    </KsModal>
  </div>
</template>

<style scoped>
.flowchart {
  position: relative;
  width: 100%;
  height: 100vh;
}

.connections {
  position: absolute;
  width: 100%;
  height: 100%;
  pointer-events: none;
  z-index: 0;
}

.box {
  position: absolute;
  width: 150px;
  height: 90px;
  border-radius: 8px;
  background: rgb(var(--box-color));
  box-shadow: 0 4px 8px rgba(0, 0, 0, 0.1);
  padding: 10px;
  cursor: grab;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  transition: box-shadow 0.2s ease;
  z-index: 2;
}

.box:hover {
  box-shadow: 0 6px 12px rgba(0, 0, 0, 0.15);
}

.box-name {
  width: 90%;
  height: 30px;
  margin-bottom: 5px;
  text-align: center;
  border: 1px solid #ddd;
  outline: none;
  font-size: 14px;
  font-weight: bold;
  padding: 4px;
  border-radius: 4px;
}

.box-color {
  width: 25px;
  height: 25px;
  border: none;
  margin-top: 5px;
  cursor: pointer;
}

.add-new-btn {
  width: 150px;
}

.add-btn,
.delete-btn,
.connect-btn {
  margin-top: 5px;
  color: white;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 12px;
}
.modal {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background-color: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
}

.modal-content {
  background-color: white;
  padding: 20px;
  border-radius: 8px;
  width: 300px;
  text-align: center;
}

.menu-btn-box {
  position: absolute;
  top: -12px;
  left: 110px;
  cursor: pointer;
}

.menu-btn {
  width: 26px;
  height: 26px;
  border-radius: 100px;
  background-color: #fff3cd;
  border: 1px solid #ffecb5;
  padding: 5px;
}

.add-btn-box {
  position: absolute;
  bottom: -12px;
  left: 50%;
  cursor: pointer;
  transform: translate(-15px, 0);
}
.user-name {
  direction: rtl;
  font-size: 11px;
  line-height: 15px;
  color: #aaa;
}
</style>
