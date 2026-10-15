import type EquipmentAssembly from "@/types/EquipmentAssembly";
import type EquipmentProcess from "@/types/EquipmentProcess";
import type EquipmentBom from "@/types/EquipmentBom";
import type { Ref } from "vue";
import { computed, reactive, ref } from "vue";
import {
  addEquipmentAssembly,
  addEquipmentBom,
  addEquipmentProcess,
  attachFileEquipmentProcess,
  editEquipmentAssembly,
  getEquipmentBomListAll,
  editEquipmentBom,
  editEquipmentProcess,
  getEquipmentAssemblyList,
  setEquipmentAssemblyPredecessor,
  setEquipmentBomPredecessor,
} from "@/api/EquipmentAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import {
  BASIC_CONFIG_MODEL,
  EQUIPMENT_ASSEMBLY_UPDATE_MODAL_MODEL,
  EQUIPMENT_BOM_UPDATE_MODAL_MODEL,
  EQUIPMENT_PROCESS_UPDATE_MODAL_MODEL,
} from "@/models";
import showSuccessNotification from "@/composables/showSuccessNotification";
import { getMockFile } from "@/composables/getMockFile";

export const useEquipmentAssembly = computed(() => {
  const config = ref({
    ...BASIC_CONFIG_MODEL,
    equipmentId: null,
  });
  const updateAssemblyModal = ref({ ...EQUIPMENT_ASSEMBLY_UPDATE_MODAL_MODEL });
  const updateBomModal = ref({ ...EQUIPMENT_BOM_UPDATE_MODAL_MODEL });
  const updateProcessModal = ref({ ...EQUIPMENT_PROCESS_UPDATE_MODAL_MODEL });
  const equipmentAssemblyList: Ref<EquipmentAssembly[]> = ref([]);
  const previewModal = reactive({
    visible: false,
    mode: null as "bom" | "process",
    bom: null as EquipmentBom,
    process: null as EquipmentProcess,
  });
  const onGoingProcessSwipe = ref({
    bomId: null,
    processIndex: null,
    isUp: false,
  });
  const clonedAssembly: Ref<EquipmentAssembly> = ref(null);
  const clonedBom: Ref<EquipmentBom> = ref(null);
  const clonedProcess: Ref<EquipmentProcess> = ref(null);
  const pasteLoading = ref(false);

  const onPreviewModalRequest = (args) => {
    const { mode, bom, process } = args;

    previewModal.mode = mode;
    if (mode === "bom") previewModal.bom = bom;
    else previewModal.process = process;
    previewModal.visible = true;
  };

  const onCloneAssembly = (assembly: EquipmentAssembly) => {
    clonedAssembly.value = assembly;
  };

  const onCloneBom = (bom: EquipmentBom) => {
    clonedBom.value = bom;
  };

  const onCloneProcess = (process: EquipmentProcess) => {
    clonedProcess.value = process;
    delete clonedProcess.value.equipmentBom;
  };

  const updateEquipmentAssembly = async (
    assembly: EquipmentAssembly,
    isEdit: boolean,
    parentId
  ) => {
    const [data, error] = isEdit
      ? await editEquipmentAssembly(assembly)
      : await addEquipmentAssembly(assembly);
    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      if (parentId) await setEquipmentAssemblyParent(data.data.id, parentId);
      await fetchEquipmentAssemblyList(false, false);
      showSuccessNotification(
        updateAssemblyModal.value.isEdit
          ? "زیر مجموعه با موفقیت ویرایش شد"
          : "زیر مجموعه با موفقیت افزوده شد"
      );
      updateAssemblyModal.value = { ...EQUIPMENT_ASSEMBLY_UPDATE_MODAL_MODEL };
    }
  };

  const setEquipmentAssemblyParent = async (id, parentId) => {
    const [data, error] = await setEquipmentAssemblyPredecessor(id, parentId);
    if (!data?.success) showErrorNotification(data?.message || error.message);
  };

  // const fetchEquipmentBomListAll = async (id) => {
  //   const [data, error] = await getEquipmentBomListAll(id);
  //   if (!data?.success) {
  //     showErrorNotification(data?.message || error.message);
  //   } else {
  //     bom.value = data.data;
  //   }
  // };

  const updateEquipmentBom = async (
    bom: EquipmentBom,
    isEdit: boolean,
    bomPredecessorId
  ) => {
    const [data, error] = isEdit
      ? await editEquipmentBom(bom)
      : await addEquipmentBom(bom);

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      if (bomPredecessorId)
        await setEquipmentBomPredecessorRequest(bom.id, bomPredecessorId);
      showSuccessNotification(
        updateBomModal.value.isEdit
          ? "BOM با موفقیت ویرایش شد"
          : "BOM با موفقیت افزوده شد"
      );
      await fetchEquipmentAssemblyList(false, false);
      updateBomModal.value = { ...EQUIPMENT_BOM_UPDATE_MODAL_MODEL };
    }
  };

  const setEquipmentBomPredecessorRequest = async (bomId, predecessorId) => {
    const [data, error] = await setEquipmentBomPredecessor(
      bomId,
      predecessorId
    );
    const isSuccess = !error && data?.success;
    if (!isSuccess) showErrorNotification(data?.message || error.message);
  };

  const updateEquipmentProcess = async (
    process: EquipmentProcess,
    isEdit: boolean,
    uploadFiles = false
  ) => {
    const [data, error] = isEdit
      ? await editEquipmentProcess(process)
      : await addEquipmentProcess(process);

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      if (data.success) {
        if (uploadFiles) await uploadClonedProcessFiles(data.data.id, process);

        await fetchEquipmentAssemblyList(false, false);
        showSuccessNotification(
          updateProcessModal.value.isEdit
            ? "فرآیند تامین با موفقیت ویرایش شد"
            : "فرآیند تامین با موفقیت افزوده شد"
        );
        updateProcessModal.value = { ...EQUIPMENT_PROCESS_UPDATE_MODAL_MODEL };
      }
    }
  };

  const uploadClonedProcessFiles = async (
    processId,
    process: EquipmentProcess
  ) => {
    for (let i = 0; i < process.files.length; i++) {
      const formData = new FormData();
      formData.append("file", getMockFile());

      await attachFileEquipmentProcess(
        processId,
        process.files[i].description,
        process.files[i].name,
        process.files[i].code,
        formData,
        {
          headers: {
            "Content-Type": "multipart/form-data",
            Authorization: "Bearer " + window.localStorage.getItem("token"),
          },
        }
      );
    }
  };

  const fetchEquipmentAssemblyList = async (
    resetPage = false,
    useLoader = true
  ) => {
    if (resetPage) config.value.activePage = 1;
    if (useLoader) config.value.isLoading = true;
    const [data, error] = await getEquipmentAssemblyList(
      true,
      config.value.equipmentId,
      config.value.searchQuery,
      config.value.entriesQnt,
      config.value.activePage
    );
    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      equipmentAssemblyList.value = data.data.data;
      config.value.totalPage = data.data.totalPages;
    }
    if (useLoader) config.value.isLoading = false;
  };

  const onInit = async (equipmentId) => {
    config.value.equipmentId = equipmentId;
    await fetchEquipmentAssemblyList();
  };

  return {
    config,
    updateAssemblyModal,
    updateBomModal,
    updateProcessModal,
    onGoingProcessSwipe,
    equipmentAssemblyList,
    previewModal,
    clonedAssembly,
    clonedBom,
    clonedProcess,
    pasteLoading,
    onInit,
    onCloneAssembly,
    onCloneBom,
    onCloneProcess,
    updateEquipmentAssembly,
    updateEquipmentBom,
    updateEquipmentProcess,
    fetchEquipmentAssemblyList,
    // fetchEquipmentBomListAll,
    onPreviewModalRequest,
  };
});
