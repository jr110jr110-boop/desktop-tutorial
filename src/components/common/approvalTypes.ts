// Types of the ApprovalTable component (columns and row buttons).
export interface ApprovalColumn {
  key: string;
  title: string;
  /** Cell text from the row (default: the field named by key). */
  text?: (row: any) => string;
  ltr?: boolean;
  /** badge class (ok / warn / bad / info) from the row, the cell text becomes a badge */
  badge?: (row: any) => string;
}
export interface ApprovalAction {
  key: string;
  label: string;
  /** bootstrap button class */
  btn?: string;
  /** a written reason is asked before the call (and sent as the note); required = it must not be empty */
  reason?: boolean | "optional";
  /** text of the question; shown above the reason box */
  hint?: string;
  /** Persian success text */
  done?: string;
}
