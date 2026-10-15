export default interface CardBoardUserProcess {
  id: number | string;
  title: string;
  assemblies: _Assembly[];
}

interface _Assembly {
  id: number | string;
  title: string;
  workOrderId: number | string;
  workOrderTitle: string;
  boms: _Bom[];
}

interface _Bom {
  id: number | string;
  title: string;
  proccesses: _Process[];
}

interface _Process {
  id: number | string;
  title: string;
  exitCommodityCode: string;
  enterCommodityCode: string;
  mapDuration: number;
  masterDuration: number;
  processDuration: number;
  sort: number;
  programPrice: number;
  needsMasterAcceptance: boolean;
  weightPercent: string;
  files: _Files[];
  userName: string;
  userId: number;
  typeDescription: string;
  type: number;
  processStatusDescription: string;
  processStatus: number;
  isSelected?: boolean;
}

interface _Files {
  description: string;
  documentId: string;
  id: number;
  major: number;
  minor: number;
  processId: number;
  regDate: string;
  regDateString: string;
  userDispalyName: string;
  userId: number;
  version: string;
}
