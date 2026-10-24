interface Links {
  title: string;
  routeName: string;
  disabled?: boolean;
  hidden?: boolean;
  /** small counter shown beside the title (for example unread inbox items); hidden when 0 or empty */
  badge?: number;
}

export default Links;
