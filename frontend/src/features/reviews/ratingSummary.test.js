import { describe, expect, it } from 'vitest';
import { distributionRows, submittedMessageKey } from './ratingSummary';

describe('rating summary', () => {
  it('turns the approved distribution into five bars from 5 stars down', () => {
    const rows = distributionRows([{ rating: 5, count: 3 }, { rating: 2, count: 1 }], 4);
    expect(rows.map((r) => r.rating)).toEqual([5, 4, 3, 2, 1]);
    expect(rows.map((r) => [r.count, r.percent])).toEqual([[3, 75], [0, 0], [0, 0], [1, 25], [0, 0]]);
  });

  it('shows empty bars without dividing by zero', () => {
    expect(distributionRows(undefined, 0).every((r) => r.count === 0 && r.percent === 0)).toBe(true);
  });

  it('tells the customer whether the review is live or awaiting the store', () => {
    expect(submittedMessageKey('Pending')).toBe('reviews.submittedPending');
    expect(submittedMessageKey('Approved')).toBe('reviews.submitted');
    expect(submittedMessageKey(undefined)).toBe('reviews.submitted');
  });
});
