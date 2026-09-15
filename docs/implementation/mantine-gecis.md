# Web: shadcn/ui → Mantine geçişi

**Karar (2026-09-15):** Web uygulaması (`web/`) shadcn/ui kopyası bileşenlerden (Radix + Tailwind + cva, `web/src/components/ui/*`) Mantine 9'a kademeli olarak taşınır. Mobil uygulama kapsam dışıdır.

## Temel (hazır)

- Paketler: `@mantine/core`, `hooks`, `dates` (+ `dayjs`), `notifications`, `dropzone`, `charts` (+ `recharts`); PostCSS: `postcss-preset-mantine`, `postcss-simple-vars`.
- `web/src/components/mantine-root.tsx`: `MantineProvider` (tema + UI store'daki light/dark/system), `DatesProvider` (i18n dilinde dayjs locale, hafta pazartesi başlar), `Notifications`.
- `web/src/lib/mantine-theme.ts`: `brandBlue` birincil renk (`--primary`), `algosenseGreen` (login gibi marka yüzeyleri), Inter, `md` radius. Mantine body/text/border/dimmed değişkenleri `styles/index.css` token'larına bağlıdır.
- CSS katman sırası `theme, base, mantine, components, utilities`: Tailwind preflight Mantine'in altında, Tailwind utility'leri üstünde kalır.
- `toast()` (`@/hooks/use-toast`) artık Mantine notifications üzerinden çalışır; çağıran kod değişmez (`variant: "success" | "destructive"`).

## Kurallar

- Sahip olduğun dosyalarda `@/components/ui/<primitive>` importlarının hepsi Mantine'e çevrilir. Geçiş boyunca kalabilecek uygulama bileşenleri: `ui/data-table`, `ui/date-picker`, `ui/time-picker`, `ui/file-dropzone`, `components/pagination-footer`, `components/confirm-dialog` (bunların içleri ayrıca Mantine'e taşınır, dışa açık API'leri korunur).
- İkonlar `lucide-react` olarak kalır. Tailwind yalnızca yerleşim/boşluk için kullanılabilir (`flex`, `grid`, `gap-*`, `p-*`, `max-w-*`); renk, kenarlık, tipografi ve bileşen görünümü Mantine prop'larıyla verilir (`c="dimmed"`, `fw={500}`, `size="sm"`, `withBorder`).
- Form alanlarında ayrı `Label` + hata `<p>` yerine Mantine'in `label`, `description`, `error`, `withAsterisk` prop'ları kullanılır; `id` korunursa testler/erişilebilirlik bozulmaz.
- Davranış, izinler, sorgu anahtarları, çeviri anahtarları ve servis çağrıları değişmez. Yeni çeviri anahtarı gerekiyorsa tr/en/de üçüne birden eklenir (de: `"[DE] English text"`).
- Mantine `Select` değeri `string | null` döndürür; boş seçenek için `clearable` veya `allowDeselect={false}` bilinçli seçilir.

## Eşleme

| shadcn (`@/components/ui/...`) | Mantine |
|---|---|
| `Button` | `Button`: `default`→`variant="filled"`, `outline`→`"default"`, `secondary`→`"light"`, `ghost`→`"subtle"`, `destructive`→`color="red"`, `link`→`Anchor` / `variant="transparent"`; `asChild` + `Link` → `component={Link} to=...`; `size="icon"` → `ActionIcon` (+ `Tooltip label=...`, `aria-label`) |
| `Input` + `Label` | `TextInput` / `NumberInput` / `PasswordInput` (`label`, `error`) |
| `Textarea` | `Textarea` (`autosize` isteğe bağlı) |
| `Select` (Radix) | `Select` (`data=[{ value, label }]`) |
| `Checkbox` | `Checkbox` (`checked`, `indeterminate`, `onChange={(e) => …e.currentTarget.checked}`) |
| `Switch` | `Switch` |
| `RadioGroup` | `Radio.Group` + `Radio` |
| `Card*` | `Card withBorder` / `Paper withBorder`; başlık `Title order={4}` veya `Text fw={600}`, açıklama `Text c="dimmed" size="sm"` |
| `Dialog*` | `Modal` (`opened`, `onClose`, `title`); alt butonlar `Group justify="flex-end"` |
| `Sheet` | `Drawer` |
| `Tabs*` | `Tabs` / `Tabs.List` / `Tabs.Tab` / `Tabs.Panel` |
| `Accordion*` | `Accordion` |
| `DropdownMenu*` | `Menu` / `Menu.Target` / `Menu.Dropdown` / `Menu.Item` / `Menu.Label` / `Menu.Divider` |
| `Popover*` | `Popover` / `Popover.Target` / `Popover.Dropdown` |
| `Tooltip*` | `Tooltip` |
| `Badge` | `Badge`: `default`→`filled`, `secondary`→`light`, `outline`→`outline`, `destructive`→`color="red"`, `success`→`color="green"` |
| `Alert*` | `Alert` (`color`, `icon`, `title`) |
| `Avatar*` | `Avatar` (`src`, `alt`, çocuk olarak baş harfler) |
| `Skeleton` | `Skeleton` |
| `Separator` | `Divider` |
| `Table*` | `Table` / `Table.Thead` / `Table.Tbody` / `Table.Tr` / `Table.Th` / `Table.Td` (liste ekranları `ui/data-table` kullanır) |
| `ScrollArea` | `ScrollArea` / `ScrollArea.Autosize mah={…}` |
| `Breadcrumb*` | `Breadcrumbs` + `Anchor component={Link}` |
| `Command*` (arama seçicileri) | `Combobox` + `useCombobox` (sunucu araması korunur) |
| `Calendar` / `DatePicker` | `@mantine/dates` (`DatePickerInput`, `TimeInput`, `TimePicker`); değerler `YYYY-MM-DD` string |

## Durum

**Tamamlandı (2026-09-15).** Tüm ekranlar Mantine'e taşındı. `web/src/components/ui/` altında yalnızca Mantine üzerine kurulu uygulama bileşenleri kaldı (`data-table`, `date-picker`, `time-picker`, `file-dropzone`); shadcn primitive'leri, tüm `@radix-ui/*` paketleri, `class-variance-authority`, `cmdk`, `react-day-picker`, `tw-animate-css` ve `TooltipProvider` kaldırıldı. Yeni ekranlarda doğrudan Mantine kullanılır; shadcn/Radix bileşeni eklenmez. Yukarıdaki eşleme tablosu eski kodu okurken başvuru içindir.
