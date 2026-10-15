import useNotification from "@/components/common/KsNotification/useNotification";

export default (message) => {
  useNotification.addNotification({
    title: "خطا!",
    text: message,
    color: "danger",
    duration: 10000,
  });
};
