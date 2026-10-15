export type ProgressReport = {
  id: number;
  processId: number;
  processTitle: string;
  contractId: number;
  workOrderId: number;
  workOrderTitle: string;
  workOrderRemainingDays: number;
  plannedStartDate: string;
  actualStartDate: string;
  plannedEndDate: string;
  actualEndDate: string;
  reportDate: string;
  plannedPrice: number;
  actualPrice: number;
  workOrderLevelPlannedProgress: number;
  workOrderLevelActualProgress: number;
  plannedWeightPercent: number;
  actualWeightPercent: number;
  prices: {
    actual: number;
    planned: number;
    date: string;
  }[];
  weightPercents: {
    actual: number;
    planned: number;
    date: string;
  }[];
};
