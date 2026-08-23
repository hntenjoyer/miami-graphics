export interface RatingAgg { avg: number; count: number }

export const RATING_CONFIDENCE_M = 5;

export function globalMeanRating(ratings: Record<string, RatingAgg>): number {
  let sum = 0, n = 0;
  for (const r of Object.values(ratings)) {
    if (r && r.count > 0) { sum += r.avg * r.count; n += r.count; }
  }
  return n > 0 ? sum / n : 0;
}

export function bayesianScore(agg: RatingAgg | undefined, globalMean: number, m = RATING_CONFIDENCE_M): number {
  if (!agg || agg.count <= 0) return -Infinity;
  return (agg.count / (agg.count + m)) * agg.avg + (m / (agg.count + m)) * globalMean;
}
