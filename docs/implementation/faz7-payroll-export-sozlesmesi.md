# Faz 7 — Bordro ve Masraf Entegrasyonu API Sözleşmesi

## S-14: Bordro Excel Özeti

`GET /api/v1/payroll/periods/{id}/export` yanıtı `.xlsx` formatında bordro özet listesi döner.

**Kolonlar:**
- Sicil (Bordro Kodu) — `EmployeePayrollProfile.ExternalPayrollCode`, boşsa hücre boş
- Ad Soyad
- Brüt
- SGK İşçi
- İşsizlik Sigortası İşçi
- Gelir Vergisi (minwage exemption sonrası)
- Damga Vergisi (minwage exemption sonrası)
- Net
- Masraf İadesi (belgelenmiş kalemler, vergisiz)
- Fişsiz Masraf (Bordroya Eklenmedi) — belgesiz masraf iadesi

**Özet Sayfası:**
- Toplam çalışan sayısı
- Bordro kodu tanımsız çalışan sayısı

**Query Parametresi:**
- `costCenterId` (opsiyonel): dönem payslip'lerini belirtilen maliyet merkezine filtreler

---

## K-7: SGK e-Bildirge ve Banka Ödeme Dosyası (İlk Sürüm Dışı)

Faz 7 ilk sürümü kapsamında **SGK e-Bildirge** ve **banka ödeme dosyası** üretimi yer almaz. Bu formatlar P5+ backlog'a alınmıştır. Diğer mevzuat entegrasyonları (muhasebe yazılımı export, dış bordro hizmetine aktarım) da ilk sürüm dışıdır.

**Çözüm:** Bordro verisinin excel özeti (S-14) ile manuel müdahale mümkündür.

---

## Uzantılar

Diğer sözleşmeler (Expense → Payroll köprüsü, Attendance workedDays, Leave devamsızlık) ilgili modülün belgelerine yazılmıştır.
