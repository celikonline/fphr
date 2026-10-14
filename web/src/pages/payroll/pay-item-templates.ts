/** Editable starting points; all expressions are evaluated by the existing server formula engine. */
export const PAY_ITEM_TEMPLATES = [
  { id: "meal", code: "YEMEK_FORMUL", formula: "deger * calisilan_gun" },
  { id: "transport", code: "YOL_FORMUL", formula: "deger * calisilan_gun" },
  { id: "bonus", code: "PRIM_FORMUL", formula: "brut * deger / 100" },
  { id: "commission", code: "KOMISYON_FORMUL", formula: "deger * 0.05" },
  { id: "extraBonus", code: "IKRAMIYE_FORMUL", formula: "aylik_ucret * deger" },
] as const;

export type PayItemTemplateId = (typeof PAY_ITEM_TEMPLATES)[number]["id"];
