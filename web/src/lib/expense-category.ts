/** Seeded system catalog (`ExpenseCategoryProvisioningService`): their `nameKey` is a resource key, not a display name. */
const SYSTEM_CATEGORY_CODES: readonly string[] = ["travel", "food"];

const SYSTEM_NAME_KEY_PREFIX = "expense.category.";

interface CategoryLike {
  code: string;
  nameKey: string;
}

export function isSystemExpenseCategory(category: Pick<CategoryLike, "code">): boolean {
  return SYSTEM_CATEGORY_CODES.includes(category.code);
}

/**
 * Display name for a category. System categories resolve through `expense:categories.<code>`; tenant-defined ones
 * carry their literal name in `nameKey`. A resource-shaped `nameKey` without a translation never leaks: the code is shown.
 */
export function expenseCategoryName(t: (key: string) => string, category: CategoryLike): string {
  if (isSystemExpenseCategory(category)) return t(`expense:categories.${category.code}`);
  return category.nameKey.startsWith(SYSTEM_NAME_KEY_PREFIX) ? category.code : category.nameKey;
}
