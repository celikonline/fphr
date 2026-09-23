// takim-kur — test kapısı: "bitti"nin ajan beyanı değil, yeniden koşturulabilir kanıt olması için.
// Kopyalanır: .claude/takim/betikler/test-kapisi.mjs  (elle değiştirmeyin; /takim-kur günceller)
//
// Kullanım (izin listesinde TAM bu komut vardır; ajan argüman ekleyemez):
//   node .claude/takim/betikler/test-kapisi.mjs
//   node .claude/takim/betikler/test-kapisi.mjs --taban-guncelle   (yalnız insan: izin istemi çıkar)
//
// Komutlar YALNIZ .claude/takim/takim.json → kapi.komutlar'dan okunur (ajan filtre veremez).
// Durumlar (çıkış kodu 0 yalnız "yesil"):
//   yesil            tüm komutlar başarılı, koşan test sayısı > 0 ve tabanın altında değil
//   kirmizi          bir komut başarısız (çıkış ≠ 0) ya da zaman aşımı
//   kirletti         kapının koşusu izlenen/izlenmeyen dosyaları değiştirdi (ör. lock dosyası) — insana söylenir
//   ayristirilamadi  test çıktısından sayı çıkarılamadı → insan takim.json kapi.desenler'e desen ekler
//   sifir-test       hiç test koşmadı (filtre hiçbir testle eşleşmedi, testler silindi…)
//   taban-alti       koşan test sayısı tabanın altına düştü (silinen/.skip eklenen testler)
//   test-yok         yapılandırılmış test komutu yok — "bitti" insan onayına bağlıdır
// Her koşu .claude/takim/kanit/<agac-ozeti>.json + son.json yazar (kapi-dogrula.mjs okur).

import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { join, basename } from 'node:path';
import { spawnSync, execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { projeKoku, agacOzeti } from './ortak.mjs';

const BOM_BASI = new RegExp('^' + String.fromCharCode(0xfeff));
const ANSI = new RegExp(String.fromCharCode(27) + '\\[[0-9;?]*[A-Za-z]', 'g');
const KATEGORI_SIRASI = ['build', 'lint', 'typecheck', 'test'];

// ---------------- test sayısı ayrıştırıcıları ----------------

const topla = (metin, desen) => [...metin.matchAll(desen)].reduce((t, m) => t + Number(m[1]), 0);
const son = (metin, desen) => { const e = [...metin.matchAll(desen)]; return e.length ? e[e.length - 1] : null; };

/** Koşan (atlanan/todo hariç) test sayısı; tanınmazsa null. Sıra önemlidir: özgül desenler önce. */
export const AYRISTIRICILAR = [
  ['node:test', (m) => {
    if (!/^ℹ tests \d+/m.test(m) && !/^# tests \d+/m.test(m)) return null;
    const t = topla(m, /^[ℹ#] tests (\d+)/gm);
    return t - topla(m, /^[ℹ#] skipped (\d+)/gm) - topla(m, /^[ℹ#] todo (\d+)/gm);
  }],
  ['vitest', (m) => {
    const s = son(m, /^\s*Tests\s+(.+?)\s+\((\d+)\)\s*$/gm);
    if (!s) return null;
    const atla = (/(\d+) skipped/.exec(s[1]) || [0, 0])[1];
    const todo = (/(\d+) todo/.exec(s[1]) || [0, 0])[1];
    return Number(s[2]) - Number(atla) - Number(todo);
  }],
  ['jest', (m) => {
    const s = son(m, /^Tests:\s+(.*?)(\d+) total\s*$/gm);
    if (!s) return null;
    const atla = (/(\d+) skipped/.exec(s[1]) || [0, 0])[1];
    const todo = (/(\d+) todo/.exec(s[1]) || [0, 0])[1];
    return Number(s[2]) - Number(atla) - Number(todo);
  }],
  ['dotnet-test', (m) => {
    // VSTest: proje başına "Passed!  - Failed: 0, Passed: 12, Skipped: 1, Total: 13"
    const satirlar = [...m.matchAll(/^\s*(?:Passed|Failed)!\s+-\s+Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)/gm)];
    if (satirlar.length) return satirlar.reduce((t, x) => t + Number(x[1]) + Number(x[2]), 0);
    // Microsoft.Testing.Platform: "Test summary: total: 13, failed: 0, succeeded: 12, skipped: 1"
    const mtp = son(m, /Test summary:\s*total:\s*(\d+),\s*failed:\s*(\d+),\s*succeeded:\s*(\d+),\s*skipped:\s*(\d+)/gi);
    if (mtp) return Number(mtp[1]) - Number(mtp[4]);
    const eski = son(m, /^Total tests:\s*(\d+)/gm);
    return eski ? Number(eski[1]) : null;
  }],
  ['pytest', (m) => {
    const s = son(m, /^=+ (.*\d+ (?:passed|failed|error|errors|xfailed|xpassed).*) in [\d.]+s.*=+\s*$/gm);
    if (!s) return /^=+ no tests ran/m.test(m) ? 0 : null;
    return [...s[1].matchAll(/(\d+) (passed|failed|error|errors|xpassed)\b/g)].reduce((t, x) => t + Number(x[1]), 0);
  }],
  ['mocha', (m) => {
    if (!/^\s*\d+ passing \(/m.test(m)) return null;
    return topla(m, /^\s*(\d+) passing \(/gm) + topla(m, /^\s*(\d+) failing\s*$/gm);
  }],
  ['go-test', (m) => {
    const n = (m.match(/^\s*--- (PASS|FAIL): /gm) || []).length;
    if (n) return n;
    return /^(ok|FAIL|\?)\s+\S+/m.test(m) ? (/no test files|no tests to run/.test(m) && !/^ok\s+\S+\s+[\d.]+s\s*$/m.test(m) ? 0 : null) : null;
  }],
  ['maven', (m) => {
    const s = son(m, /Tests run:\s*(\d+),\s*Failures:\s*\d+,\s*Errors:\s*\d+,\s*Skipped:\s*(\d+)/g);
    return s ? Number(s[1]) - Number(s[2]) : null;
  }],
];

/** Çıktıdan koşan test sayısını çıkarır. ozelDesen: ilk yakalama grubu sayıdır (eşleşmeler toplanır). */
export function testSayisi(cikti, ozelDesen) {
  const m = String(cikti).replace(ANSI, '').replace(/\r\n/g, '\n');
  if (ozelDesen) {
    const d = new RegExp(ozelDesen, 'gm');
    const e = [...m.matchAll(d)];
    return e.length ? { sayi: e.reduce((t, x) => t + Number(x[1]), 0), ayristirici: 'ozel' } : null;
  }
  for (const [ad, f] of AYRISTIRICILAR) {
    const sayi = f(m);
    if (sayi !== null && Number.isFinite(sayi)) return { sayi, ayristirici: ad };
  }
  return null;
}

// ---------------- koşu ----------------

function ayarOku(kok) {
  const yol = join(kok, '.claude', 'takim', 'takim.json');
  if (!existsSync(yol)) return null;
  return JSON.parse(readFileSync(yol, 'utf8').replace(BOM_BASI, ''));
}

function gitDurumu(kok) {
  try {
    return execFileSync('git', ['--no-optional-locks', 'status', '--porcelain', '--untracked-files=all'], {
      cwd: kok, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], windowsHide: true,
    }).split('\n').filter((l) => l && !l.slice(3).startsWith('.claude/takim/kanit/')).sort();
  } catch {
    return null;
  }
}

function kos(komut, kok, zamanAsimiSn) {
  const bas = Date.now();
  // CI=true: vitest/jest izleme moduna girmez. NODE_TEST_CONTEXT silinir: kapı bir Node test koşucusunun
  // içinden çağrılırsa (ör. kendi testlerimiz) iç "node --test" çıktıyı okunabilir metin yerine ikili
  // protokolle üst sürece yollar ve test sayısı ayrıştırılamaz.
  const env = { ...process.env, CI: 'true', FORCE_COLOR: '0', NO_COLOR: '1' };
  delete env.NODE_TEST_CONTEXT;
  const r = spawnSync(komut, {
    cwd: kok, shell: true, encoding: 'utf8', timeout: zamanAsimiSn * 1000, maxBuffer: 64 * 1024 * 1024,
    windowsHide: true, env,
  });
  const cikti = `${r.stdout || ''}\n${r.stderr || ''}`;
  return {
    cikis: r.error && r.error.code === 'ETIMEDOUT' ? 'zaman-asimi' : r.status,
    sure_ms: Date.now() - bas,
    cikti,
  };
}

export function kapiyiKos(kok = projeKoku(), { tabanGuncelle = false } = {}) {
  const ayar = ayarOku(kok);
  if (!ayar) return { durum: 'kurulu-degil', sorunlar: ['.claude/takim/takim.json yok — önce /takim-kur'] };
  const kapi = ayar.kapi || {};
  const komutlar = [...(kapi.komutlar || [])].sort((a, b) => KATEGORI_SIRASI.indexOf(a.kategori) - KATEGORI_SIRASI.indexOf(b.kategori));
  const zamanAsimi = Number(kapi.zaman_asimi_sn) || 900;
  const desenler = kapi.desenler || {};

  const onceki = gitDurumu(kok);
  const sonuclar = [];
  for (const k of komutlar) {
    const r = kos(k.komut, kok, zamanAsimi);
    const sonuc = { kategori: k.kategori, komut: k.komut, cikis: r.cikis, sure_ms: r.sure_ms,
      cikti_sha256: createHash('sha256').update(r.cikti).digest('hex'),
      cikti_kuyruk: r.cikti.replace(ANSI, '').trimEnd().split('\n').slice(-30).join('\n') };
    if (k.kategori === 'test') {
      const s = testSayisi(r.cikti, desenler[k.komut]);
      sonuc.test_sayisi = s ? s.sayi : null;
      sonuc.ayristirici = s ? s.ayristirici : null;
    }
    sonuclar.push(sonuc);
  }
  const sonraki = gitDurumu(kok);

  const testler = sonuclar.filter((s) => s.kategori === 'test');
  const toplam = testler.reduce((t, s) => t + (s.test_sayisi || 0), 0);
  const taban = Number.isFinite(kapi.taban_sayi) ? kapi.taban_sayi : null;
  const sorunlar = [];
  const basarisiz = sonuclar.filter((s) => s.cikis !== 0);
  if (basarisiz.length) sorunlar.push(...basarisiz.map((s) => `kirmizi: "${s.komut}" çıkış ${s.cikis}`));
  const degisen = onceki && sonraki ? sonraki.filter((l) => !onceki.includes(l)).concat(onceki.filter((l) => !sonraki.includes(l))) : [];
  if (degisen.length) sorunlar.push(`kirletti: kapı koşusu dosya değiştirdi → ${degisen.map((l) => l.slice(3)).join(', ')}`);
  const ayrisamayan = testler.filter((s) => s.test_sayisi === null);
  if (ayrisamayan.length) sorunlar.push(...ayrisamayan.map((s) => `ayristirilamadi: "${s.komut}" çıktısından test sayısı çıkarılamadı (takim.json kapi.desenler'e desen eklenmeli)`));
  if (testler.length && !ayrisamayan.length && toplam === 0) sorunlar.push('sifir-test: hiç test koşmadı');
  if (taban !== null && testler.length && !ayrisamayan.length && toplam > 0 && toplam < taban) {
    sorunlar.push(`taban-alti: koşan test ${toplam} < taban ${taban} (silinen ya da atlanan test?)`);
  }

  let durum;
  if (!testler.length) durum = basarisiz.length ? 'kirmizi' : 'test-yok';
  else if (basarisiz.length) durum = 'kirmizi';
  else if (degisen.length) durum = 'kirletti';
  else if (ayrisamayan.length) durum = 'ayristirilamadi';
  else if (toplam === 0) durum = 'sifir-test';
  else if (taban !== null && toplam < taban) durum = 'taban-alti';
  else durum = 'yesil';

  // Taban, ağaç özetinden ÖNCE yazılır: takim.json da ağacın parçası; sonra yazılsa kanıt anında bayatlardı
  // (BIRIKSIN_CMS pilot bulgusu).
  let tabanGuncellendi;
  if (tabanGuncelle) {
    if (durum === 'yesil' || (durum === 'taban-alti' && !basarisiz.length)) {
      ayar.kapi = { ...kapi, taban_sayi: toplam };
      writeFileSync(join(kok, '.claude', 'takim', 'takim.json'), JSON.stringify(ayar, null, 2) + '\n');
      tabanGuncellendi = { eski: taban, yeni: toplam };
    } else {
      tabanGuncellendi = null;
    }
  }

  const ozet = agacOzeti(kok);
  const kanit = {
    surum: 1, zaman: new Date().toISOString(), durum, toplam_test: toplam, taban_sayi: taban,
    agac_ozeti: ozet, sorunlar, komutlar: sonuclar,
  };
  const kanitDizini = join(kok, '.claude', 'takim', 'kanit');
  mkdirSync(kanitDizini, { recursive: true });
  const metin = JSON.stringify(kanit, null, 2) + '\n';
  if (ozet) writeFileSync(join(kanitDizini, `${ozet.slice(0, 16)}.json`), metin);
  writeFileSync(join(kanitDizini, 'son.json'), metin);
  if (tabanGuncelle) kanit.taban_guncellendi = tabanGuncellendi;
  return kanit;
}

// ---------------- giriş noktası ----------------

if (process.argv[1] && basename(process.argv[1]) === 'test-kapisi.mjs') {
  const tabanGuncelle = process.argv.includes('--taban-guncelle');
  const k = kapiyiKos(projeKoku(), { tabanGuncelle });
  const satirlar = [`takim-kur test kapısı: ${k.durum.toUpperCase()}`];
  if (k.komutlar) {
    for (const s of k.komutlar) {
      satirlar.push(`  [${s.kategori}] ${s.komut} → çıkış ${s.cikis}${s.kategori === 'test' ? `, koşan test: ${s.test_sayisi ?? 'ayrıştırılamadı'}` : ''} (${Math.round(s.sure_ms / 1000)} sn)`);
    }
    satirlar.push(`  toplam koşan test: ${k.toplam_test}${k.taban_sayi !== null ? ` (taban ${k.taban_sayi})` : ''}`);
  }
  for (const s of k.sorunlar || []) satirlar.push(`  ! ${s}`);
  if (k.taban_guncellendi) satirlar.push(`  taban güncellendi: ${k.taban_guncellendi.eski} → ${k.taban_guncellendi.yeni}`);
  if (k.agac_ozeti) satirlar.push(`  kanıt: .claude/takim/kanit/${k.agac_ozeti.slice(0, 16)}.json`);
  if (k.durum === 'yesil') satirlar.push('  Bu kanıt yalnız şu anki çalışma ağacı için geçerlidir; kod değişirse kapı yeniden koşturulmalı.');
  if (k.durum === 'test-yok') satirlar.push('  Test yok: "bitti" için build + gözden geçirici + insan onayı gerekir. İlk iş: test iskeleti.');
  console.log(satirlar.join('\n'));
  process.exit(k.durum === 'yesil' ? 0 : k.durum === 'kurulu-degil' ? 3 : 1);
}
