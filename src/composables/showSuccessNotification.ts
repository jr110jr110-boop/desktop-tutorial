import useNotification from "@/components/common/KsNotification/useNotification";

export default (message) => {
  useNotification.addNotification({
    title: "موفق!",
    text: message,
    color: "success",
    duration: 1500,
  });
};
