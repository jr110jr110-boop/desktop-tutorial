import useNotification from "@/components/common/KsNotification/useNotification";

export default (message) => {
  useNotification.addNotification({
    title: "هشدار!",
    text: message,
    color: "warn",
    duration: 3000,
  });
};
