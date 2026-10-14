# FP-SUPPORT-001 — FPHR merkezi destek bağlantısı

İş kartı: `docs/takim/isler/FP-SUPPORT-001.json`. Orkestratör planı mevcut Announcements/Support modülünü ve `ExternalRef` alanını kullanır; şema değişikliği yapılmaz. Backend ve frontend dosya sahipliği ayrıdır. Önceden var olan org-chart çalışma ağacı değişiklikleri korunur.

## Sözleşme

Yerel create cevabı ve ticket ID değişmez. `SupportTicketDto` sonuna isteğe bağlı `portal` eklenir; null alan normal JSON ayarlarıyla atlanır:

```typescript
portal?: {
  ticketId?: string;
  number?: string;
  status?: "new" | "triaged" | "in_progress" | "waiting_requester" | "waiting_internal" | "resolved" | "closed" | "cancelled";
  syncState: "pending" | "connected" | "unavailable";
  commentsTruncated?: boolean;
  comments: { id: string; body: string; createdAt: string; authorName?: string; source?: string }[];
};
```

Sahiplik denetiminden sonra `GET /v1/support/tickets/mine` ve `GET /v1/support/tickets/{localId}` merkezi bilgilerle genişletilir. Takip yanıtı `POST /v1/support/tickets/{localId}/comments`, `{body:string}` ve `Idempotency-Key: UUID` ile sahip kullanıcıya açıktır; başarı 204.

Portal sunucu köprüsü `/api/v1/tenants/{supportTenant}/projects/{supportProject}/customer-tickets` kullanır. `X-Support-Subject` sunucuda yerel kaydın `tenant:{TenantId}:user:{RequestedByUserId}` alanlarından üretilir. Create gövdesi `title`, `description`, `sourceProduct:"fphr"`, `externalReference:localGuid`, `priority:"normal"` içerir; idempotency anahtarı sabit yerel ticket Guid olur. Anahtar yalnız sunucu ortamında `FP_SUPPORT_API_KEY` değerinden alınır; API ve Worker aynı proje yapılandırmasını kullanır.

Görev tamamlandığında doğrulama, gözden geçirme ve kurulum notları buraya eklenir.

## İnceleme durumu (14 Ekim 2026)

Backend ve web uygulaması incelemeye hazır. Announcements backend modülünün 112 testi geçti; web typecheck, lint (0 hata, mevcut 2 uyarı) ve production build geçti. İlk geniş web test koşusunda bazı mevcut router-context/timeout hataları ve yeni test fixture eksiği görüldü; destek fixture’ı düzeltildi. Son kaynaklar için argümansız tam depo test kapısı ve bağımsız yeniden koşu bekleniyor; bu aşamada tam kapı PASS iddiası yapılmıyor.

Bağımsız kaynak incelemesinin bulduğu liste yorum sözleşmesi ve terminal yanıt formu sorunları düzeltildi. Merkezi liste `includeComments=1` ile son 5 açık mesajı alır; `commentsTruncated` durumunda yetkili yerel detail tam konuşmayı getirir. Kurulum: `docs/implementation/fp-support-portal.md`. Migration, sır dosyası, commit ve korumalı dala merge yapılmadı.

## Birleşik çalışma ağacı doğrulaması (14 Ekim 2026)

Kullanıcının tüm bekleyen değişiklikleri test edip commit/push etme talebi kapsamında destek bağlantısı, organizasyon şeması ve web test fixture düzeltmeleri birlikte doğrulandı.

- Geliştirici: Announcements tam test kümesi **112/112**, atlanan test yok; güncel Sense.Api/Migrator derlemesi ve Sense.Worker derlemesi **0 hata**. Web ilgili 13 dosyada **68/68** test, TypeScript, ESLint (0 hata, mevcut 2 uyarı) ve Vite/PWA başarılı.
- Bağımsız gözden geçirici: Announcements **112/112**, web destek/organizasyon şeması/fixture testleri **29/29**; TypeScript, ESLint ve Vite/PWA yeniden başarılı. İşlevsel kaynak incelemesinde engelleyici bulgu yok.
- Yeni destek JSON test örneğinin C# raw string sözdizimi düzeltildi. Devam takibi testlerine MemoryRouter eklendi; sicil düzenleme testi asenkron yüklemeyi bekler. Test veya assertion kaldırılmadı.
- Aynı çalışma ağacındaki native .NET derlemesi paket yollarını değiştirdiğinden SDK doğrulaması ayrı `senseik-support-validation-20261014:/validation` alanında, `--artifacts-path /validation` ile çalıştırıldı. Kaynak SHA değerleri test boyunca sabit kaldı.
- Argümansız `node .Codex/takim/betikler/test-kapisi.mjs` ve `kapi-dogrula.mjs` dosyaları depoda yok: `MODULE_NOT_FOUND`. Bu ortam sınırı devam ediyor; yukarıdaki gerçek test/tip/lint/build kontrolleri tam depo takım kapısı olarak sunulmuyor.
- Canlı portal bağlantısı bu koşumda kullanılmadı; HTTP sözleşmesi fake transport testleri ve portal kaynak koduyla doğrulandı. API/Worker sunucu yapılandırması için `docs/implementation/fp-support-portal.md` geçerlidir.
