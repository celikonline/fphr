// takim-kur — hedef projedeki kancaların ortak yardımcıları.
// Bu dosya /takim-kur tarafından .claude/takim/betikler/ altına kopyalanır; elle değiştirmeyin
// (manifest değişikliği fark eder). Sıfır bağımlılık, Node >= 18 (ADR-0001).
//
// Kancalar KAZA ÖNLEYİCİDİR, güvenlik sınırı değildir: bilinen aşma yolları (node -e, betik yazıp
// çalıştırma, git alias) engellenmez ve testte "engellenmiyor" diye belgelenir. Asıl koruma uzak
// depodaki dal korumasıdır (branch protection).

import { readFileSync, existsSync, realpathSync, statSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { dirname, resolve, relative, isAbsolute, basename, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const BETIK_DIZINI = dirname(fileURLToPath(import.meta.url));
const BOM_BASI = new RegExp('^' + String.fromCharCode(0xfeff)); // kaçış dizisi değil: araçlar onu görünmez karaktere çeviriyor

/** Proje kökü: Claude Code'un verdiği CLAUDE_PROJECT_DIR, yoksa <kök>/.claude/takim/betikler/ konumundan. */
export function projeKoku() {
  return resolve(process.env.CLAUDE_PROJECT_DIR || resolve(BETIK_DIZINI, '..', '..', '..'));
}

/** .claude/takim/takim.json — yoksa güvenli varsayılanlar. */
export function ayarOku(kok = projeKoku()) {
  let ayar = {};
  try {
    ayar = JSON.parse(readFileSync(resolve(kok, '.claude', 'takim', 'takim.json'), 'utf8').replace(BOM_BASI, ''));
  } catch { /* ayar yoksa varsayılanlar */ }
  return {
    korumali_dallar: Array.isArray(ayar.korumali_dallar) && ayar.korumali_dallar.length ? ayar.korumali_dallar : ['main', 'master'],
    yazilabilir_disari: Array.isArray(ayar.yazilabilir_disari) ? ayar.yazilabilir_disari : [],
  };
}

/** Claude Code kanca girdisini (stdin JSON) okur. */
export function girdiOku() {
  const ham = readFileSync(0, 'utf8');
  return JSON.parse(ham.replace(BOM_BASI, ''));
}

const ORNEK_ENV = /^\.env\.(example|sample|template|dist|defaults)$/i;

/** Sır içeren dosya mı? (yalnız ada bakar; .env.example gibi şablonlar serbest) */
export function sirDosyasiMi(yol) {
  const ad = basename(String(yol).replace(/\\/g, '/')).toLowerCase();
  if (ORNEK_ENV.test(ad)) return false;
  if (ad === '.env' || ad.startsWith('.env.')) return true;
  if (/\.(pem|key|p12|pfx|keystore|jks)$/.test(ad)) return true;
  if (/^id_(rsa|dsa|ecdsa|ed25519)(?!.*\.pub$)/.test(ad)) return true;
  if (/^secrets?\./.test(ad) || /^credentials/.test(ad)) return true;
  return false;
}

/**
 * Gerçek yol: Windows kısa adlarını (HALILE~1) ve sembolik bağları açar. Dosya henüz yoksa
 * var olan en yakın üst dizin çözülür, kalan parçalar eklenir.
 */
export function gercekYol(p) {
  let mevcut = resolve(p);
  const kalan = [];
  while (!existsSync(mevcut)) {
    const ust = dirname(mevcut);
    if (ust === mevcut) return resolve(p);
    kalan.unshift(basename(mevcut));
    mevcut = ust;
  }
  let cozulmus;
  try { cozulmus = realpathSync.native(mevcut); } catch { cozulmus = mevcut; }
  return kalan.length ? join(cozulmus, ...kalan) : cozulmus;
}

/** Yolu proje köküne göre ileri eğik çizgili göreli yola çevirir; kök dışındaysa null. */
export function kokeGoreli(yol, cwd, kok = projeKoku()) {
  const temiz = String(yol).replace(/^["']|["']$/g, '');
  const tam = gercekYol(isAbsolute(temiz) || /^[A-Za-z]:[\\/]/.test(temiz) ? temiz : resolve(cwd || kok, temiz));
  const rel = relative(gercekYol(kok), tam);
  if (rel.startsWith('..') || isAbsolute(rel)) return null;
  return rel.split(/[\\/]/).join('/');
}

const buyukKucuk = process.platform === 'win32' || process.platform === 'darwin';

/** Takımın kendini koruduğu yollar: betikler, ayar/manifest ve Claude ayar dosyaları. */
export function korunanTakimYoluMu(goreli) {
  if (goreli === null || goreli === undefined) return false;
  const g = buyukKucuk ? goreli.toLowerCase() : goreli;
  return g === '.claude/takim' || g.startsWith('.claude/takim/') || /^\.claude\/settings(\.local)?\.json$/.test(g);
}

/**
 * Çalışma ağacı İÇERİK özeti (sha256): diskteki izlenen + izlenmeyen (gitignore dışı) her dosyanın yolu ve
 * blob özeti. Kanıt kaydı bu özete bağlanır: testten sonra kod değişirse özet değişir, kanıt "bayat" olur.
 * Commit'ten bağımsızdır: aynı içerik commit'lenince kanıt geçerli kalır (OP_PANEL pilot bulgusu).
 * Blob özetleri `hash-object --no-filters` ile nesne deposuna YAZILMADAN hesaplanır.
 * .claude/takim/kanit/ hariç tutulur. Git deposu değilse null.
 */
export function agacOzeti(kok = projeKoku()) {
  const git = (a, girdi) => execFileSync('git', ['--no-optional-locks', ...a], {
    cwd: kok, input: girdi === undefined ? undefined : Buffer.from(girdi), encoding: 'buffer', stdio: [girdi === undefined ? 'ignore' : 'pipe', 'pipe', 'ignore'],
    maxBuffer: 256 * 1024 * 1024, windowsHide: true,
  });
  let dosyalar;
  try {
    dosyalar = git(['ls-files', '-z', '--cached', '--others', '--exclude-standard']).toString().split('\0');
  } catch { return null; }
  const dosyaMi = (f) => { try { return statSync(join(kok, f)).isFile(); } catch { return false; } };
  // Diskte olmayan (silinmiş) dosya listeye girmez: silme commit'lenince özet yine aynı kalır.
  dosyalar = [...new Set(dosyalar)]
    .filter((f) => f && !f.includes('\n') && !f.startsWith('.claude/takim/kanit/') && dosyaMi(f)).sort();
  const h = createHash('sha256');
  if (dosyalar.length) {
    const ozler = git(['hash-object', '--no-filters', '--stdin-paths'], dosyalar.join('\n') + '\n').toString().trim().split('\n');
    dosyalar.forEach((f, i) => h.update(`${ozler[i]} ${f}\n`));
  }
  return h.digest('hex');
}

export function engelle(kural, mesaj) {
  process.stderr.write(`takim-kur koruması [${kural}]: ${mesaj}\n`);
  process.exit(2);
}

/** Kanca kendi içinde çökerse komutu ENGELLEMEZ (fail-open, ADR-0001 md. 5) — uyarı basar. */
export function guvenliCalistir(fn) {
  try {
    fn();
    process.exit(0);
  } catch (hata) {
    process.stderr.write(`takim-kur kancası hata verdi, işlem ENGELLENMEDİ (fail-open): ${hata && hata.message}\n`);
    process.exit(0);
  }
}
