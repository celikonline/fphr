/** Editable starting points; all expressions are evaluated by the existing server formula engine. */
export const PAY_ITEM_TEMPLATES = [
  { id: "meal", code: "YEMEK_FORMUL", formula: "deger * calisilan_gun", direction: "earning" },
  { id: "transport", code: "YOL_FORMUL", formula: "deger * calisilan_gun", direction: "earning" },
  { id: "bonus", code: "PRIM_FORMUL", formula: "brut * deger / 100", direction: "earning" },
  { id: "commission", code: "KOMISYON_FORMUL", formula: "deger * 0.05", direction: "earning" },
  { id: "extraBonus", code: "IKRAMIYE_FORMUL", formula: "aylik_ucret * deger", direction: "earning" },
  { id: "fixedDeduction", code: "SABIT_KESINTI", formula: "deger", direction: "deduction" },
  { id: "percentageDeduction", code: "ORANLI_KESINTI", formula: "brut * deger / 100", direction: "deduction" },
] as const;

export type PayItemTemplateId = (typeof PAY_ITEM_TEMPLATES)[number]["id"];
