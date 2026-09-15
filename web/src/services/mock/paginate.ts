import type { PagedResult } from "@/types";

export interface ClientPageQuery {
  page?: number;
  pageSize?: number;
  q?: string;
  sort?: string;
}

/**
 * Client-side pagination/search/sort over an in-memory array, shaped exactly like the
 * backend's PagedResult<T> so mock services are drop-in compatible with the real ones.
 */
export function paginate<T>(
  all: T[],
  query: ClientPageQuery,
  searchableText: (item: T) => string,
  sortBy?: (item: T) => Record<string, string | number>
): PagedResult<T> {
  const page = query.page && query.page > 0 ? query.page : 1;
  const pageSize = query.pageSize && query.pageSize > 0 ? query.pageSize : 20;

  let filtered = all;
  if (query.q && query.q.trim().length > 0) {
    const needle = query.q.trim().toLowerCase();
    filtered = filtered.filter((item) => searchableText(item).toLowerCase().includes(needle));
  }

  if (query.sort && sortBy) {
    const desc = query.sort.startsWith("-");
    const field = desc ? query.sort.slice(1) : query.sort;
    filtered = [...filtered].sort((a, b) => {
      const av = sortBy(a)[field];
      const bv = sortBy(b)[field];
      if (av === bv) return 0;
      const cmp = av > bv ? 1 : -1;
      return desc ? -cmp : cmp;
    });
  }

  const totalCount = filtered.length;
  const start = (page - 1) * pageSize;
  const items = filtered.slice(start, start + pageSize);

  return {
    items,
    page,
    pageSize,
    totalCount,
    totalPages: Math.max(1, Math.ceil(totalCount / pageSize)),
    hasNext: start + pageSize < totalCount,
    hasPrevious: page > 1,
  };
}
