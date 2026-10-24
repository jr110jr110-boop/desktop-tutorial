// Company decision (1405/08/02): one company, rial only. The second company (legal entities,
// intercompany, consolidation) and foreign currency (FX rates, revaluation, letters of credit,
// currency fields) are switched off. Set to true when the backend flags
// Finance:MultiEntity / Finance:ForeignCurrency are switched on.
export const Features = {
  multiEntity: false,
  foreignCurrency: false,
};
