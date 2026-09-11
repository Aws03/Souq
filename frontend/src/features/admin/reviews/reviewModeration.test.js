import { describe, expect, it } from 'vitest';
import { buildReviewQuery, moderationActions } from './reviewModeration';

describe('review moderation', () => {
  it('builds the queue query with a known status and a valid product only', () => {
    expect(buildReviewQuery({ status: 'Pending', productId: '12', page: 2 })).toEqual({ page: 2, pageSize: 20, status: 'Pending', productId: 12 });
    expect(buildReviewQuery({ status: '', productId: '' })).toEqual({ page: 1, pageSize: 20 });
    expect(buildReviewQuery({ status: 'Deleted', productId: '-3' })).toEqual({ page: 1, pageSize: 20 });
  });

  it('offers the moderation decisions each status allows', () => {
    expect(moderationActions({ status: 'Pending' })).toEqual(['approve', 'reject']);
    expect(moderationActions({ status: 'Approved' })).toEqual(['reject']);
    expect(moderationActions({ status: 'Rejected' })).toEqual(['approve']);
    expect(moderationActions({ status: 'Unknown' })).toEqual([]);
  });
});
