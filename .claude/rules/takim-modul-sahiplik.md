---
paths:
  - "**"
  - "**"
---
# Modül sahipliği (/takim-kur, M boyutu)

| Alan | Sahip geliştirici |
|---|---|
| `(kök, diğer alanlar hariç)` | `takim-gelistirici-dotnet` |
| `(kök, diğer alanlar hariç)` | `takim-gelistirici-node` |

- Bir alanı yalnız sahibi değiştirir; başka alana dokunmak gerekiyorsa sahibine ayrı parça olarak verilir.
- Alanlar arası veri akışında önce sözleşme (uç, alanlar, tipler) yazılır, sonra iki taraf uygulanır.
- Paylaşılan kayıt dosyaları (modül/rota/menü listeleri, bağımlılık dosyaları) aynı anda tek geliştiriciye verilir
  ve yalnız sona ekleme yapılır; mevcut satırlar yeniden sıralanmaz.
