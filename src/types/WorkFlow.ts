export default interface Workflow {
  id: number;
  workflowGroupId: number | string;
  fromStation: number;
  positionX: number;
  positionY: number;
  title: string;
  displayName: string;
  isDeleted: boolean;
  isStart: boolean;
  isEnd: boolean;
  userId: number | string;
}
