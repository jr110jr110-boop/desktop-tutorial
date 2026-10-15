interface Notification {
  color?:
    | "secondary"
    | "primary"
    | "dark"
    | "danger"
    | "warn"
    | "success"
    | "white";
  title?: string;
  text?: string;
  position?:
    | "top"
    | "bottom"
    | "left-top"
    | "left-bottom"
    | "right-top"
    | "right-bottom";
  content?: string;
  duration?: number | null;
  icon?: string;
  closeable?: boolean;
  id?: number;
  ltr?: boolean;
  closeOnClick?: boolean;
}

export default Notification;
